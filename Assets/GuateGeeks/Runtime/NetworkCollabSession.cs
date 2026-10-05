using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    [Serializable] public sealed class RoomGrant { public string token, userId, roomId, role, websocketUrl; public int station; public long expiresAt; }
    [Serializable] public sealed class RoomMember { public string userId, name, role; public int station; }
    [Serializable] public sealed class RoomLease { public string objectId, owner, token; public long expiresAt; }
    [Serializable] public sealed class RoomPose { public Vector3 head, hand, pointAt; public Quaternion rotation; public string focusId; public bool pointing; }
    [Serializable] public sealed class RoomDeployment { public string deploymentId, stackId, status, fingerprint; public int revision; public bool finished, success; public long expiresAt; }
    [Serializable] public sealed class RoomMessage
    {
        public string type, roomId, hostId, requestId, message, userId, focusId;
        public int revision, roomVersion;
        public bool accepted, pointing;
        public Architecture graph;
        public RoomMember[] members;
        public RoomLease[] locks;
        public RoomPose pose;
        public RoomDeployment deployment;
    }
    [Serializable] public sealed class RoomCommand
    {
        public string action, requestId, objectId, leaseToken;
        public int baseRevision;
        public Architecture graph;
        public RoomPose pose;
        public RoomLease[] leases;
        public bool global;
    }

    // Tasks only handle bytes. Unity objects and session state are updated by Pump on the main thread.
    // One ordered send queue, one receive loop, bounded buffers and cancellation on close.
    public sealed class NetworkCollabSession : ICollabSession, IDisposable
    {
        readonly ConcurrentQueue<string> received = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<string> outgoing = new ConcurrentQueue<string>();
        readonly List<PeerState> peers = new List<PeerState>();
        readonly Dictionary<string, RoomPose> targets = new Dictionary<string, RoomPose>();
        readonly Dictionary<string, RoomLease> leases = new Dictionary<string, RoomLease>();
        readonly HashSet<string> claims = new HashSet<string>();
        CancellationTokenSource cancellation;
        ClientWebSocket socket;
        volatile bool connected, failed;
        bool disposed;
        int queuedReceived, queuedOutgoing;
        float lastTick, lastSnapshot;
        public RoomGrant Grant { get; }
        public bool Connected => connected && !failed && !disposed;
        public bool Ready { get; private set; }
        public string Mode => Connected && Ready ? "CONECTADA" : "DESCONECTADA";
        public IReadOnlyList<PeerState> Peers => peers;
        public int Version { get; private set; }
        public string LocalHold { get; private set; }
        public int Revision { get; private set; }
        public Architecture Graph { get; private set; }
        public RoomMessage LastSnapshot { get; private set; }
        public bool IsFacilitator => LastSnapshot != null && LastSnapshot.hostId == Grant.userId;
        public bool CanWrite => Connected && Ready && Time.unscaledTime - lastSnapshot < 12;
        public event Action<RoomMessage> Snapshot;
        public event Action LostConnection;
        public NetworkCollabSession(RoomGrant grant) { Grant = grant; }

        public async Task Connect(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "wss" || uri.Host != new Uri(Grant.websocketUrl).Host)
                throw new ArgumentException("URL de sala inválida.");
            cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(TimeSpan.FromSeconds(15));
            socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            try
            {
                await socket.ConnectAsync(uri, cancellation.Token);
                if (disposed) return;
                cancellation.CancelAfter(Timeout.Infinite);
                connected = true;
                _ = ReceiveLoop(); _ = SendLoop();
                Send(new RoomCommand { action = "sync" });
            }
            catch { failed = true; throw; }
        }
        async Task ReceiveLoop()
        {
            var buffer = new byte[8192];
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    using (var stream = new MemoryStream())
                    {
                        WebSocketReceiveResult part;
                        do
                        {
                            part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token);
                            if (part.MessageType == WebSocketMessageType.Close) { failed = true; return; }
                            if (part.MessageType != WebSocketMessageType.Text || stream.Length + part.Count > 30000) throw new InvalidDataException();
                            stream.Write(buffer, 0, part.Count);
                        } while (!part.EndOfMessage);
                        if (Interlocked.Increment(ref queuedReceived) > 128) throw new InvalidDataException();
                        received.Enqueue(Encoding.UTF8.GetString(stream.ToArray()));
                    }
                }
            }
            catch (Exception) { if (!disposed) failed = true; }
        }
        async Task SendLoop()
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    if (outgoing.TryDequeue(out var json))
                    {
                        Interlocked.Decrement(ref queuedOutgoing);
                        var bytes = Encoding.UTF8.GetBytes(json);
                        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation.Token);
                    }
                    else await Task.Delay(10, cancellation.Token);
                }
            }
            catch (Exception) { if (!disposed) failed = true; }
        }
        public bool Send(RoomCommand command)
        {
            if (!Connected) return false;
            if (Interlocked.Increment(ref queuedOutgoing) > 32) { Interlocked.Decrement(ref queuedOutgoing); failed = true; return false; }
            outgoing.Enqueue(JsonUtility.ToJson(command)); return true;
        }
        public void Pump()
        {
            while (received.TryDequeue(out var json))
            {
                Interlocked.Decrement(ref queuedReceived);
                try
                {
                    var message = JsonUtility.FromJson<RoomMessage>(json);
                    if (message.type == "presence")
                    {
                        var peer = peers.FirstOrDefault(p => p.Id == message.userId);
                        if (peer != null && message.pose != null)
                        { targets[peer.Id] = message.pose; peer.FocusId = message.focusId; peer.Pointing = message.pointing; }
                        continue;
                    }
                    if (message.type != "snapshot" || message.graph == null || message.roomId != Grant.roomId) continue;
                    if (message.revision < Revision || LastSnapshot != null && message.roomVersion < LastSnapshot.roomVersion)
                    {
                        // A later broadcast may overtake an operation receipt. Complete that receipt
                        // using the newest state rather than reverting state or losing the acknowledgment.
                        if (!string.IsNullOrEmpty(message.requestId) && LastSnapshot != null)
                        {
                            var receipt = JsonUtility.FromJson<RoomMessage>(JsonUtility.ToJson(LastSnapshot));
                            receipt.requestId = message.requestId; receipt.accepted = message.accepted; receipt.message = message.message;
                            Snapshot?.Invoke(receipt);
                        }
                        continue;
                    }
                    LastSnapshot = message; Revision = message.revision; Graph = message.graph;
                    var local = message.members?.FirstOrDefault(m => m.userId == Grant.userId);
                    if (local == null) { failed = true; break; }
                    Grant.station = local.station;
                    var previous = peers.ToDictionary(p => p.Id);
                    peers.Clear();
                    foreach (var member in message.members)
                    {
                        if (member.userId == Grant.userId) continue;
                        if (!previous.TryGetValue(member.userId, out var peer))
                        {
                            peer = new PeerState(member.userId, member.name, member.station, false);
                            peer.Head = SharedSpace.StationPosition(member.station) + Vector3.up * 1.65f;
                            peer.Hand = peer.Head + Vector3.down * .4f;
                        }
                        peer.Station = member.station; peers.Add(peer);
                    }
                    leases.Clear();
                    foreach (var lease in message.locks ?? Array.Empty<RoomLease>()) leases[lease.objectId] = lease;
                    claims.Clear(); LocalHold = leases.Values.FirstOrDefault(l => l.owner == Grant.userId && Live(l))?.objectId;
                    foreach (var peer in peers) peer.Activity = leases.Values.Any(l => l.owner == peer.Id && Live(l)) ? "EDITA" : "OBSERVA";
                    Ready = true; lastSnapshot = Time.unscaledTime; Version++; Snapshot?.Invoke(message);
                }
                catch (ArgumentException) { failed = true; }
            }
            if (Ready && Connected && Time.unscaledTime - lastSnapshot >= 12) failed = true;
            if (failed && connected)
            { connected = false; Ready = false; cancellation?.Cancel(); socket?.Abort(); Version++; LostConnection?.Invoke(); }
        }
        static bool Live(RoomLease lease) => lease.expiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public PeerState LockOwner(string objectId)
        {
            return objectId != null && leases.TryGetValue(objectId, out var lease) && Live(lease) && lease.owner != Grant.userId
                ? peers.FirstOrDefault(p => p.Id == lease.owner) : null;
        }
        public bool Claim(string objectId)
        {
            if (!CanWrite || objectId == null) return false;
            if (leases.TryGetValue(objectId, out var lease) && lease.owner == Grant.userId && Live(lease)) return true;
            if (LockOwner(objectId) != null || claims.Contains(objectId)) return false;
            if (Send(new RoomCommand { action = "claim", objectId = objectId, requestId = Guid.NewGuid().ToString("N") })) claims.Add(objectId);
            return false;
        }
        public void Release(string objectId)
        {
            if (objectId != null && leases.TryGetValue(objectId, out var lease) && lease.owner == Grant.userId)
                Send(new RoomCommand { action = "release", objectId = objectId, leaseToken = lease.token, requestId = Guid.NewGuid().ToString("N") });
        }
        public RoomLease[] OwnLeases() => leases.Values.Where(l => l.owner == Grant.userId && Live(l)).ToArray();
        public void SetLocalStation(int station) { /* Membership reserves stations on the server. */ }
        public void Tick(float now, IReadOnlyDictionary<string, Vector3> objects)
        {
            float step = lastTick == 0 ? 1 : 1 - Mathf.Exp(-Mathf.Max(0, now - lastTick) / .1f); lastTick = now;
            foreach (var peer in peers)
                if (targets.TryGetValue(peer.Id, out var pose))
                {
                    peer.Head = Vector3.Lerp(peer.Head, pose.head, step); peer.Hand = Vector3.Lerp(peer.Hand, pose.hand, step);
                    peer.HeadRotation = Quaternion.Slerp(peer.HeadRotation, pose.rotation, step); peer.PointAt = pose.pointAt;
                }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; connected = false; Ready = false;
            cancellation?.Cancel(); socket?.Abort(); socket?.Dispose();
            // Loops may still observe the cancellation source; do not dispose it underneath them.
        }
    }
}
