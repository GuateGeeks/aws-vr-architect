using System;
using System.Collections.Generic;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    // Up to four people share one physical booth around the projection table. Every headset renders the same
    // table, holograms and numbered floor stations; each person's panels live on a personal console that turns
    // to their station and, in a shared room, compacts to arm's reach so it never reaches into a neighbour's
    // sector (90° each). Alignment maps the physical head pose onto the chosen station: everyone stands in their
    // circle facing the table and presses «Alinear a mi estación». Networking replaces only ICollabSession.
    //
    // The compact console is a near-field cockpit: compact panels within arm's reach around the station, defined
    // as physical offsets from the eyes — controls as a desk tilted toward the eyes in front of the waist, the
    // catalog and the inspector as wings at chest height, settings and ATLAS popping up between them — so every
    // button can be touched with a finger without raising or fully extending an arm. It is used in a shared room,
    // on the smaller tables and, alone at the full table, in the headset. Desktop rehearsal keeps the panoramic
    // console (a monitor cannot show arm's-reach panels); «Paneles» in settings overrides the automatic choice.
    public sealed class SharedSpace : MonoBehaviour
    {
        public const int MaxStations = 4;
        public const string SharedPreference = "GuateGeeks.Room.Shared.v1", StationPreference = "GuateGeeks.Room.Station.v1";
        public const string LayoutPreference = "GuateGeeks.Layout.v1";
        public const int LayoutAuto = 0, LayoutNear = 1, LayoutPanoramic = 2;
        public const float SoloDistance = 3.15f, SharedDistance = 2.6f, ZoneRadius = .75f, EyeHeight = 1.65f;
        // Personal panels stay inside ±MaxAngle of the station's forward axis and within reach of a controller ray.
        public const float MaxAngle = 40, NearDistance = .75f, FarDistance = 1.35f;
        public static readonly Vector3 Center = new Vector3(0, 0, 2.65f);
        // Compact layouts are authored for station 1 of the full table; smaller tables move and scale them from here.
        static readonly Vector3 AuthoredEye = new Vector3(0, EyeHeight, Center.z - SharedDistance);
        public static readonly Color[] Colors = { Cyan, Orange, Hex("#B38CFF"), Hex("#B8F25C") };
        public static readonly string[] ColorHex = { "#5CE1F2", "#FFB24F", "#B38CFF", "#B8F25C" };
        public static SharedSpace Current { get; private set; }
        public static bool SharedRoomActive => Current && Current.SharedRoom;
        public bool SharedRoom { get; private set; }
        public int Station { get; private set; }
        public bool OutsideZone { get; private set; }
        public Transform Console { get; private set; }
        // Table size (1 m, 3 m or the full 3.9 m). Stations keep their gap to the rim, so they move with it.
        public TableLayout Table { get; private set; } = TableLayout.Full;
        public float StationDistance => Table.StationDistance;
        public static float ActiveStationDistance => Current ? Current.StationDistance : SharedDistance;
        public static TableLayout ActiveTable => Current ? Current.Table : TableLayout.Full;
        // The full solo console is laid out around the 3.9 m table; with a smaller table you stand at station 1's spot
        // and use the compact console, as in a shared room (but with snap turn, no floor circles and no zone guard).
        public bool CompactConsole => SharedRoom || Table.Size != TableSize.Large || NearCockpit;
        // Alone at the full table: the cockpit when chosen, or automatically while the headset is on.
        public int LayoutChoice { get; private set; }
        public bool NearCockpit => LayoutChoice == LayoutNear || LayoutChoice == LayoutAuto && lab && lab.Rig && lab.Rig.IsXR;
        public bool NearField => CompactConsole;
        public static Vector3 SoloStand => Center + new Vector3(0, 0, -SoloDistance);
        public string LayoutKey => !CompactConsole ? "" : Table.Size == TableSize.Small ? "shared.small." : "shared.";
        public float Yaw => SharedRoom ? StationYaw(Station) : 0;
        public Quaternion Rotation => Quaternion.Euler(0, Yaw, 0);
        public Vector3 StandPosition => CompactConsole ? StationPosition(SharedRoom ? Station : 0, StationDistance) : Center + new Vector3(0, 0, -SoloDistance);
        public Color LocalColor => Colors[Station];
        public int MarkerCount => markers.Count;
        // Editor tests have no headset: they can still evaluate the zone guard against the desktop camera.
        public bool EvaluateZoneWithoutXR;
        public event Action Changed;
        public static float StationYaw(int station) => station * 90f;
        public static Vector3 StationPosition(int station) => StationPosition(station, ActiveStationDistance);
        public static Vector3 StationPosition(int station, float distance) =>
            Center + Quaternion.Euler(0, StationYaw(station), 0) * new Vector3(0, 0, -distance);
        public static string StationName(int station) => "Estación " + (station + 1);

        struct Home { public Vector3 position; public Quaternion rotation; public Vector3 scale; }
        sealed class Marker { public Transform root; public LineRenderer ring, inner; public TMPro.TMP_Text you; }
        readonly Dictionary<Transform, Home> homes = new Dictionary<Transform, Home>();
        readonly List<Transform> adopted = new List<Transform>();
        readonly List<Marker> markers = new List<Marker>();
        ArchitectureLab lab;
        Transform world, markerRoot;
        RectTransform identity, guard;
        TMPro.TMP_Text guardText;
        Home identityHome;
        bool identityPlaced;
        float guardSince = -1;

        public void Initialize(ArchitectureLab owner, Transform labWorld)
        {
            lab = owner; world = labWorld; Current = this; Table = owner ? owner.Table : TableLayout.Full;
            SharedRoom = PlayerPrefs.GetInt(SharedPreference, 0) == 1;
            Station = Mathf.Clamp(PlayerPrefs.GetInt(StationPreference, 0), 0, MaxStations - 1);
            LayoutChoice = Mathf.Clamp(PlayerPrefs.GetInt(LayoutPreference, LayoutAuto), LayoutAuto, LayoutPanoramic);
            Console = new GameObject("Personal console").transform; Console.SetParent(world, false);
            BuildMarkers(); ApplyConsole();
        }
        void OnDestroy() { if (Current == this) Current = null; }

        public void RegisterIdentity(RectTransform header)
        {
            identity = header;
            identityHome = new Home { position = header.localPosition, rotation = header.localRotation, scale = header.localScale };
        }
        public void SetSharedRoom(bool shared)
        {
            if (SharedRoom == shared) return;
            SharedRoom = shared; PlayerPrefs.SetInt(SharedPreference, shared ? 1 : 0); PlayerPrefs.Save();
            Apply();
        }
        public void SetStation(int station)
        {
            station = Mathf.Clamp(station, 0, MaxStations - 1);
            bool changed = station != Station;
            Station = station; PlayerPrefs.SetInt(StationPreference, station); PlayerPrefs.Save();
            if (changed || SharedRoom) Apply();
        }
        // The lab decides how people get there: alone it recentres you, in a shared room you walk to your circle.
        public void SetTable(TableLayout table)
        {
            if (table.Size == Table.Size) return;
            Table = table; ApplyConsole();
        }
        public void SetLayoutChoice(int choice)
        {
            LayoutChoice = Mathf.Clamp(choice, LayoutAuto, LayoutPanoramic);
            PlayerPrefs.SetInt(LayoutPreference, LayoutChoice); PlayerPrefs.Save();
            // Alone, the stand moves with the layout (cockpit at the station spot, panoramic further back).
            Refresh(); if (!SharedRoom && lab && lab.Rig) lab.Rig.Recenter();
        }
        // Re-place every personal panel: the headset started or stopped, or the layout choice changed.
        public void Refresh() { if (Console) ApplyConsole(); }
        void Apply()
        {
            ApplyConsole();
            if (lab && lab.Rig) lab.Rig.Recenter();
            Changed?.Invoke();
        }
        // The console's home pose: turned to the station; on a smaller table also moved in with it and scaled about
        // the eye (same visual angle).
        void PoseConsole()
        {
            var rotation = Rotation;
            float k = CompactConsole ? Table.ConsoleScale : 1;
            var eye = CompactConsole ? new Vector3(0, EyeHeight, Center.z - StationDistance) : AuthoredEye;
            Console.localScale = Vector3.one * k; Console.localRotation = rotation;
            Console.localPosition = Center - rotation * Center + rotation * (eye - AuthoredEye * k);
        }
        // Back home after «Traer aquí» (centring and restoring panels call this).
        public void ResetAnchor() { if (Console) PoseConsole(); }
        // «Traer aquí»: alone, the whole console (and the poses saved in it) moves so its eye point is where the
        // person's eyes are, facing where they look — the cockpit also adopts their real eye height. In a shared
        // room the console must stay on the station for co-location, so it only returns home.
        public bool BringTo(Transform head)
        {
            if (!Console || !head || !world) return false;
            if (SharedRoom) { PoseConsole(); return false; }
            float k = CompactConsole ? Table.ConsoleScale : 1;
            var reference = CompactConsole ? AuthoredEye : SoloStand + Vector3.up * EyeHeight;
            var eye = world.InverseTransformPoint(head.position);
            eye.y = CompactConsole ? Mathf.Clamp(eye.y, EyeHeight - .45f, EyeHeight + .35f) : EyeHeight;
            var rotation = Quaternion.Euler(0, head.eulerAngles.y - world.eulerAngles.y, 0);
            Console.localScale = Vector3.one * k; Console.localRotation = rotation;
            Console.localPosition = eye - rotation * (reference * k);
            return true;
        }
        void ApplyConsole()
        {
            PoseConsole();
            adopted.RemoveAll(t => !t || t.parent != Console);
            foreach (var panel in adopted) Place(panel, homes[panel]);
            if (markerRoot) markerRoot.gameObject.SetActive(SharedRoom);
            for (int s = 0; s < markers.Count; s++) markers[s].root.localPosition = StationPosition(s) + Vector3.up * .012f;
            RefreshMarkers();
            identityPlaced = false; OutsideZone = false;
            if (guard) guard.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            // Panels can be created lazily (ATLAS, code studio, guide…): adopt any new console child once.
            for (int i = 0; i < Console.childCount; i++)
            {
                var child = Console.GetChild(i);
                if (!homes.ContainsKey(child)) Adopt(child);
            }
            UpdateIdentity(); UpdateZone();
        }
        void Adopt(Transform panel)
        {
            var home = new Home { position = panel.localPosition, rotation = panel.localRotation, scale = panel.localScale };
            var menu = panel.GetComponent<LabMenu>();
            if (menu && menu.TryGetDefault(out var position, out var rotation)) { home.position = position; home.rotation = rotation; }
            homes[panel] = home; adopted.Add(panel); Place(panel, home);
        }
        void Place(Transform panel, Home home)
        {
            var pose = CompactConsole ? CompactPose(panel.name, home, Table) : home;
            panel.SetLocalPositionAndRotation(pose.position, pose.rotation); panel.localScale = pose.scale;
            var menu = panel.GetComponent<LabMenu>();
            if (menu) menu.Rehome(pose.position, pose.rotation, LayoutKey);
        }
        // The cockpit as physical offsets from the eyes of a person standing at the station (x right, y up, z toward
        // the table). Interactive surfaces sit 0.55–0.7 m from the eyes and about 0.5 m from a shoulder, below the eye
        // line; text is about 15 dmm. On the 1 m table the holograms sit low and close (about 25° to 35° below the eye),
        // so the desk goes lower, under them, and the status line closer, inside the small station's sector.
        public static bool Cockpit(string name, TableSize table, out Vector3 fromEye, out Vector3 euler, out float scale)
        {
            bool small = table == TableSize.Small;
            switch (name)
            {
                case "02 · Architecture controls":
                    fromEye = small ? new Vector3(0, -.55f, .40f) : new Vector3(0, -.48f, .44f); euler = new Vector3(small ? 50 : 44, 0, 0); scale = .00046f; return true;
                case "01 · Service catalog": fromEye = new Vector3(-.44f, -.29f, .40f); euler = new Vector3(8, -48, 0); scale = .00044f; return true;
                case "03 · Inspector": fromEye = new Vector3(.44f, -.29f, .40f); euler = new Vector3(8, 48, 0); scale = .00044f; return true;
                case "Settings console": fromEye = new Vector3(0, -.15f, .52f); euler = new Vector3(16, 0, 0); scale = .00045f; return true;
                case "ATLAS assistant": fromEye = new Vector3(-.3f, -.17f, .54f); euler = new Vector3(10, -30, 0); scale = .0004f; return true;
                case "ATLAS compact presence": fromEye = new Vector3(-.5f, .09f, .46f); euler = new Vector3(-6, -47, 0); scale = .00036f; return true;
                case "Guided mission": fromEye = new Vector3(.5f, .1f, .46f); euler = new Vector3(-6, 47, 0); scale = .00036f; return true;
                case "Mission status":
                    if (small) { fromEye = new Vector3(0, -.166f, .78f); euler = new Vector3(12, 0, 0); scale = .00101f; return true; }
                    // High enough to clear every hologram on the larger tables.
                    fromEye = new Vector3(0, .3f, 1f); euler = new Vector3(-14, 0, 0); scale = .0007f; return true;
            }
            fromEye = euler = Vector3.zero; scale = 0; return false;
        }
        // Targets are physical offsets from the eye; the console's table scale is undone here, so the cockpit has the
        // same physical size and reach at every table size.
        static Home CompactPose(string name, Home home, TableLayout table)
        {
            if (Cockpit(name, table.Size, out var fromEye, out var euler, out float scale)) return FromEye(fromEye, euler, scale, table.ConsoleScale);
            return Compact(name, home);
        }
        static Home FromEye(Vector3 offset, Vector3 euler, float scale, float k) => Pose(AuthoredEye + offset / k, Quaternion.Euler(euler), scale / k);
        // Any other personal panel keeps its direction and angular size relative to the user, clamped to the
        // station's sector (station-1 frame of the full table).
        static Home Compact(string name, Home home)
        {
            var soloStand = Center + new Vector3(0, 0, -SoloDistance); var sharedStand = StationPosition(0, SharedDistance);
            var flat = home.position - soloStand; flat.y = 0;
            float distance = Mathf.Max(.01f, flat.magnitude), angle = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            float clamped = Mathf.Clamp(angle, -MaxAngle, MaxAngle), reach = Mathf.Clamp(distance, NearDistance, FarDistance), k = reach / distance;
            var position = sharedStand + Quaternion.Euler(0, clamped, 0) * Vector3.forward * reach;
            position.y = EyeHeight + (home.position.y - EyeHeight) * k;
            var euler = home.rotation.eulerAngles;
            var rotation = Mathf.Approximately(clamped, angle) ? home.rotation : Quaternion.Euler(euler.x, clamped, euler.z);
            return new Home { position = position, rotation = rotation, scale = home.scale * k };
        }
        static Home Pose(Vector3 position, Quaternion rotation, float scale) => new Home { position = position, rotation = rotation, scale = Vector3.one * scale };

        // The event identity floats above the table centre and turns to each viewer in a shared room.
        void UpdateIdentity()
        {
            if (!identity) return;
            var menu = identity.GetComponent<LabMenu>(); if (menu && menu.Grabbed) return;
            if (!SharedRoom)
            {
                if (!identityPlaced) { identity.SetLocalPositionAndRotation(identityHome.position, identityHome.rotation); identity.localScale = identityHome.scale; identityPlaced = true; }
                return;
            }
            identity.localPosition = Center + new Vector3(0, 3.05f, 0); identity.localScale = identityHome.scale * .8f; identityPlaced = true;
            var cam = lab && lab.Rig ? lab.Rig.ViewCamera : Camera.main;
            if (!cam) return;
            var direction = identity.position - cam.transform.position; direction.y = 0;
            if (direction.sqrMagnitude > .01f) identity.rotation = Quaternion.LookRotation(direction);
        }

        void BuildMarkers()
        {
            markerRoot = new GameObject("Shared room stations").transform; markerRoot.SetParent(world, false);
            for (int s = 0; s < MaxStations; s++)
            {
                var color = Colors[s];
                var root = new GameObject("Station marker " + (s + 1)).transform; root.SetParent(markerRoot, false);
                root.localPosition = StationPosition(s) + Vector3.up * .012f; root.localRotation = Quaternion.Euler(0, StationYaw(s), 0);
                var ring = Ring(root, Vector3.zero, .38f, color, .022f, 64); ring.sharedMaterial = Beam(color, false); ring.textureMode = LineTextureMode.Stretch;
                var inner = Ring(root, Vector3.zero, .31f, color, .006f, 48);
                // A chevron toward the table: "face this way" when aligning.
                var chevron = Line(root, "Facing chevron", new[] { new Vector3(-.12f, 0, .44f), new Vector3(0, 0, .56f), new Vector3(.12f, 0, .44f) }, color, .02f);
                chevron.sharedMaterial = Beam(color, false); chevron.textureMode = LineTextureMode.Stretch;
                var label = Panel(root, "Station number", new Vector3(0, .002f, -.62f), new Vector2(360, 240), background: false, movable: false);
                label.localRotation = Quaternion.Euler(90, 0, 0); label.localScale = Vector3.one * .0011f;
                Text(label, (s + 1).ToString(), new Vector2(0, 30), new Vector2(360, 170), 104, color, TextAnchor.MiddleCenter);
                var you = Text(label, "", new Vector2(0, -88), new Vector2(360, 44), 26, White, TextAnchor.MiddleCenter);
                markers.Add(new Marker { root = root, ring = ring, inner = inner, you = you });
            }
        }
        void RefreshMarkers()
        {
            for (int s = 0; s < markers.Count; s++)
            {
                bool local = s == Station;
                markers[s].ring.widthMultiplier = local ? .034f : .018f;
                markers[s].ring.sharedMaterial = Beam(OutsideZone && local ? Alert : Colors[s], false);
                markers[s].you.text = local ? "TU ESTACIÓN" : "";
                Retrack(markers[s].you);
            }
        }

        // Zone guard: leaving your circle in a shared room risks bumping a neighbour, so a quiet head-up warning
        // appears after a short grace period and the station ring turns red until you step back.
        void UpdateZone()
        {
            var cam = lab && lab.Rig ? lab.Rig.ViewCamera : null;
            bool evaluate = SharedRoom && cam && ((lab.Rig.IsXR) || EvaluateZoneWithoutXR);
            bool outside = false;
            if (evaluate)
            {
                var flat = cam.transform.position - StandPosition; flat.y = 0;
                outside = flat.magnitude > ZoneRadius;
            }
            if (outside && !OutsideZone && guardSince < 0) guardSince = Time.unscaledTime;
            if (!outside) guardSince = -1;
            bool show = outside && (EvaluateZoneWithoutXR || Time.unscaledTime - guardSince > .6f);
            if (show == OutsideZone) return;
            OutsideZone = show; RefreshMarkers();
            if (show && !guard && cam) BuildGuard(cam.transform);
            if (guard)
            {
                guard.gameObject.SetActive(show);
                guardText.text = "FUERA DE TU ESTACIÓN  ·  VUELVE AL CÍRCULO " + (Station + 1);
            }
            if (show && lab.Feedback) lab.Feedback.Play(LabFeedback.Close);
        }
        void BuildGuard(Transform head)
        {
            guard = Focus(Panel(head, "Station guard", new Vector3(0, -.11f, .65f), new Vector2(620, 84), movable: false));
            guard.localScale = Vector3.one * .0008f;
            guard.GetComponent<HoloPanelGraphic>().Accent = Alert;
            guardText = Text(guard, "", Vector2.zero, new Vector2(590, 70), 24, Alert, TextAnchor.MiddleCenter);
            guardText.enableAutoSizing = true; guardText.fontSizeMin = 14; guardText.fontSizeMax = 24;
        }
    }
}
