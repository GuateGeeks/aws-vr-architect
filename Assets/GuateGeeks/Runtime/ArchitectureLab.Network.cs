using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    public sealed partial class ArchitectureLab
    {
        public NetworkCollabSession NetworkRoom => Collab as NetworkCollabSession;
        public bool RoomEditPending => !string.IsNullOrEmpty(roomPending);
        public bool RoomReadOnly => roomConnecting || NetworkRoom != null && (!NetworkRoom.CanWrite || RoomEditPending || RoomDeploymentBusy);
        public bool RoomDeploymentBusy => NetworkRoom?.LastSnapshot?.deployment != null && !NetworkRoom.LastSnapshot.deployment.finished && NetworkRoom.LastSnapshot.deployment.expiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Architecture roomAccepted;
        Architecture roomPendingGraph;
        string roomPendingMove;
        int roomMoveSequence;
        string roomPending, roomPendingRelease, roomResult;
        float roomSentAt, roomNextPresence, roomNextHeartbeat, roomNextLeaseCleanup, roomIdleSince, roomNextAwsStatus;
        bool roomApplying, roomConnecting, roomGlobal, roomReadingStatus;
        bool roomTalkLatched;
        Coroutine roomConnection;
        AwsCloudApi roomBroker;
        TMPro.TMP_Text roomCodeText;
        LabTarget roomCreate, roomJoin, roomReconnect, roomLeave;

        static bool RoomGraphsEqual(Architecture a, Architecture b)
        {
            if (a == null || b == null) return a == b;
            if (a.schemaVersion != b.schemaVersion || a.region != b.region || a.nodes.Count != b.nodes.Count || a.links.Count != b.links.Count) return false;
            foreach (var node in a.nodes)
            {
                ResourceNode other = null;
                foreach (var candidate in b.nodes) if (candidate.id == node.id) { other = candidate; break; }
                if (other == null || node.kind != other.kind || node.name != other.name || node.setting != other.setting ||
                    !node.position.Equals(other.position) || node.viewScale != other.viewScale) return false;
            }
            foreach (var edge in a.links)
            {
                bool found = false;
                foreach (var other in b.links) if (edge.from == other.from && edge.to == other.to) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }
        void BuildNetworkRoomControls(Transform page)
        {
            roomCodeText = Text(page, "SIN CONEXIÓN ENTRE VISORES", new Vector2(0, -267), new Vector2(980, 34), 19, Cyan, TextAnchor.MiddleCenter);
            roomCreate = Button(page, "Crear sala real", new Vector2(-250, -322), new Vector2(460, 55), () => RoomNamePrompt(""));
            roomJoin = Button(page, "Unirme con código", new Vector2(250, -322), new Vector2(460, 55), () => {
                if (NetworkRoom != null || roomConnecting) return;
                OpenDesignKeyboard("CÓDIGO DE SALA · 8 CARACTERES", "", 8, code => RoomNamePrompt(code.Trim().ToUpperInvariant()));
            });
            roomReconnect = Button(page, "Reconectar sala", new Vector2(-250, -390), new Vector2(460, 55), () => {
                if (NetworkRoom != null && !roomConnecting) roomConnection = StartCoroutine(ConnectRoom(NetworkRoom.Grant));
            });
            roomLeave = Button(page, "Salir de la sala", new Vector2(250, -390), new Vector2(460, 55), LeaveNetworkRoom);
            RefreshNetworkRoomControls();
        }
        void RoomNamePrompt(string code)
        {
            if (NetworkRoom != null || roomConnecting) return;
            if (!IsCloud || !Cloud.Connected) { SetStatus("Conecta la API AWS antes de crear o unirte a una sala real."); return; }
            OpenDesignKeyboard("TU NOMBRE EN LA SALA", "INVITADO", 24, name => {
                if (!roomConnecting) roomConnection = StartCoroutine(BootstrapRoom(code, name));
            });
        }
        IEnumerator BootstrapRoom(string code, string name)
        {
            roomConnecting = true; RefreshNetworkRoomControls();
            roomBroker = Cloud.CreateInspectionReader();
            RoomGrant grant = null; string error = null;
            // A new room starts with this headset's table; joining takes the room's.
            yield return roomBroker.CreateRoom(code, name, Graph.Copy(), Table.Size, (value, message) => { grant = value; error = message; });
            roomConnecting = false;
            if (grant == null) { roomConnection = null; SetStatus(error ?? "No se pudo crear la sala."); RefreshNetworkRoomControls(); yield break; }
            yield return ConnectRoom(grant);
        }
        IEnumerator ConnectRoom(RoomGrant grant)
        {
            roomConnecting = true; RefreshNetworkRoomControls();
            NetworkRoom?.Dispose();
            roomPending = roomPendingRelease = roomPendingMove = null; roomPendingGraph = null;
            roomBroker?.Disconnect(); roomBroker = Cloud?.CreateInspectionReader();
            if (roomBroker == null) { roomConnecting = false; roomConnection = null; RefreshNetworkRoomControls(); yield break; }
            AwsCloudApi.RoomTicket ticket = null; string error = null;
            yield return roomBroker.GetRoomTicket(grant.token, (value, message) => { ticket = value; error = message; });
            var session = new NetworkCollabSession(grant);
            Collab = session; roomAccepted = Graph.Copy();
            session.Snapshot += ReceiveRoomSnapshot;
            session.LostConnection += () => {
                Rig?.ReleaseForConfiguration(); roomPending = null; RestoreRoomGraph();
                SetStatus("Sala desconectada · diseño en lectura. Pulsa Reconectar sala."); RefreshNetworkRoomControls();
            };
            if (ticket != null && !string.IsNullOrEmpty(ticket.ticket))
            {
                var task = session.Connect(ticket.websocketUrl + "?ticket=" + Uri.EscapeDataString(ticket.ticket));
                while (!task.IsCompleted) yield return null;
                if (task.IsFaulted || task.IsCanceled) error = "No se pudo conectar la sala. Revisa internet e IP permitida.";
                else
                {
                    float until = Time.unscaledTime + 10;
                    while (!session.Ready && session.Connected && Time.unscaledTime < until) { session.Pump(); yield return null; }
                    if (!session.Ready) error = "La sala no confirmó el estado inicial. Reconecta.";
                }
            }
            else error = error ?? "No se recibió un ticket de sala.";
            roomConnecting = false; roomConnection = null;
            if (error != null) { session.Dispose(); SetStatus(error); }
            else
            {
                Cloud.RoomToken = grant.token; roomBroker.RoomToken = grant.token;
                Space.SetSharedRoom(true); Space.SetStation(grant.station);
                SetStatus("Sala " + grant.roomId + " conectada · " + SharedSpace.StationName(grant.station) + ". Alinea tu visor.");
            }
            roomNextPresence = roomNextHeartbeat = 0; teamVersion = -1; RefreshNetworkRoomControls();
        }
        void LeaveNetworkRoom()
        {
            if (roomConnecting && roomConnection != null) StopCoroutine(roomConnection);
            roomConnection = null; roomConnecting = false;
            roomTalkLatched = false;
            Rig?.ReleaseForConfiguration(); NetworkRoom?.Dispose(); roomBroker?.Disconnect(); roomBroker = null;
            Collab = new LocalCollabSession(); roomAccepted = null; roomPending = null; roomPendingRelease = null;
            roomPendingGraph = null; roomPendingMove = null;
            if (Cloud != null) Cloud.RoomToken = null;
            history.Clear(); historyScales.Clear(); teamVersion = -1; RestoreLocalTable(); RefreshNetworkRoomControls(); RefreshRoomSettings();
            SetStatus("Fuera de la sala · conservas una copia local del diseño" + (Table.Size == TableSize.Large ? "." : " y tu mesa " + Table.Label + "."));
        }
        void RefreshNetworkRoomControls()
        {
            var room = NetworkRoom;
            if (roomCodeText) roomCodeText.text = room == null ? (roomConnecting ? "CONECTANDO…" : "SIN CONEXIÓN ENTRE VISORES")
                : "SALA " + room.Grant.roomId + " · " + room.Mode + (room.IsFacilitator ? " · FACILITADOR" : " · EDITOR");
            roomCreate?.SetAvailable(room == null && !roomConnecting);
            roomJoin?.SetAvailable(room == null && !roomConnecting);
            roomReconnect?.SetAvailable(room != null && !room.Connected && !roomConnecting);
            roomLeave?.SetAvailable(room != null && !roomConnecting);
            foreach (var button in stationButtons) button?.SetAvailable(room == null && !roomConnecting);
            RefreshTableSettings();
        }
        void ReceiveRoomSnapshot(RoomMessage message)
        {
            var room = NetworkRoom;
            if (room == null) return;
            roomAccepted = message.graph.Copy();
            if (RoomEditPending && message.requestId == roomPending)
            {
                roomResult = message.accepted ? "applied" : "rejected";
                roomPending = null; roomPendingGraph = null; roomPendingMove = null;
                if (roomPendingRelease != null) { room.Release(roomPendingRelease); roomPendingRelease = null; }
                SetStatus(message.accepted ? "Cambio confirmado por la sala · revisión " + message.revision : message.message);
            }
            // Membership/heartbeat snapshots must never cancel a grab. Apply
            // graph changes in place and keep our candidate visible until receipt.
            if (views.Values.Any(v => v.Grabbed && !room.HasLease(v.Model.id)))
            {
                Rig?.ReleaseForConfiguration();
                foreach (var view in views.Values) view.Grabbed = false;
                roomPendingRelease = null;
                SetStatus("La reserva del objeto venció. Vuelve a agarrarlo para moverlo.");
            }
            ReconcileRoomGraph();
            revision = message.revision;
            Space.SetSharedRoom(true);
            if (Space.Station != room.Grant.station) Space.SetStation(room.Grant.station);
            ReceiveRoomTable(message);
            RefreshNetworkRoomControls();
            if(message.deployment != null && message.deployment.finished && !message.deployment.success) Deployed = false;
            if(message.deployment != null && message.deployment.success) {
                // Layout-only edits keep AWS status; definition changes invalidate it.
                Deployed = message.deployment.revision == message.revision;
                if (Deployed) {
                    Cloud?.AdoptRoomDeployment(message.deployment, message.graph);
                    foreach (var node in Graph.nodes) node.state = ResourceState.Ready;
                    UpdateCounts();
                }
            }
        }
        void RestoreRoomGraph()
        {
            if (roomAccepted == null) return;
            Rig?.ReleaseForConfiguration();
            foreach (var view in views.Values) view.Grabbed = false;
            roomPendingGraph = null; roomPendingMove = null; roomPendingRelease = null;
            ReconcileRoomGraph();
        }
        void ReconcileRoomGraph()
        {
            if (roomAccepted == null || RoomEditPending && roomPendingMove == null) return;
            roomApplying = true;
            try
            {
                var next = roomAccepted.Copy();
                var localMove = roomPendingMove ?? roomPendingRelease;
                if (localMove != null)
                {
                    var pending = RoomEditPending ? roomPendingGraph?.Find(localMove) : Graph?.Find(localMove);
                    var node = next.Find(localMove);
                    if (pending != null && node != null) node.position = pending.position;
                }
                bool definitionChanged = Graph == null || DesignSemantics.Definition(Graph) != DesignSemantics.Definition(next);
                bool linksChanged = Graph == null || Graph.links.Count != next.links.Count ||
                    Graph.links.Any(e => !next.links.Any(n => n.from == e.from && n.to == e.to));
                foreach (var key in views.Keys.ToArray())
                {
                    var node = next.Find(key);
                    if (node != null && node.kind == views[key].Model.kind) continue;
                    var old = views[key]; if (selected == old) selected = null;
                    if (connectionSource == key) { connectionSource = null; ConnectingMode = false; HideConnectionPreview(); }
                    old.gameObject.SetActive(false); Destroy(old.gameObject); views.Remove(key); linksChanged = true;
                }
                for (int i = 0; i < next.nodes.Count; i++)
                {
                    var node = next.nodes[i];
                    if (!views.TryGetValue(node.id, out var view)) { CreateView(node); linksChanged = true; continue; }
                    var model = view.Model;
                    node.state = model.state;
                    model.name = node.name; model.setting = node.setting; model.position = node.position; model.viewScale = node.viewScale;
                    next.nodes[i] = model;
                    view.transform.localScale = Vector3.one * EffectiveScale(model);
                }
                Graph = next;
                if (linksChanged) RebuildLinks();
                if (definitionChanged) { Deployed = false; Graph.ResetStates(); UpdateButtons(); RefreshSelection(); ShowInspector(); }
                revision = NetworkRoom?.Revision ?? revision; history.Clear(); historyScales.Clear(); UpdateCounts();
            }
            finally { roomApplying = false; }
        }
        // Render the candidate immediately. The authoritative graph is kept separately;
        // only rejection or uncertain delivery reconciles back to committed state.
        void LateUpdate()
        {
            var room = NetworkRoom;
            if (room == null || roomApplying || roomAccepted == null) return;
            if (!RoomEditPending && !views.Values.Any(v => v.Grabbed) && !RoomGraphsEqual(Graph, roomAccepted))
            {
                var candidate = Graph.Copy();
                if (room.CanWrite && !RoomEditPending)
                {
                    roomPending = Guid.NewGuid().ToString("N"); roomResult = "pending"; roomSentAt = Time.unscaledTime;
                    roomPendingGraph = candidate;
                    var command = new RoomCommand { action = "op", requestId = roomPending, baseRevision = room.Revision, graph = candidate, leases = room.OwnLeases(), global = roomGlobal };
                    if (roomPendingRelease != null && !roomGlobal)
                    {
                        var moved = candidate.Find(roomPendingRelease);
                        var expected = roomAccepted.Copy(); var before = expected.Find(roomPendingRelease);
                        var lease = room.OwnLeases().FirstOrDefault(l => l.objectId == roomPendingRelease);
                        if (moved != null && before != null && lease != null)
                        {
                            before.position = moved.position;
                            if (RoomGraphsEqual(expected, candidate))
                            {
                                roomPendingMove = moved.id;
                                command.action = "move"; command.objectId = moved.id; command.position = moved.position; command.leaseToken = lease.token; command.graph = null;
                            }
                        }
                    }
                    room.Send(command);
                }
                roomGlobal = false;
                if (!RoomEditPending) RestoreRoomGraph();
                SetStatus(RoomEditPending ? "Esperando confirmación de la sala…" : "Sala en lectura · reconecta antes de editar.");
            }
            if (!RoomEditPending && RoomGraphsEqual(Graph, roomAccepted)) roomGlobal = false;
            if (RoomEditPending && Time.unscaledTime - roomSentAt > 10)
            {
                // Delivery is uncertain: reconnect/sync instead of retrying the edit.
                room.Dispose(); roomPending = null; roomResult = "unknown";
                RestoreRoomGraph(); SetStatus("Confirmación no recibida. Reconecta para consultar el resultado; no se reenvió el cambio."); RefreshNetworkRoomControls();
            }
            if (!RoomEditPending && roomPendingRelease != null) { room.Release(roomPendingRelease); roomPendingRelease = null; }
        }
        void TickNetworkRoom()
        {
            var room = NetworkRoom;
            assistantVoice?.SetPushToTalk(room != null, (roomTalkLatched || Rig && Rig.AssistantTalkHeld) && !ConfiguringConnection && !EditingText);
            if (room == null) return;
            room.Pump();
            if (!room.Connected || !room.Ready) return;
            float now = Time.unscaledTime;
            var deployment = room.LastSnapshot?.deployment;
            if (deployment != null && !deployment.finished && !Busy && !roomReadingStatus && roomBroker != null && now >= roomNextAwsStatus)
            {
                roomNextAwsStatus = now + 5; StartCoroutine(RefreshSharedAwsStatus(deployment.deploymentId));
            }
            // A claim may arrive after the grip was released. Do not keep that unused lease alive.
            if (!RoomEditPending && roomPendingRelease == null && !(Rig && Rig.AwaitingObjectGrab) && RoomGraphsEqual(Graph, roomAccepted) && !views.Values.Any(v => v.Grabbed))
            {
                if (roomIdleSince == 0) roomIdleSince = now;
                if (now - roomIdleSince > 2 && now >= roomNextLeaseCleanup)
                {
                    roomNextLeaseCleanup = now + 2;
                    foreach (var lease in room.OwnLeases()) room.Release(lease.objectId);
                }
            }
            else roomIdleSince = 0;
            if (now >= roomNextHeartbeat) { roomNextHeartbeat = now + 5; room.Send(new RoomCommand { action = "heartbeat" }); }
            if (now >= roomNextPresence && Rig && Rig.ViewCamera)
            {
                roomNextPresence = now + .1f;
                var camera = Rig.ViewCamera.transform;
                // World coordinates match the station alignment; no private speech/selection is sent.
                var pose = new RoomPose {
                    head = camera.position, rotation = camera.rotation, hand = Rig.PresenceHand,
                    pointAt = SharedSpace.Center, pointing = false, focusId = "" };
                var held = views.Values.FirstOrDefault(v => v.Grabbed);
                var lease = held ? room.OwnLeases().FirstOrDefault(l => l.objectId == held.Model.id) : null;
                if (held && lease != null)
                {
                    pose.objectId = held.Model.id; pose.leaseToken = lease.token;
                    pose.objectPosition = held.transform.localPosition; pose.revision = room.Revision; pose.moveSequence = ++roomMoveSequence;
                }
                room.Send(new RoomCommand { action = "presence", pose = pose });
            }
        }
        IEnumerator RefreshSharedAwsStatus(string slot)
        {
            roomReadingStatus = true;
            try { yield return roomBroker.RefreshRoomDeployment(slot); }
            finally { roomReadingStatus = false; }
        }
        bool UndoRoomOperation()
        {
            var room = NetworkRoom;
            if (room == null) return false;
            if (room.CanWrite && !RoomEditPending)
            {
                roomPending = Guid.NewGuid().ToString("N"); roomResult = "pending"; roomSentAt = Time.unscaledTime;
                room.Send(new RoomCommand { action = "undo", requestId = roomPending, baseRevision = room.Revision, leases = room.OwnLeases() });
                SetStatus("Solicitando deshacer tu última operación…");
            }
            return true;
        }
    }
}
