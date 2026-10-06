using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GuateGeeks.AwsVr.Tests
{
    // Hand-first interface, checked from a standing person's eyes without a headset: the near-field cockpit,
    // the wrist menu, touch keyboards at the typing pose, head-following readers and every-finger touch.
    public sealed class NearFieldInterfaceTests
    {
        ArchitectureLab lab;
        string originalProfile, codeDirectory;
        readonly Dictionary<string, string> savedStrings = new Dictionary<string, string>();
        readonly Dictionary<string, int?> savedInts = new Dictionary<string, int?>();
        bool hadComponentScale; float componentScale;
        static readonly string[] MenuNames = { "Lab identity", "Mission status", "01 · Service catalog", "03 · Inspector", "02 · Architecture controls", "Controls reference", "Comfort controls", "04 · Environment settings", "05 · Cloud connection", "Settings console", "Workspace reader", "ATLAS assistant", "ATLAS compact presence", "Guided mission" };
        static readonly string[] IntKeys = { SharedSpace.LayoutPreference, LabRig.WristPreference, TableLayout.Preference, SharedSpace.SharedPreference, SharedSpace.StationPreference, LabEnvironmentSettings.PreferenceKey };
        static readonly string[] Cockpit = { "02 · Architecture controls", "01 · Service catalog", "03 · Inspector" };
        string ProfilePath => Path.Combine(Application.persistentDataPath, "cloud-profile.json");

        [UnitySetUp]
        public IEnumerator Setup()
        {
            originalProfile = File.Exists(ProfilePath) ? File.ReadAllText(ProfilePath) : null;
            if (File.Exists(ProfilePath)) File.Delete(ProfilePath);
            savedStrings.Clear(); savedInts.Clear();
            foreach (var name in MenuNames) foreach (var layout in new[] { "", "near.", "shared.", "shared.small." })
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
            codeDirectory = Path.Combine(Path.GetTempPath(), "near-field-" + System.Guid.NewGuid().ToString("N"));
            typeof(ArchitectureLab).GetField("codeDirectory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(lab, codeDirectory);
            float deadline = Time.realtimeSinceStartup + 15;
            while (lab.Busy && Time.realtimeSinceStartup < deadline) yield return null;
            lab.Rig.enabled = false;
        }
        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if (originalProfile != null) File.WriteAllText(ProfilePath, originalProfile); else if (File.Exists(ProfilePath)) File.Delete(ProfilePath);
            if (lab) Object.Destroy(lab.gameObject); yield return null;
            if (Directory.Exists(codeDirectory)) Directory.Delete(codeDirectory, true);
            foreach (var pair in savedStrings) { if (pair.Value == null) PlayerPrefs.DeleteKey(pair.Key); else PlayerPrefs.SetString(pair.Key, pair.Value); }
            foreach (var pair in savedInts) { if (pair.Value == null) PlayerPrefs.DeleteKey(pair.Key); else PlayerPrefs.SetInt(pair.Key, pair.Value.Value); }
            if (hadComponentScale) PlayerPrefs.SetFloat(ArchitectureLab.ComponentScalePreference, componentScale); else PlayerPrefs.DeleteKey(ArchitectureLab.ComponentScalePreference);
            PlayerPrefs.Save();
        }
        LabTarget Find(string prefix, Transform under = null) => Object.FindObjectsByType<LabTarget>()
            .Where(t => t.isActiveAndEnabled && t.Label && t.Label.text.StartsWith(prefix) && (!under || t.transform.IsChildOf(under)) && lab.CanInteract(t))
            .OrderByDescending(t => t.Label.text == prefix).FirstOrDefault();
        void Click(string prefix, Transform under = null)
        {
            var button = Find(prefix, under);
            Assert.IsNotNull(button, "Missing button: " + prefix); Assert.IsTrue(button.Available, "Disabled button: " + prefix); button.Activate();
        }
        Transform Head => lab.Rig.ViewCamera.transform;
        // A standing person at the station: eyes 1.65 m above the stand.
        Vector3 StandEye => lab.Space.Console.parent.TransformPoint(lab.Space.StandPosition + Vector3.up * SharedSpace.EyeHeight);
        void LookFrom(Vector3 eye, float yaw, float pitch) => Head.SetPositionAndRotation(eye, Quaternion.Euler(pitch, yaw, 0));
        void Capture(string name, float fov = 80)
        {
            HoloReveal.CompleteAll();
            var camera = Camera.main; var previous = camera.targetTexture; float previousFov = camera.fieldOfView; camera.fieldOfView = fov;
            var rt = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 }; camera.targetTexture = rt; camera.Render();
            var active = RenderTexture.active; RenderTexture.active = rt;
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
            Directory.CreateDirectory("Validation"); File.WriteAllBytes("Validation/" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = active; camera.targetTexture = previous; camera.fieldOfView = previousFov; Object.Destroy(rt); Object.Destroy(texture);
        }
        static Vector2 Size(Component c) => ComfortLayout.WorldSize(c.transform as RectTransform);
        static float FlatAngle(Vector3 forward, Vector3 direction) { forward.y = 0; direction.y = 0; return Vector3.SignedAngle(forward, direction, Vector3.up); }
        static void AssertNotClipped(Transform root)
        {
            foreach (var label in root.GetComponentsInChildren<TMPro.TMP_Text>()) { label.ForceMeshUpdate(); Assert.IsFalse(label.isTextTruncated, "Clipped label: " + label.text); }
        }

        [UnityTest]
        public IEnumerator NearFieldCockpitKeepsPanelsCompactWithinReachAndTouchSized()
        {
            var space = lab.Space;
            Assert.IsFalse(space.NearField, "Desktop rehearsal keeps the panoramic console by default");
            var dock = GameObject.Find("02 · Architecture controls").transform; var panoramicDock = dock.position;
            space.SetLayoutChoice(SharedSpace.LayoutNear); yield return null; yield return null;
            Assert.IsTrue(space.NearField);
            var eye = StandEye; LookFrom(eye, 0, 22);
            foreach (var name in Cockpit)
            {
                var panel = (RectTransform)GameObject.Find(name).transform;
                Assert.Less(Vector3.Distance(eye, panel.position), ComfortLayout.ReachFromEyes, name + " sits within reach of the eyes");
                var nearShoulder = eye + new Vector3(panel.position.x < eye.x ? -.18f : .18f, -.23f, 0);
                Assert.Less(Vector3.Distance(nearShoulder, panel.position), .62f, name + " is reachable from the near shoulder");
                Assert.Less(panel.position.y, eye.y - .2f, name + " stays below the eyes: no raised arm");
                Assert.Less(Mathf.Max(Size(panel).x, Size(panel).y), .6f, name + " is compact");
                Assert.Greater(Vector3.Dot(panel.forward, (panel.position - eye).normalized), .6f, name + " faces the eyes");
                AssertNotClipped(panel);
            }
            var catalogPanel = GameObject.Find("01 · Service catalog").transform;
            foreach (var label in new[] { "Crear", "Definir", "Conectar nodos", "Revisar diseño", "Desplegar demo", "Deshacer", "Guardar", "Lambda", "DynamoDB" })
            {
                var target = Find(label, label == "Lambda" || label == "DynamoDB" ? catalogPanel : dock); Assert.IsNotNull(target, label);
                Assert.GreaterOrEqual(Mathf.Min(Size(target).x, Size(target).y), ComfortLayout.MinTouchTarget - .0002f, label + " is a finger-sized target");
            }
            Click("Ajustes"); yield return null;
            var settings = GameObject.Find("Settings console").transform;
            Assert.Less(Vector3.Distance(eye, settings.position), ComfortLayout.ReachFromEyes, "Settings pop up within reach");
            Click("Controles"); yield return null;
            Assert.IsNotNull(Find("Paneles: al alcance"), "Layout choice is in the controls settings");
            AssertNotClipped(settings);
            Click("Cerrar ajustes"); yield return null;
            Capture("65-near-cockpit");
            // «Traer aquí»: after a turn and a step the console comes to the person; «Centrar» returns it home.
            var moved = eye + new Vector3(.8f, 0, .3f); LookFrom(moved, 60, 20);
            lab.Rig.BringPanelsHere(); yield return null;
            var catalog = GameObject.Find("01 · Service catalog").transform;
            Assert.Less(Vector3.Distance(moved, catalog.position), ComfortLayout.ReachFromEyes, "The cockpit follows the person on request");
            Assert.Less(Mathf.Abs(FlatAngle(Head.forward, GameObject.Find("02 · Architecture controls").transform.position - moved)), 5, "The desk is in front again");
            lab.Rig.Recenter(); yield return null;
            Assert.AreEqual(Vector3.zero, space.Console.localPosition, "Centring returns the console to the stand");
            space.SetLayoutChoice(SharedSpace.LayoutPanoramic); yield return null;
            Assert.IsFalse(space.NearField);
            Assert.Less(Vector3.Distance(panoramicDock, dock.position), .001f, "The panoramic layout is restored exactly");
        }

        [UnityTest]
        public IEnumerator WristMenuIsTouchSizedAndOpensEveryWorkspace()
        {
            var eye = StandEye; LookFrom(eye, 0, 10);
            lab.Rig.ShowWristMenu(true); yield return null;
            var wrist = lab.Rig.WristMenu; Assert.IsTrue(lab.Rig.WristMenuVisible);
            Assert.Less(Vector3.Distance(eye, wrist.position), .62f, "The wrist menu is at hand distance");
            var buttons = wrist.GetComponentsInChildren<LabTarget>();
            Assert.AreEqual(12, buttons.Length);
            foreach (var b in buttons) Assert.GreaterOrEqual(Mathf.Min(Size(b).x, Size(b).y), .028f, b.Label.text + " is easy to tap");
            AssertNotClipped(wrist);
            Capture("66-wrist-menu", 70);
            bool catalog = lab.CatalogVisible;
            Click("CREAR", wrist); Assert.AreNotEqual(catalog, lab.CatalogVisible); Click("CREAR", wrist); Assert.AreEqual(catalog, lab.CatalogVisible);
            bool controls = lab.ControlsVisible; Click("MESA", wrist); Assert.AreNotEqual(controls, lab.ControlsVisible); Click("MESA", wrist);
            Click("INSPECCIÓN", wrist); yield return null; Assert.IsTrue(lab.InspectionVisible, "Inspection opens from the wrist");
            Click("INSPECCIÓN", wrist); yield return null; Assert.IsFalse(lab.InspectionVisible, "…and closes from it");
            lab.Select(lab.Views.Values.First(v => v.Model.kind == ServiceKind.Lambda)); lab.Rig.ShowWristMenu(true); yield return null;
            Click("CÓDIGO", wrist); yield return null; Assert.IsTrue(lab.CodeStudioVisible, "The selected Lambda's code opens from the wrist");
            Click("CÓDIGO", wrist); yield return null; Assert.IsFalse(lab.CodeStudioVisible);
            Click("AJUSTES", wrist); yield return null; Assert.IsTrue(lab.SettingsVisible);
            Click("Controles"); yield return null;
            Click("Menú de muñeca"); Assert.IsTrue(lab.Rig.WristOnRight, "The menu can move to the right wrist");
            Click("Menú de muñeca"); Assert.IsFalse(lab.Rig.WristOnRight);
            Click("Cerrar ajustes");
            lab.Rig.ShowWristMenu(false); Assert.IsFalse(lab.Rig.WristMenuVisible);
        }

        [UnityTest]
        public IEnumerator KeyboardsOpenAtTheTypingPoseAndAnyFingerTypes()
        {
            var eye = StandEye; LookFrom(eye, 0, 30);
            var node = lab.Views.Values.First(v => v.Model.kind == ServiceKind.Lambda);
            lab.Select(node); yield return null;
            Click("Nombre:"); yield return null; Assert.IsTrue(lab.EditingText);
            var keyboard = (RectTransform)GameObject.Find("Design keyboard").transform;
            // The headset pose (the desktop rehearsal pose differs only in distance so a monitor can show it).
            ComfortLayout.PlaceKeyboard(keyboard, Head, true, 88); Physics.SyncTransforms();
            float distance = Vector3.Distance(eye, keyboard.position);
            Assert.That(distance, Is.InRange(.45f, .62f), "In front of the chest, not at arm's length");
            Assert.Less(keyboard.position.y, eye.y - .25f, "Below the eyes, where forearms rest");
            Assert.Greater(Vector3.Dot(keyboard.forward, (keyboard.position - eye).normalized), .95f, "Tilted to face the eyes");
            Assert.Less(Vector3.Distance(eye + new Vector3(.18f, -.23f, 0), keyboard.position), .55f, "Within an easy reach of the shoulder");
            var keys = keyboard.GetComponentsInChildren<LabTarget>().Where(t => t.Label && t.Label.text.Length == 1).ToArray();
            Assert.GreaterOrEqual(keys.Length, 30);
            foreach (var key in keys) { var s = Size(key); Assert.GreaterOrEqual(s.x, .03f, key.Label.text); Assert.GreaterOrEqual(s.y, ComfortLayout.MinTouchTarget - .0002f, key.Label.text); }
            var q = keys.Single(k => k.Label.text == "Q").transform; var w = keys.Single(k => k.Label.text == "W").transform;
            Assert.AreEqual(ComfortLayout.KeyPitch, Vector3.Distance(q.position, w.position), .001f, "3.6 cm key pitch");
            AssertNotClipped(keyboard);
            Capture("67-touch-keyboard");
            // Press G with the middle finger: approach from the front, touch, dwell. Thumb, ring and little do the same.
            var g = keys.Single(k => k.Label.text == "G");
            var fingers = new MultiFingerTouch(); var targets = new object[MultiFingerTouch.Fingers]; var fronts = new float[MultiFingerTouch.Fingers];
            int pressed = -1; float now = 0;
            foreach (float depth in new[] { .04f, .03f, .02f, .01f, .003f, .002f, .001f, .001f, .001f })
            {
                targets[2] = lab.Rig.TouchProbe(g.transform.position - g.transform.forward * depth, false, out fronts[2]);
                Assert.AreSame(g, targets[2], "The middle fingertip in front of G finds G");
                now += .03f; int finger = fingers.Step(targets, fronts, now, .03f);
                if (finger >= 0) { pressed = finger; ((LabTarget)targets[finger]).Activate(); }
            }
            Assert.AreEqual(2, pressed, "The middle finger pressed");
            Assert.IsTrue(keyboard.GetComponentsInChildren<TMPro.TMP_Text>().Any(t => t.text.EndsWith("G")), "G was typed");
            Assert.IsNull(lab.Rig.TouchProbe(g.transform.position + g.transform.forward * .04f, false, out _), "No press from behind the keyboard");
            Click("Cancelar nombre"); Assert.IsFalse(lab.EditingText);
            // The code keyboard keeps every key finger-sized too.
            lab.OpenLambdaEditor(node.Model.id); yield return null;
            Click("Editar línea"); yield return null;
            var code = (RectTransform)GameObject.Find("Code keyboard").transform; ComfortLayout.PlaceKeyboard(code, Head, true, 98);
            foreach (var key in code.GetComponentsInChildren<LabTarget>().Where(t => !t.Menu))
                Assert.GreaterOrEqual(Mathf.Min(Size(key).x, Size(key).y), ComfortLayout.MinTouchTarget - .0002f, "Code key " + key.Label.text);
            Assert.That(Vector3.Distance(eye, code.position), Is.InRange(.45f, .62f));
            AssertNotClipped(code);
            Click("Cancelar edición");
        }

        [UnityTest]
        public IEnumerator CodeAndInspectionFollowTheHeadAndPinWhereLeft()
        {
            var eye = StandEye; LookFrom(eye, 0, 8);
            var fn = lab.Views.Values.First(v => v.Model.kind == ServiceKind.Lambda);
            lab.Select(fn); lab.OpenLambdaEditor(fn.Model.id); lab.OpenSlots();
            yield return null; yield return null;
            var code = GameObject.Find("Lambda code studio").GetComponent<HeadFollow>();
            var reader = GameObject.Find("Workspace reader").GetComponent<HeadFollow>();
            Assert.IsNotNull(code); Assert.IsNotNull(reader);
            Assert.AreEqual(HeadFollow.DesktopDistance, Vector3.Distance(eye, code.transform.position), .05f, "A comfortable reading distance");
            Assert.Less(Mathf.Abs(FlatAngle(Head.forward, code.transform.position - eye)), 3, "Code sits in the middle");
            float right = FlatAngle(Head.forward, reader.transform.position - eye);
            Assert.Greater(right, code.HalfAngle + reader.HalfAngle, "Inspection sits to the right without overlapping the code");
            Assert.Greater(Vector3.Dot(code.transform.forward, (code.transform.position - eye).normalized), .98f, "Faces the eyes");
            Capture("68-follow-readers", 70);
            // Glancing 25° (e.g. at the table) moves nothing.
            var before = code.transform.position; LookFrom(eye, 25, 8);
            yield return new WaitForSecondsRealtime(.4f);
            Assert.Less(Vector3.Distance(before, code.transform.position), .01f, "Small glances never move reading panels");
            // A real turn brings them along.
            LookFrom(eye, 100, 8); yield return new WaitForSecondsRealtime(1.5f);
            Assert.Less(Mathf.Abs(FlatAngle(Head.forward, code.transform.position - eye)), 6, "After turning, the code is in front again");
            Assert.Greater(FlatAngle(Head.forward, reader.transform.position - eye), 10, "…with inspection still on the right");
            // Dragging pins a panel; «Traer aquí» releases it.
            var menu = code.GetComponent<LabMenu>(); var owner = new object(); var ray = new Ray(eye, Head.forward);
            Assert.IsTrue(menu.TryGrab(owner, ray, 1, Head.rotation));
            menu.Move(owner, new Ray(eye, Quaternion.Euler(0, -30, 0) * Head.forward), Head.rotation, 0); menu.Release(owner);
            Assert.IsTrue(code.Pinned);
            var pinned = code.transform.position; LookFrom(eye, 200, 8); yield return new WaitForSecondsRealtime(1f);
            Assert.Less(Vector3.Distance(pinned, code.transform.position), .001f, "A pinned panel stays where it was left");
            lab.Rig.BringPanelsHere(); yield return new WaitForSecondsRealtime(.6f);
            Assert.IsFalse(code.Pinned);
            Assert.Less(Mathf.Abs(FlatAngle(Head.forward, code.transform.position - eye)), 6, "«Traer aquí» brings it back in front");
        }
    }
}
