using System.Collections.Generic;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // Presence and object ownership for up to four people at one table. The lab and its UI only talk to
    // ICollabSession, so the networked session planned for the next phase (API Gateway WebSocket + Lambda,
    // locks as DynamoDB conditional writes with a TTL) replaces the local and simulated sessions here
    // without touching panels, avatars or lock visuals.
    public sealed class PeerState
    {
        public string Id, Name, Label, LockText, Activity = "";
        public int Station;
        public bool Simulated;
        public Vector3 Head, Hand, PointAt;
        public Quaternion HeadRotation = Quaternion.identity;
        public bool Pointing;
        public string FocusId;
        public Color Color => SharedSpace.Colors[Mathf.Clamp(Station, 0, SharedSpace.MaxStations - 1)];
        public PeerState(string id, string name, int station, bool simulated)
        {
            Id = id; Name = name; Station = station; Simulated = simulated;
            Label = (station + 1) + " · " + name; LockText = "EN USO · " + name;
        }
    }
    public interface ICollabSession
    {
        string Mode { get; }
        IReadOnlyList<PeerState> Peers { get; }
        // Increments whenever membership or a lock changes, so UI can refresh without polling strings.
        int Version { get; }
        // The person holding an object, or null when it is free or held by this headset.
        PeerState LockOwner(string objectId);
        string LocalHold { get; }
        // Exclusive editing of one object (the local selection). False when another person holds it.
        bool Claim(string objectId);
        void SetLocalStation(int station);
        void Tick(float now, IReadOnlyDictionary<string, Vector3> objects);
    }

    // One headset, no peers: claims always succeed. This is the default and the fallback when offline.
    public sealed class LocalCollabSession : ICollabSession
    {
        static readonly PeerState[] none = new PeerState[0];
        public string Mode => "LOCAL";
        public IReadOnlyList<PeerState> Peers => none;
        public int Version { get; private set; }
        public string LocalHold { get; private set; }
        public PeerState LockOwner(string objectId) => null;
        public bool Claim(string objectId) { if (LocalHold != objectId) { LocalHold = objectId; Version++; } return true; }
        public void SetLocalStation(int station) { }
        public void Tick(float now, IReadOnlyDictionary<string, Vector3> objects)
        {
            if (LocalHold != null && !objects.ContainsKey(LocalHold)) { LocalHold = null; Version++; }
        }
    }

    // Three rehearsal teammates at the other stations: they look around, point at objects and take short
    // editing locks, so presence, colours and lock rules can be designed and tested before networking exists.
    // Deterministic (fixed seed) so tests and screenshots are repeatable. They never change the design.
    public sealed class SimulatedCollabSession : ICollabSession
    {
        const string Observe = "OBSERVA · SIM", Point = "SEÑALA · SIM", Edit = "EDITA · SIM";
        static readonly string[] Names = { "ANA", "LUIS", "SOFÍA" };
        sealed class Bot { public PeerState peer; public float next, phase; public string held; }
        readonly List<PeerState> peers = new List<PeerState>();
        readonly List<Bot> bots = new List<Bot>();
        readonly Dictionary<string, PeerState> locks = new Dictionary<string, PeerState>();
        readonly List<string> candidates = new List<string>();
        readonly System.Random random = new System.Random(2026);
        public string Mode => "SIMULADA";
        public IReadOnlyList<PeerState> Peers => peers;
        public int Version { get; private set; }
        public string LocalHold { get; private set; }
        public SimulatedCollabSession(int localStation) { SetLocalStation(localStation); }
        public PeerState LockOwner(string objectId) => objectId != null && locks.TryGetValue(objectId, out var owner) ? owner : null;
        public bool Claim(string objectId)
        {
            if (objectId != null && locks.ContainsKey(objectId)) return false;
            if (LocalHold != objectId) { LocalHold = objectId; Version++; }
            return true;
        }
        public void SetLocalStation(int station)
        {
            peers.Clear(); bots.Clear(); locks.Clear(); int n = 0;
            for (int s = 0; s < SharedSpace.MaxStations; s++)
            {
                if (s == station) continue;
                var peer = new PeerState("sim-" + (s + 1), Names[n++], s, true) { Activity = Observe };
                peers.Add(peer); bots.Add(new Bot { peer = peer, phase = s * 1.7f, next = .4f * n });
            }
            Version++;
        }
        // Rehearsal hook for tests: make a teammate hold an object now.
        public bool ForceLock(int peerIndex, string objectId)
        {
            if (peerIndex < 0 || peerIndex >= bots.Count || objectId == null || objectId == LocalHold || locks.ContainsKey(objectId)) return false;
            var bot = bots[peerIndex]; Release(bot);
            bot.held = objectId; locks[objectId] = bot.peer; bot.peer.FocusId = objectId; bot.peer.Pointing = true; bot.peer.Activity = Edit;
            bot.next = float.MaxValue; Version++; return true;
        }
        public void Tick(float now, IReadOnlyDictionary<string, Vector3> objects)
        {
            if (LocalHold != null && !objects.ContainsKey(LocalHold)) { LocalHold = null; Version++; }
            foreach (var bot in bots)
            {
                var peer = bot.peer;
                if (peer.FocusId != null && !objects.ContainsKey(peer.FocusId)) { Release(bot); peer.FocusId = null; peer.Pointing = false; bot.next = now; }
                if (now >= bot.next) Choose(bot, now, objects);
                var stand = SharedSpace.StationPosition(peer.Station);
                peer.Head = stand + new Vector3(Mathf.Sin(now * .6f + bot.phase) * .07f, 1.62f + Mathf.Sin(now * .9f + bot.phase) * .012f, Mathf.Cos(now * .45f + bot.phase) * .05f);
                var target = peer.FocusId != null ? objects[peer.FocusId] : SharedSpace.Center + new Vector3(Mathf.Sin(now * .2f + bot.phase) * .6f, 1.25f, 0);
                var look = target - peer.Head;
                peer.HeadRotation = Quaternion.Slerp(peer.HeadRotation, Quaternion.LookRotation(look.sqrMagnitude > .01f ? look : Vector3.forward), .08f);
                var body = Quaternion.Euler(0, peer.HeadRotation.eulerAngles.y, 0);
                peer.Hand = peer.Head + body * new Vector3(.2f, -.38f, .3f);
                peer.PointAt = target;
            }
        }
        void Choose(Bot bot, float now, IReadOnlyDictionary<string, Vector3> objects)
        {
            var peer = bot.peer; bool hadLock = bot.held != null; Release(bot);
            candidates.Clear(); foreach (var key in objects.Keys) candidates.Add(key);
            double roll = random.NextDouble();
            peer.FocusId = null; peer.Pointing = false; peer.Activity = Observe;
            if (candidates.Count > 0)
            {
                string pick = candidates[random.Next(candidates.Count)];
                if (roll < .3) { peer.FocusId = pick; }
                else if (roll < .65) { peer.FocusId = pick; peer.Pointing = true; peer.Activity = Point; }
                else if (pick != LocalHold && !locks.ContainsKey(pick))
                { peer.FocusId = pick; peer.Pointing = true; peer.Activity = Edit; bot.held = pick; locks[pick] = peer; }
            }
            bot.next = now + (bot.held != null ? 4f : 2.2f) + (float)random.NextDouble() * 3f;
            if (hadLock || bot.held != null) Version++;
        }
        void Release(Bot bot) { if (bot.held != null) { locks.Remove(bot.held); bot.held = null; Version++; } }
    }
}
