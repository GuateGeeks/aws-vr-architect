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
    public sealed class SharedSpace : MonoBehaviour
    {
        public const int MaxStations = 4;
        public const string SharedPreference = "GuateGeeks.Room.Shared.v1", StationPreference = "GuateGeeks.Room.Station.v1";
        public const float SoloDistance = 3.15f, SharedDistance = 2.6f, ZoneRadius = .75f, EyeHeight = 1.65f;
        // Personal panels stay inside ±MaxAngle of the station's forward axis and within reach of a controller ray.
        public const float MaxAngle = 40, NearDistance = .75f, FarDistance = 1.35f;
        public static readonly Vector3 Center = new Vector3(0, 0, 2.65f);
        public static readonly Color[] Colors = { Cyan, Orange, Hex("#B38CFF"), Hex("#B8F25C") };
        public static readonly string[] ColorHex = { "#5CE1F2", "#FFB24F", "#B38CFF", "#B8F25C" };
        public static SharedSpace Current { get; private set; }
        public static bool SharedRoomActive => Current && Current.SharedRoom;
        public bool SharedRoom { get; private set; }
        public int Station { get; private set; }
        public bool OutsideZone { get; private set; }
        public Transform Console { get; private set; }
        public float Yaw => SharedRoom ? StationYaw(Station) : 0;
        public Quaternion Rotation => Quaternion.Euler(0, Yaw, 0);
        public Vector3 StandPosition => SharedRoom ? StationPosition(Station) : Center + new Vector3(0, 0, -SoloDistance);
        public Color LocalColor => Colors[Station];
        public int MarkerCount => markers.Count;
        // Editor tests have no headset: they can still evaluate the zone guard against the desktop camera.
        public bool EvaluateZoneWithoutXR;
        public event Action Changed;
        public static float StationYaw(int station) => station * 90f;
        public static Vector3 StationPosition(int station, float distance = SharedDistance) =>
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
            lab = owner; world = labWorld; Current = this;
            SharedRoom = PlayerPrefs.GetInt(SharedPreference, 0) == 1;
            Station = Mathf.Clamp(PlayerPrefs.GetInt(StationPreference, 0), 0, MaxStations - 1);
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
        void Apply()
        {
            ApplyConsole();
            if (lab && lab.Rig) lab.Rig.Recenter();
            Changed?.Invoke();
        }
        void ApplyConsole()
        {
            var rotation = Rotation;
            Console.localRotation = rotation; Console.localPosition = Center - rotation * Center;
            adopted.RemoveAll(t => !t || t.parent != Console);
            foreach (var panel in adopted) Place(panel, homes[panel]);
            if (markerRoot) markerRoot.gameObject.SetActive(SharedRoom);
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
            var pose = SharedRoom ? Compact(panel.name, home) : home;
            panel.SetLocalPositionAndRotation(pose.position, pose.rotation); panel.localScale = pose.scale;
            var menu = panel.GetComponent<LabMenu>();
            if (menu) menu.Rehome(pose.position, pose.rotation, SharedRoom ? "shared." : "");
        }
        // Personal-console layout in the station-1 frame. The four main surfaces get hand-tuned poses; any other
        // panel keeps its direction and angular size relative to the user, clamped to the station's sector.
        static Home Compact(string name, Home home)
        {
            switch (name)
            {
                case "02 · Architecture controls": return Pose(new Vector3(0, 1.06f, .74f), Quaternion.Euler(34, 0, 0), .001f);
                case "Mission status": return Pose(new Vector3(0, 1.43f, .9f), Quaternion.Euler(14, 0, 0), .00112f);
                case "01 · Service catalog": return Pose(new Vector3(-.82f, 1.42f, .7f), Quaternion.Euler(0, -50, 0), .00105f);
                case "03 · Inspector": return Pose(new Vector3(.82f, 1.42f, .7f), Quaternion.Euler(0, 50, 0), .00105f);
            }
            var soloStand = Center + new Vector3(0, 0, -SoloDistance); var sharedStand = StationPosition(0);
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
