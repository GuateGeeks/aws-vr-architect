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
        string roomPending, roomPendingRelease, roomResult;
        float roomSentAt, roomNextPresence, roomNextHeartbeat, roomNextLeaseCleanup, roomIdleSince, roomNextAwsStatus;
        bool roomApplying, roomConnecting, roomGlobal, roomReadingStatus;
        bool roomTalkLatched;
        Coroutine roomConnection;
        AwsCloudApi roomBroker;
        TMPro.TMP_Text roomCodeText;
        LabTarget roomCreate, roomJoin, roomReconnect, roomLeave;

        static string RoomGraphJson(Architecture graph)
        {
            if (graph == null) return "";
            var clean = graph.Copy(); clean.ResetStates(); clean.nodes = clean.nodes.OrderBy(n => n.id).ToList(); clean.links = clean.links.OrderBy(l => l.from).ThenBy(l => l.to).ToList(); return JsonUtility.ToJson(clean);
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
            yield return roomBroker.CreateRoom(code, name, Graph.Copy(), (value, message) => { grant = value; error = message; });
            roomConnecting = false;
            if (grant == null) { roomConnection = null; SetStatus(error ?? "No se pudo crear la sala."); RefreshNetworkRoomControls(); yield break; }
            yield return ConnectRoom(grant);
        }
        IEnumerator ConnectRoom(RoomGrant grant)
        {
            roomConnecting = true; RefreshNetworkRoomControls();
            NetworkRoom?.Dispose();
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
            if (Cloud != null) Cloud.RoomToken = null;
            history.Clear(); historyScales.Clear(); teamVersion = -1; RefreshNetworkRoomControls(); RefreshRoomSettings();
            SetStatus("Fuera de la sala · conservas una copia local del diseño.");
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
        }
        void ReceiveRoomSnapshot(RoomMessage message)
        {
            var room = NetworkRoom;
            if (room == null) return;
            bool changed = roomAccepted == null || RoomGraphJson(roomAccepted) != RoomGraphJson(message.graph);
            roomAccepted = message.graph.Copy();
            if (message.requestId == roomPending)
            {
                roomResult = message.accepted ? "applied" : "rejected";
                roomPending = null;
                if (roomPendingRelease != null) { room.Release(roomPendingRelease); roomPendingRelease = null; }
                SetStatus(message.accepted ? "Cambio confirmado por la sala · revisión " + message.revision : message.message);
            }
            if (changed) RestoreRoomGraph();
            revision = message.revision;
            Space.SetSharedRoom(true);
            if (Space.Station != room.Grant.station) Space.SetStation(room.Grant.station);
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
            roomApplying = true;
            var selectedId = selected ? selected.Model.id : null;
            try { Rig?.ReleaseForConfiguration(); SetGraph(roomAccepted.Copy());
                if (selectedId != null && views.TryGetValue(selectedId, out var view)) { selected = view; RefreshSelection(); }
                revision = NetworkRoom?.Revision ?? revision; history.Clear(); historyScales.Clear(); }
            finally { roomApplying = false; }
        }
        // Existing local editing paths generate a candidate. Before rendering the next frame,
        // restore committed state and submit it once. Only a server snapshot commits the change.
        void LateUpdate()
        {
            var room = NetworkRoom;
            if (room == null || roomApplying || roomAccepted == null) return;
            if (!views.Values.Any(v => v.Grabbed) && RoomGraphJson(Graph) != RoomGraphJson(roomAccepted))
            {
                var candidate = Graph.Copy();
                if (room.CanWrite && !RoomEditPending)
                {
                    roomPending = Guid.NewGuid().ToString("N"); roomResult = "pending"; roomSentAt = Time.unscaledTime;
                    room.Send(new RoomCommand { action = "op", requestId = roomPending, baseRevision = room.Revision, graph = candidate, leases = room.OwnLeases(), global = roomGlobal });
                }
                roomGlobal = false; RestoreRoomGraph();
                SetStatus(RoomEditPending ? "Esperando confirmación de la sala…" : "Sala en lectura · reconecta antes de editar.");
            }
            if (!RoomEditPending && RoomGraphJson(Graph) == RoomGraphJson(roomAccepted)) roomGlobal = false;
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
            if (!RoomEditPending && roomPendingRelease == null && RoomGraphJson(Graph) == RoomGraphJson(roomAccepted) && !views.Values.Any(v => v.Grabbed))
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
                room.Send(new RoomCommand { action = "presence", pose = new RoomPose {
                    head = camera.position, rotation = camera.rotation, hand = Rig.PresenceHand,
                    pointAt = SharedSpace.Center, pointing = false, focusId = "" } });
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
