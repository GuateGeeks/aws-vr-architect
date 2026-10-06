using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GuateGeeks.AwsVr.Tests
{
    // 1 m, 3 m and the full 3.9 m table, alone and in a shared room (local and networked).
    public sealed class TableSizeTests
    {
        ArchitectureLab lab;
        string originalProfile;
        readonly Dictionary<string, string> savedStrings = new Dictionary<string, string>();
        readonly Dictionary<string, int?> savedInts = new Dictionary<string, int?>();
        bool hadComponentScale; float componentScale;
        static readonly string[] MenuNames = { "Lab identity", "Mission status", "01 · Service catalog", "03 · Inspector", "02 · Architecture controls", "Controls reference", "Comfort controls", "04 · Environment settings", "05 · Cloud connection", "Settings console", "Workspace reader" };
        static readonly string[] IntKeys = { TableLayout.Preference, SharedSpace.SharedPreference, SharedSpace.StationPreference, LabEnvironmentSettings.PreferenceKey, SharedSpace.LayoutPreference, LabRig.WristPreference };
        static readonly string[] Personal = { "02 · Architecture controls", "01 · Service catalog", "03 · Inspector", "Mission status" };
        string ProfilePath => Path.Combine(Application.persistentDataPath, "cloud-profile.json");
        readonly StringBuilder metrics = new StringBuilder();

        [UnitySetUp]
        public IEnumerator Setup()
        {
            originalProfile = File.Exists(ProfilePath) ? File.ReadAllText(ProfilePath) : null;
            if (File.Exists(ProfilePath)) File.Delete(ProfilePath);
            savedStrings.Clear(); savedInts.Clear(); metrics.Clear();
            foreach (var name in MenuNames) foreach (var layout in new[] { "", "shared.", "shared.small." })
            {
                string key = LabMenu.PreferencePrefix + layout + name;
                savedStrings[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null; PlayerPrefs.DeleteKey(key);
            }
            foreach (var key in IntKeys) { savedInts[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null; PlayerPrefs.DeleteKey(key); }
            hadComponentScale = PlayerPrefs.HasKey(ArchitectureLab.ComponentScalePreference); componentScale = PlayerPrefs.GetFloat(ArchitectureLab.ComponentScalePreference, 1);
            PlayerPrefs.DeleteKey(ArchitectureLab.ComponentScalePreference);
            yield return SceneManager.LoadSceneAsync("AWSArchitectLab");
            yield return null;
            lab = Object.FindAnyObjectByType<ArchitectureLab>(); Assert.IsNotNull(lab);
            float deadline = Time.realtimeSinceStartup + 15;
            while (lab.Busy && Time.realtimeSinceStartup < deadline) yield return null;
            lab.Rig.enabled = false;
        }
        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if (metrics.Length > 0) { Directory.CreateDirectory("Validation"); File.WriteAllText("Validation/table-size-metrics-" + TestContext.CurrentContext.Test.Name + ".txt", metrics.ToString()); }
            if (originalProfile != null) File.WriteAllText(ProfilePath, originalProfile); else if (File.Exists(ProfilePath)) File.Delete(ProfilePath);
            if (lab) Object.Destroy(lab.gameObject); yield return null;
            foreach (var pair in savedStrings) { if (pair.Value == null) PlayerPrefs.DeleteKey(pair.Key); else PlayerPrefs.SetString(pair.Key, pair.Value); }
            foreach (var pair in savedInts) { if (pair.Value == null) PlayerPrefs.DeleteKey(pair.Key); else PlayerPrefs.SetInt(pair.Key, pair.Value.Value); }
            if (hadComponentScale) PlayerPrefs.SetFloat(ArchitectureLab.ComponentScalePreference, componentScale); else PlayerPrefs.DeleteKey(ArchitectureLab.ComponentScalePreference);
            PlayerPrefs.Save();
        }
        void Click(string prefix)
        {
            var button = Object.FindObjectsByType<LabTarget>()
                .Where(t => t.isActiveAndEnabled && t.Label && t.Label.text.StartsWith(prefix) && lab.CanInteract(t))
                .OrderByDescending(t => t.Label.text == prefix).FirstOrDefault();
            Assert.IsNotNull(button, "Missing button: " + prefix); Assert.IsTrue(button.Available, "Disabled button: " + prefix); button.Activate();
        }
        LabTarget TableButton(string prefix) => Object.FindObjectsByType<LabTarget>().FirstOrDefault(t => t.isActiveAndEnabled && t.Label && t.Label.text.StartsWith(prefix));
        void Capture(string name, Vector3? eye = null, Vector3? look = null, float fov = 60)
        {
            HoloReveal.CompleteAll();
            var camera = Camera.main; var previous = camera.targetTexture; var pose = (camera.transform.position, camera.transform.rotation); float previousFov = camera.fieldOfView;
            if (eye.HasValue) { camera.transform.position = eye.Value; camera.transform.LookAt(look.Value); camera.fieldOfView = fov; }
            var rt = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 }; camera.targetTexture = rt; camera.Render();
            var active = RenderTexture.active; RenderTexture.active = rt;
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
            Directory.CreateDirectory("Validation"); File.WriteAllBytes("Validation/" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = active; camera.targetTexture = previous; Object.Destroy(rt); Object.Destroy(texture);
            camera.transform.SetPositionAndRotation(pose.Item1, pose.Item2); camera.fieldOfView = previousFov;
        }
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        Vector3 Eye => lab.Space.StandPosition + Vector3.up * SharedSpace.EyeHeight;
        // Degrees below the horizon, seen from the person's eye at their stand.
        float Below(Vector3 point) => Mathf.Atan2(Eye.y - point.y, Flat(point - Eye).magnitude) * Mathf.Rad2Deg;
        float Bearing(Vector3 point) => Vector3.SignedAngle(lab.Space.Rotation * Vector3.forward, Flat(point - Eye), Vector3.up);
        static Vector3[] Corners(string name) { var c = new Vector3[4]; ((RectTransform)GameObject.Find(name).transform).GetWorldCorners(c); return c; }
        Vector3 MiniatureCentre => lab.Table.ToRoom(new Vector3(0, 1.5f, 2.65f));

        void AssertTableAndHolograms(TableSize size, float diameter)
        {
            var t = lab.Table; Assert.AreEqual(size, lab.TableSize); Assert.AreEqual(diameter, t.Diameter, 1e-4f);
            Assert.AreEqual(t.Scale, GameObject.Find("Projection table").transform.lossyScale.x, 1e-4f);
            var inset = GameObject.Find("Console inset").GetComponent<Renderer>().bounds;
            Assert.AreEqual(3.62f * t.Scale, inset.size.x, .01f, "Glass diameter"); Assert.AreEqual(.72f, inset.center.y, .01f + .03f * (1 - t.Scale), "The table keeps its height");
            Assert.AreEqual(t.Scale, lab.Workspace.lossyScale.x, 1e-4f);
            foreach (var view in lab.Views.Values)
            {
                Assert.Less(Vector3.Distance(t.ToRoom(view.Model.position), view.transform.position), 1e-3f, "Design positions are unchanged: " + view.Model.name);
                Assert.LessOrEqual(Flat(view.transform.position - SharedSpace.Center).magnitude, t.Radius, view.Model.name + " is over the table");
                Assert.AreEqual(t.Scale * view.transform.localScale.x, view.transform.lossyScale.x, 1e-4f);
            }
            // Holding or previewing an object keeps it on this table, wherever you point.
            var far = lab.ClampToTable(Eye + lab.Space.Rotation * Vector3.forward * 6);
            Assert.LessOrEqual(Flat(far - SharedSpace.Center).magnitude, t.Radius + 1e-3f);
            Assert.Less(Vector3.Distance(ArchitectureLab.ClampWorkspace(lab.DesignPoint(far)), lab.DesignPoint(far)), 1e-4f);
        }
        void Measure(string label)
        {
            var t = lab.Table; metrics.Append("[").Append(label).Append("] table ").Append(t.Label).Append(" stand ").Append(lab.Space.StandPosition.ToString("F2")).AppendLine();
            foreach (var view in lab.Views.Values)
            {
                var name = view.GetComponentsInChildren<TMPro.TMP_Text>().First(x => x.text == view.Model.name);
                float letters = name.fontSize * name.rectTransform.lossyScale.y, distance = Vector3.Distance(Eye, name.transform.position);
                metrics.AppendFormat("  {0}: emblem {1:F1}° below, plinth {2:F1}°, label {3:F1}°, letter {4:F2}° at {5:F2} m\n", view.Model.name,
                    Below(view.Emblem.transform.position), Below(view.transform.TransformPoint(new Vector3(0, -.29f, 0))), Below(name.transform.position),
                    Mathf.Atan2(letters, distance) * Mathf.Rad2Deg, distance);
            }
            foreach (var panel in Personal)
            {
                var c = Corners(panel); var centre = (c[0] + c[2]) / 2;
                var tr = GameObject.Find(panel).transform;
                metrics.AppendFormat("  {0}: {1:F1}°..{2:F1}° below, bearing {3:F1}°, {4:F2} m · at {5} rot {6} scale {7:F5}\n", panel, c.Min(Below), c.Max(Below), Bearing(centre), Vector3.Distance(Eye, centre),
                    tr.position.ToString("F3"), tr.eulerAngles.ToString("F1"), tr.lossyScale.x);
            }
        }
        // Your panels stay in your sector and nearer to you than to any neighbour.
        void AssertInSector(int s, float margin)
        {
            var stand = SharedSpace.StationPosition(s); var forward = Quaternion.Euler(0, SharedSpace.StationYaw(s), 0) * Vector3.forward;
            Assert.AreEqual(lab.Table.StationDistance, Flat(stand - SharedSpace.Center).magnitude, 1e-3f);
            foreach (var name in Personal.Append("Settings console"))
            {
                var flat = Flat(GameObject.Find(name).transform.position - stand);
                Assert.LessOrEqual(Vector3.Angle(forward, flat), 55, name + " stays inside the station's sector");
                Assert.LessOrEqual(flat.magnitude, 1.4f, name + " stays within reach");
                for (int other = 0; other < SharedSpace.MaxStations; other++) if (other != s)
                    Assert.Greater(Flat(GameObject.Find(name).transform.position - SharedSpace.StationPosition(other)).magnitude, flat.magnitude + margin, name + " intrudes on station " + (other + 1));
            }
        }

        [UnityTest]
        public IEnumerator SoloTableSizesScaleTheTableAndBringYouAndTheConsoleAlong()
        {
            Assert.AreEqual(TableSize.Large, lab.TableSize, "The full table by default");
            var dock = GameObject.Find("02 · Architecture controls").transform; var soloDock = dock.position;
            var stand = lab.Space.StandPosition; Assert.AreEqual(SharedSpace.SoloDistance, Flat(SharedSpace.Center - stand).magnitude, 1e-3f);
            AssertTableAndHolograms(TableSize.Large, 3.9f);
            Click("Ajustes"); Click("Espacio"); yield return null;
            foreach (var label in GameObject.Find("Settings console").GetComponentsInChildren<TMPro.TMP_Text>()) { label.ForceMeshUpdate(); Assert.IsFalse(label.isTextTruncated, "Clipped label: " + label.text); }
            StringAssert.StartsWith("Grande · 3.9 m  ●", TableButton("Grande ·").Label.text);
            Capture("57-table-size-settings");
            Click("Pequeña"); yield return null; yield return null;
            Assert.AreEqual((int)TableSize.Small, PlayerPrefs.GetInt(TableLayout.Preference), "Kept on this headset");
            AssertTableAndHolograms(TableSize.Small, 1);
            StringAssert.StartsWith("Pequeña · 1 m  ●", TableButton("Pequeña").Label.text);
            Assert.IsTrue(lab.Space.CompactConsole); Assert.IsFalse(lab.Space.SharedRoom, "Still alone: no circles, snap turn available");
            Assert.AreEqual(lab.Table.StationDistance, Flat(SharedSpace.Center - lab.Space.StandPosition).magnitude, 1e-3f, "You stand at the small table");
            var camera = lab.Rig.ViewCamera.transform;
            Assert.Greater(Vector3.Dot(Flat(camera.forward).normalized, Flat(SharedSpace.Center - camera.position).normalized), .95f, "Recentred facing the table");
            Click("Cerrar ajustes"); yield return null;
            Measure("solo small");
            Assert.Less(Vector3.Distance(Eye, dock.position), .8f, "Controls within reach");
            // The controls float under the holograms and the status line over them, so neither hides the design.
            float dockTop = Corners("02 · Architecture controls").Min(Below), statusBottom = Corners("Mission status").Max(Below);
            foreach (var view in lab.Views.Values)
            {
                Assert.Greater(dockTop, Below(view.Emblem.transform.position), "The controls hide " + view.Model.name);
                Assert.Less(statusBottom, Below(view.Emblem.transform.position), "The status line hides " + view.Model.name);
            }
            // Placement previews land on the small table.
            Click("Lambda"); Assert.IsTrue(lab.Placing);
            lab.PreviewPlacement(camera.position + camera.forward * lab.PointerReach(3));
            var ghost = GameObject.Find("Placement preview").transform;
            Assert.LessOrEqual(Flat(ghost.position - SharedSpace.Center).magnitude, lab.Table.Radius);
            Click("Cancelar colocación"); yield return null;
            Capture("58-table-small-solo", Eye, MiniatureCentre, 80);
            // Object-first editing still works at arm's length: the context ring keeps its visual angle.
            var fn = lab.Views.Values.First(v => v.Model.kind == ServiceKind.Lambda);
            lab.Select(fn); yield return new WaitForSecondsRealtime(.3f);
            Assert.IsTrue(lab.ContextRingVisible);
            var ring = GameObject.Find("Object context ring").transform;
            Assert.Less(Vector3.Distance(ring.position, fn.transform.position), .2f, "The ring blooms at the small hologram");
            foreach (var text in ring.GetComponentsInChildren<TMPro.TMP_Text>()) { text.ForceMeshUpdate(); Assert.IsFalse(text.isTextTruncated, "Clipped ring label: " + text.text); }
            Capture("64-table-small-context", Eye, fn.transform.position, 80);
            lab.SetGraph(lab.Graph.Copy()); yield return null; // clears the selection
            Capture("59-table-small-desktop");
            Click("Ajustes"); Click("Espacio"); Click("Mediana"); yield return null; yield return null;
            AssertTableAndHolograms(TableSize.Medium, 3);
            Assert.AreEqual(2.17f, Flat(SharedSpace.Center - lab.Space.StandPosition).magnitude, 1e-3f);
            Click("Cerrar ajustes"); yield return null;
            Measure("solo medium");
            Capture("60-table-medium-solo", Eye, MiniatureCentre, 80);
            Click("Ajustes"); Click("Espacio"); Click("Grande ·"); yield return null; yield return null;
            AssertTableAndHolograms(TableSize.Large, 3.9f);
            Assert.IsFalse(lab.Space.CompactConsole);
            Assert.Less(lab.Space.Console.localPosition.magnitude, 1e-4f); Assert.Less(Vector3.Distance(Vector3.one, lab.Space.Console.localScale), 1e-5f);
            Assert.Less(Vector3.Distance(soloDock, dock.position), .001f, "The full solo console is restored exactly");
            Assert.Less(Vector3.Distance(stand, lab.Space.StandPosition), .001f);
        }
        [UnityTest]
        public IEnumerator SharedRoomTableMovesEveryStationAndKeepsConsolesInTheirSector()
        {
            lab.SetSimulatedPeers(true); Assert.IsTrue(lab.Space.SharedRoom);
            lab.SetTableSize(TableSize.Small); for (int i = 0; i < 3; i++) yield return null;
            AssertTableAndHolograms(TableSize.Small, 1);
            for (int s = 0; s < SharedSpace.MaxStations; s++)
            {
                var marker = GameObject.Find("Station marker " + (s + 1)).transform;
                Assert.AreEqual(1.17f, Flat(marker.position - SharedSpace.Center).magnitude, .01f, "Circle " + (s + 1) + " moved in with the table");
            }
            foreach (var peer in lab.Collab.Peers) Assert.AreEqual(1.17f, Flat(peer.Head - SharedSpace.Center).magnitude, .12f, peer.Name + " stands at the small table");
            StringAssert.Contains("Camina a tu círculo 1", lab.StatusMessage);
            Click("Ajustes"); Click("Sala compartida"); yield return null;
            foreach (var label in GameObject.Find("Settings console").GetComponentsInChildren<TMPro.TMP_Text>()) { label.ForceMeshUpdate(); Assert.IsFalse(label.isTextTruncated, "Clipped label: " + label.text); }
            StringAssert.Contains("Mesa Pequeña · 1 m · estaciones a 1.17 m", string.Join(" ", GameObject.Find("Settings console").GetComponentsInChildren<TMPro.TMP_Text>().Select(x => x.text)));
            AssertInSector(0, .25f);
            Click("Cerrar ajustes"); yield return null;
            Measure("shared small · station 1");
            float dockTop = Corners("02 · Architecture controls").Min(Below);
            foreach (var view in lab.Views.Values) Assert.Greater(dockTop, Below(view.Emblem.transform.position), "The controls hide " + view.Model.name);
            Capture("61-table-small-shared", Eye, MiniatureCentre, 80);
            var overview = SharedSpace.Center + new Vector3(-1.9f, 2.9f, -1.9f);
            Capture("62-table-small-overview", overview, SharedSpace.Center + Vector3.up * .8f, 60);
            Click("Ajustes"); lab.SetStation(2); yield return null; yield return null; AssertInSector(2, .25f);
            lab.SetTableSize(TableSize.Medium); yield return null; yield return null;
            AssertTableAndHolograms(TableSize.Medium, 3); AssertInSector(2, .6f);
            Assert.AreEqual(2.17f, Flat(GameObject.Find("Station marker 1").transform.position - SharedSpace.Center).magnitude, .01f);
            lab.SetStation(0); yield return null; yield return null;
            Click("Cerrar ajustes"); yield return null;
            Measure("shared medium · station 1");
            Capture("63-table-medium-shared", Eye, MiniatureCentre, 80);
            Click("Ajustes"); lab.SetTableSize(TableSize.Large); yield return null; yield return null;
            AssertTableAndHolograms(TableSize.Large, 3.9f); AssertInSector(0, .6f);
            Assert.Less(Vector3.Distance(SharedSpace.Center - lab.Space.Rotation * SharedSpace.Center, lab.Space.Console.localPosition), 1e-4f, "The full shared console is unchanged");
            lab.SetSimulatedPeers(false); lab.SetSharedRoom(false);
        }

        NetworkCollabSession AttachRoom()
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var room = new NetworkCollabSession(new RoomGrant { userId = "me", roomId = "ABCDEF12", station = 0, websocketUrl = "wss://test.example/rooms" });
            typeof(NetworkCollabSession).GetField("connected", flags).SetValue(room, true);
            typeof(ArchitectureLab).GetProperty("Collab").SetValue(lab, room);
            var receive = typeof(ArchitectureLab).GetMethod("ReceiveRoomSnapshot", flags);
            room.Snapshot += (System.Action<RoomMessage>)receive.CreateDelegate(typeof(System.Action<RoomMessage>), lab);
            return room;
        }
        void Deliver(NetworkCollabSession room, int version, int tableSize, string receipt = "", bool accepted = true, string message = "", string host = "me")
        {
            var queue = (System.Collections.Concurrent.ConcurrentQueue<string>)typeof(NetworkCollabSession).GetField("received", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(room);
            queue.Enqueue(JsonUtility.ToJson(new RoomMessage { type = "snapshot", roomId = room.Grant.roomId, revision = 0, roomVersion = version, hostId = host, graph = lab.Graph.Copy(),
                tableSize = tableSize, requestId = receipt, accepted = accepted, message = message,
                members = new[] { new RoomMember { userId = "me", name = "ME", role = host == "me" ? "facilitator" : "editor", station = 0 } }, locks = new RoomLease[0] }));
            room.Pump();
        }
        static RoomCommand Take(NetworkCollabSession room, string action)
        {
            var queue = (System.Collections.Concurrent.ConcurrentQueue<string>)typeof(NetworkCollabSession).GetField("outgoing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(room);
            RoomCommand found = null; while (queue.TryDequeue(out var json)) { var command = JsonUtility.FromJson<RoomCommand>(json); if (command.action == action) found = command; }
            return found;
        }
        [UnityTest]
        public IEnumerator RoomTableFollowsTheFacilitatorAndLeavingRestoresYourOwn()
        {
            lab.SetTableSize(TableSize.Small); yield return null;
            var room = AttachRoom();
            Deliver(room, 1, 0);
            Assert.AreEqual(TableSize.Large, lab.TableSize, "A room on an older backend uses the full table");
            Assert.AreEqual(TableSize.Small, lab.LocalTableSize); Assert.IsTrue(lab.Space.SharedRoom);
            // The facilitator asks the room; nothing changes until the room confirms.
            lab.SetTableSize(TableSize.Medium);
            var request = Take(room, "table"); Assert.IsNotNull(request); Assert.AreEqual(2, request.tableSize); Assert.IsFalse(string.IsNullOrEmpty(request.requestId));
            Assert.AreEqual(TableSize.Large, lab.TableSize);
            Deliver(room, 2, 2, request.requestId);
            Assert.AreEqual(TableSize.Medium, lab.TableSize); Assert.AreEqual(2.17f, lab.Space.StationDistance, 1e-3f);
            StringAssert.Contains("La sala usa la mesa Mediana", lab.StatusMessage);
            Assert.AreEqual((int)TableSize.Small, PlayerPrefs.GetInt(TableLayout.Preference), "The room never overwrites your own table");
            lab.SetTableSize(TableSize.Small); request = Take(room, "table");
            Deliver(room, 3, 2, request.requestId, false, "Solo el facilitador cambia el tamaño de la mesa.");
            Assert.AreEqual(TableSize.Medium, lab.TableSize); StringAssert.Contains("no cambió la mesa", lab.StatusMessage);
            // Another person's room: you see its table but cannot change it.
            Deliver(room, 4, 2, host: "host");
            lab.SetTableSize(TableSize.Small); Assert.IsNull(Take(room, "table")); StringAssert.Contains("Solo el facilitador", lab.StatusMessage);
            Click("Ajustes"); Click("Espacio"); yield return null;
            Assert.IsFalse(TableButton("Pequeña").Available, "Editors see the room's table read-only");
            Deliver(room, 5, 1, host: "host"); Assert.AreEqual(TableSize.Small, lab.TableSize, "Everyone follows the facilitator");
            Deliver(room, 6, 3, host: "host"); Assert.AreEqual(TableSize.Large, lab.TableSize);
            typeof(ArchitectureLab).GetMethod("LeaveNetworkRoom", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(lab, null);
            Assert.AreEqual(TableSize.Small, lab.TableSize, "Leaving restores this headset's table");
            Assert.IsTrue(TableButton("Pequeña").Available);
            room.Dispose(); lab.SetSharedRoom(false);
        }
    }
}
