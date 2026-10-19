using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class QuetzalRecoveryPlayTests
    {
        [UnityTest] public IEnumerator BeaconFeedbackUpdatesWithoutCreatingMaterialsEveryFrame()
        {
            var go = new GameObject("Recovered hand feedback");
            try
            {
                var hand = go.AddComponent<ArmoredHandVisual>(); hand.Initialize(true);
                var positions = new Vector3[26]; var tracked = new bool[26];
                for (int i = 0; i < tracked.Length; i++) { tracked[i] = true; positions[i] = new Vector3(0, 0, i * .003f); }
                hand.ApplyPoses(positions, tracked, Quaternion.identity);
                Assert.IsTrue(hand.BeaconShown);
                int count = Resources.FindObjectsOfTypeAll<Material>().Length;
                for (int i = 0; i < 120; i++) hand.SetBeacon(i % 2 == 0, i / 120f, 1 - i / 120f);
                Assert.AreEqual(count, Resources.FindObjectsOfTypeAll<Material>().Length);
                hand.SetBeacon(true, 0, 0); Assert.IsTrue(hand.BeaconOpen);
                hand.ApplyPoses(positions, new bool[26], Quaternion.identity); Assert.IsFalse(hand.BeaconShown);
            }
            finally { Object.Destroy(go); }
            yield return null;
        }
        [UnityTest] public IEnumerator PerchedBirdRendersAndLeavesAfterTrackingLoss()
        {
            var world = new GameObject("Recovered quetzal rehearsal");
            bool allowed = DigitalQuetzal.PerchAllowed(0);
            var cameraObject = new GameObject("Recovery camera", typeof(Camera));
            RenderTexture rt = null; Texture2D image = null;
            try
            {
                var camera = cameraObject.GetComponent<Camera>(); camera.backgroundColor = LabVisuals.Hex("#071827"); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.nearClipPlane = .02f; camera.fieldOfView = 55;
                var arm = new QuetzalArm { Tracked = true, Offered = true, Wrist = new Vector3(-.25f, 1.2f, .4f),
                    Elbow = new Vector3(-.5f, 1.2f, .4f), Dorsal = Vector3.up };
                DigitalQuetzal.SetPerchAllowed(0, true, false);
                var bird = DigitalQuetzal.Create(world.transform); bird.enabled = false; bird.ViewCamera = camera;
                bird.Senses.Simulate(Vector3.up * 1.65f, Quaternion.identity, arm, default);
                bird.Advance(12); Assert.IsTrue(bird.Perched);
                var forearm = LabVisuals.Shape(world.transform, "Perch rehearsal forearm", PrimitiveType.Capsule,
                    (arm.Elbow + arm.Wrist) * .5f, new Vector3(.065f, .16f, .065f), LabVisuals.Hex("#4D6F82"));
                forearm.transform.rotation = Quaternion.FromToRotation(Vector3.up, arm.Forearm);
                camera.transform.position = bird.Bird.position + new Vector3(.45f, .08f, -1.25f);
                camera.transform.LookAt(bird.Bird.position + Vector3.down * .28f);
                bird.Senses.Simulate(camera.transform.position, Quaternion.identity, arm, default); bird.Advance(.4f);
                yield return null;
                rt = new RenderTexture(1280, 960, 24); camera.targetTexture = rt; camera.Render();
                var old = RenderTexture.active;
                try { RenderTexture.active = rt; image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply(); }
                finally { RenderTexture.active = old; camera.targetTexture = null; }
                string captures = System.Environment.GetEnvironmentVariable("GG_RECOVERY_CAPTURE_DIR") ?? "Validation";
                Directory.CreateDirectory(captures); File.WriteAllBytes(Path.Combine(captures, "recovery-quetzal-perch.png"), image.EncodeToPNG());
                var colors = image.GetPixels32(); int visible = 0;
                foreach (var c in colors) if (c.g > 70 && c.g > c.r * 1.2f) visible++;
                Assert.Greater(visible, 500, "Quetzal plumage must render, not only the background");
                bird.Senses.Simulate(Vector3.up * 1.65f, Quaternion.identity, default, default); bird.Advance(.3f);
                Assert.AreEqual(DigitalQuetzal.Mode.Leaving, bird.State);
            }
            finally
            {
                DigitalQuetzal.SetPerchAllowed(0, allowed, false);
                if (rt) { rt.Release(); Object.Destroy(rt); } if (image) Object.Destroy(image);
                Object.Destroy(world); Object.Destroy(cameraObject);
            }
            yield return null;
        }
    }
}
