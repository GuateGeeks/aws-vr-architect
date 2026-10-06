using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    // Shared room for up to four people: stations, the personal console, presence and object locks.
    public sealed partial class ArchitectureLab
    {
        public SharedSpace Space { get; private set; }
        public ICollabSession Collab { get; private set; } = new LocalCollabSession();
        public PeerAvatars Peers { get; private set; }
        public bool SimulatingPeers => Collab is SimulatedCollabSession;
        Transform PersonalRoot => Space ? Space.Console : world;
        public string StatusMessage => statusText ? statusText.text : "";
        public NodeView SelectedView => selected;
        readonly Dictionary<string, Vector3> collabObjects = new Dictionary<string, Vector3>();
        readonly LabTarget[] stationButtons = new LabTarget[SharedSpace.MaxStations];
        readonly StringBuilder teamBuilder = new StringBuilder(256);
        TMPro.TMP_Text teamText, sharedRoomSetting, simulateSetting, roomStatusText;
        int teamVersion = -1;

        void BuildSharedSpace()
        {
            Space = gameObject.AddComponent<SharedSpace>(); Space.Initialize(this, world);
            Space.Changed += OnSharedSpaceChanged;
        }
        void BuildCollaboration() { Peers = PeerAvatars.Create(world, this); }
        void OnSharedSpaceChanged()
        {
            Collab.SetLocalStation(Space.Station);
            if (!Space.SharedRoom && SimulatingPeers) Collab = new LocalCollabSession();
            ClaimSelection(); teamVersion = -1; RefreshRoomSettings();
            SetStatus(Space.SharedRoom
                ? "Sala compartida · " + SharedSpace.StationName(Space.Station) + ". Párate en tu círculo mirando la mesa y pulsa «Alinear a mi estación»."
                : Table.Size == TableSize.Large ? "Modo individual: la consola vuelve a su distribución completa."
                : "Modo individual · mesa " + Table.Label + ": consola compacta frente a la mesa.");
        }
        public void SetSharedRoom(bool shared) { if (NetworkRoom != null || roomConnecting) { SetStatus("Sal de la sala real antes de cambiar la distribución."); return; } if (Space) Space.SetSharedRoom(shared); RefreshRoomSettings(); }
        public void SetStation(int station) { if (NetworkRoom != null || roomConnecting) { SetStatus("La estación está reservada por el servidor."); return; } if (Space) Space.SetStation(station); RefreshRoomSettings(); }
        public void SetSimulatedPeers(bool simulate)
        {
            if (NetworkRoom != null || roomConnecting) { SetStatus("Sal de la sala real antes de iniciar un ensayo."); return; }
            if (simulate == SimulatingPeers) return;
            if (simulate && Space && !Space.SharedRoom) Space.SetSharedRoom(true);
            Collab = simulate ? new SimulatedCollabSession(Space ? Space.Station : 0) : (ICollabSession)new LocalCollabSession();
            ClaimSelection(); teamVersion = -1; RefreshRoomSettings();
            SetStatus(simulate ? "Ensayo con 3 compañeros simulados: verás su presencia, punteros y objetos en uso." : "Simulación de equipo apagada.");
        }
        public void AlignToStation()
        {
            if (Rig) Rig.Recenter();
            SetStatus(Space && Space.SharedRoom ? "Alineado a " + SharedSpace.StationName(Space.Station) + ". Mantente dentro de tu círculo." : "Vista centrada.");
        }
        // The person holding this object, when it is not you.
        public bool LockedByOther(NodeView view, out PeerState owner)
        {
            owner = view ? Collab.LockOwner(view.Model.id) : null; return owner != null;
        }
        bool RejectLocked(NodeView view)
        {
            if (!LockedByOther(view, out var owner)) return false;
            SetStatus(owner.Name + " está editando " + view.Model.name + ". Puedes verlo; podrás editarlo cuando lo suelte.");
            Feedback.Play(LabFeedback.Error); return true;
        }
        void ClaimSelection()
        {
            // Selection is private. Only moving/editing requires a lease.
        }
        void TickCollab()
        {
            var room = NetworkRoom;
            if (room != null)
            {
                float blend = 1 - Mathf.Exp(-Time.unscaledDeltaTime / .065f);
                foreach (var pair in views)
                {
                    var view = pair.Value;
                    if (!view || view.Grabbed) continue;
                    var position = room.TryGetObjectPosition(pair.Key, out var preview) ? preview : view.Model.position;
                    view.transform.localPosition = Vector3.Lerp(view.transform.localPosition, position, blend);
                    if ((view.transform.localPosition - position).sqrMagnitude < .000001f) view.transform.localPosition = position;
                }
            }
            collabObjects.Clear();
            foreach (var pair in views) if (pair.Value) collabObjects[pair.Key] = pair.Value.transform.position;
            Collab.Tick(Time.unscaledTime, collabObjects);
            foreach (var pair in views) if (pair.Value) pair.Value.LockedBy = Collab.LockOwner(pair.Key);
            if (Collab.Version != teamVersion) { teamVersion = Collab.Version; RefreshTeamStrip(); }
        }
        void RefreshTeamStrip()
        {
            if (!teamText) return;
            bool shared = Space && Space.SharedRoom;
            teamText.gameObject.SetActive(shared); if (!shared) return;
            teamBuilder.Clear().Append("EQUIPO   ");
            for (int s = 0; s < SharedSpace.MaxStations; s++)
            {
                string name = null; bool editing = false;
                if (s == Space.Station) { name = "TÚ"; editing = Collab.LocalHold != null; }
                else foreach (var peer in Collab.Peers) if (peer.Station == s) { name = peer.Name; editing = HoldsAny(peer); }
                teamBuilder.Append("<color=").Append(SharedSpace.ColorHex[s]).Append(">●</color> ").Append(s + 1).Append(' ')
                    .Append(name ?? "<color=#5E7688>libre</color>").Append(editing ? " · EDITA" : "").Append("     ");
            }
            teamBuilder.Append("<color=#8FA9BC>· RED ").Append(Collab.Mode).Append("</color>");
            teamText.text = teamBuilder.ToString();
        }
        bool HoldsAny(PeerState peer) { foreach (var key in views.Keys) if (Collab.LockOwner(key) == peer) return true; return false; }

        void BuildRoomSettings(Transform page)
        {
            Text(page, "SALA COMPARTIDA · HASTA 4 PERSONAS", new Vector2(0, 335), new Vector2(980, 44), 26, Cyan, TextAnchor.MiddleCenter);
            Text(page, "Distribución para cuatro: consola privada y estaciones alrededor de la mesa. Para conectar visores usa «Crear sala real» o «Unirme con código». La sala reserva tu estación; luego alinea tu visor.",
                new Vector2(0, 250), new Vector2(980, 110), 22, White, TextAnchor.MiddleCenter);
            sharedRoomSetting = Button(page, "Distribución para 4: NO", new Vector2(0, 150), new Vector2(980, 60), () => SetSharedRoom(!(Space && Space.SharedRoom))).Label;
            for (int s = 0; s < SharedSpace.MaxStations; s++)
            {
                int station = s;
                stationButtons[s] = Button(page, SharedSpace.StationName(s), new Vector2((s - 1.5f) * 245, 65), new Vector2(230, 64), () => SetStation(station), SharedSpace.Colors[s]);
            }
            Button(page, "Alinear a mi estación", new Vector2(0, -20), new Vector2(980, 60), AlignToStation);
            simulateSetting = Button(page, "Simular 4 usuarios: NO", new Vector2(0, -100), new Vector2(980, 60), () => SetSimulatedPeers(!SimulatingPeers)).Label;
            roomStatusText = Text(page, "", new Vector2(0, -187), new Vector2(980, 85), 19, Muted, TextAnchor.MiddleCenter);
            BuildNetworkRoomControls(page);
            RefreshRoomSettings();
        }
        void RefreshRoomSettings()
        {
            bool shared = Space && Space.SharedRoom;
            if (sharedRoomSetting) sharedRoomSetting.text = "Distribución para 4: " + (shared ? "SÍ" : "NO");
            if (simulateSetting) simulateSetting.text = "Simular 4 usuarios: " + (SimulatingPeers ? "SÍ" : "NO");
            for (int s = 0; s < stationButtons.Length; s++)
                if (stationButtons[s]) stationButtons[s].Label.text = SharedSpace.StationName(s) + (Space && s == Space.Station ? "  ●" : "");
            string table = "Mesa " + Table.Label + (shared ? " · estaciones a " + Table.StationLabel + " del centro" : "");
            if (roomStatusText) roomStatusText.text = NetworkRoom != null ? "Estación reservada · " + SharedSpace.StationName(Space.Station) + ". " + table + " (la elige el facilitador). Alinéate mirando al centro. Conversación y paneles personales privados."
                : shared
                ? "Estás en la " + SharedSpace.StationName(Space.Station) + ". " + table + ". Al salir de tu círculo verás un aviso.\n" + (SimulatingPeers ? "Ensayo simulado · no hay otros visores conectados." : "Distribución local · aún no conectaste una sala real.")
                : "Modo individual · " + table + ": una persona, consola " + (Table.Size == TableSize.Large ? "completa" : "compacta") + " y giro por stick disponible.";
            teamVersion = -1;
        }
    }
}
