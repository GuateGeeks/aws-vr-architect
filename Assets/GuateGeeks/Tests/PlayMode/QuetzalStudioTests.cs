using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GuateGeeks.AwsVr.Tests
{
    // Rehearses the arm interaction with synthetic articulated hands (not headset evidence) and renders the bird and the
    // gloves at each moment for visual review: approach, landing flare, perched on the gauntlet, petting, step-up.
    public sealed class QuetzalStudioTests
    {
        GameObject world, cameraObject, eyeObject;
        Camera camera;
        DigitalQuetzal bird;
        ArmoredHandVisual leftGlove, rightGlove;
        RenderTexture target;
        Texture2D image;
        bool leftAllowed, rightAllowed;
        static readonly Vector3 Eye = new Vector3(0, 1.62f, 0);
        string captures;

        [SetUp] public void SetUp()
        {
            leftAllowed = DigitalQuetzal.PerchAllowed(0); rightAllowed = DigitalQuetzal.PerchAllowed(1);
            DigitalQuetzal.SetPerchAllowed(0, true, false); DigitalQuetzal.SetPerchAllowed(1, true, false);
            world = new GameObject("Quetzal studio");
            cameraObject = new GameObject("Studio camera", typeof(Camera)); camera = cameraObject.GetComponent<Camera>();
            camera.backgroundColor = LabVisuals.Hex("#0A1C2A"); camera.clearFlags = CameraClearFlags.SolidColor; camera.nearClipPlane = .01f; camera.fieldOfView = 50;
            eyeObject = new GameObject("Studio eye"); eyeObject.transform.SetPositionAndRotation(Eye, Quaternion.identity);
            bird = DigitalQuetzal.Create(world.transform); bird.enabled = false; bird.ViewCamera = camera;
            leftGlove = new GameObject("Left glove").AddComponent<ArmoredHandVisual>(); leftGlove.Initialize(true); leftGlove.Viewer = eyeObject.transform;
            rightGlove = new GameObject("Right glove").AddComponent<ArmoredHandVisual>(); rightGlove.Initialize(false); rightGlove.Viewer = eyeObject.transform;
            leftGlove.transform.SetParent(world.transform); rightGlove.transform.SetParent(world.transform);
            target = new RenderTexture(1280, 960, 24); image = new Texture2D(1280, 960, TextureFormat.RGB24, false);
            captures = Path.Combine(System.Environment.GetEnvironmentVariable("GG_RECOVERY_CAPTURE_DIR") ?? "Validation", "quetzal-studio");
            Directory.CreateDirectory(captures);
        }
        [TearDown] public void TearDown()
        {
            DigitalQuetzal.SetPerchAllowed(0, leftAllowed, false); DigitalQuetzal.SetPerchAllowed(1, rightAllowed, false);
            Object.Destroy(world); Object.Destroy(cameraObject); Object.Destroy(eyeObject);
            if (target) { target.Release(); Object.Destroy(target); }
            if (image) Object.Destroy(image);
        }

        // A synthetic hand in its own frame: x toward the thumb side for a left hand palm down, y the back of the hand,
        // z along the fingers. pointing: index straight, the other fingers curled.
        static Vector3[] LocalHand(bool left, bool pointing)
        {
            var p = new Vector3[26]; float side = left ? 1 : -1;
            p[0] = Vector3.zero; p[1] = new Vector3(0, -.006f, .045f);
            p[2] = new Vector3(.018f * side, -.01f, .018f); p[3] = new Vector3(.04f * side, -.014f, .042f); p[4] = new Vector3(.055f * side, -.016f, .066f); p[5] = new Vector3(.064f * side, -.017f, .088f);
            float[] x = { .022f, .003f, -.016f, -.031f }, knuckle = { .082f, .086f, .08f, .072f };
            float[][] bones = { new[] { .041f, .025f, .021f }, new[] { .046f, .029f, .022f }, new[] { .043f, .026f, .021f }, new[] { .033f, .02f, .018f } };
            for (int f = 0; f < 4; f++)
            {
                int start = 6 + f * 5; bool curl = pointing && f > 0;
                p[start] = new Vector3(x[f] * .55f * side, -.002f, .012f);
                var at = new Vector3(x[f] * side, 0, knuckle[f]); p[start + 1] = at;
                var dir = new Vector3(x[f] * .25f * side, -.06f, 1).normalized;
                for (int j = 0; j < 3; j++)
                {
                    if (curl) dir = Quaternion.AngleAxis(58, Vector3.right) * dir;
                    at += dir * bones[f][j]; p[start + 2 + j] = at;
                }
            }
            return p;
        }
        static Vector3[] PlaceHand(Vector3[] local, Vector3 wrist, Quaternion frame)
        {
            var world = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++) world[i] = wrist + frame * local[i];
            return world;
        }
        static readonly bool[] AllTracked = Enumerable.Repeat(true, 26).ToArray();
        QuetzalArm Arm(Vector3[] joints, Quaternion frame, bool left, ArmoredHandVisual glove)
        {
            glove.ApplyPoses(joints, AllTracked, frame);
            return QuetzalSenses.FromHand(joints, AllTracked, frame, left, Eye, Vector3.forward);
        }
        // Left forearm held out across the body, the back of the hand up: the perch offer.
        Vector3[] leftJoints; Quaternion leftFrame;
        QuetzalArm OfferedLeft()
        {
            leftFrame = Quaternion.LookRotation(new Vector3(.75f, -.05f, .66f).normalized, Vector3.up);
            leftJoints = PlaceHand(LocalHand(true, false), new Vector3(-.12f, 1.2f, .38f), leftFrame);
            return Arm(leftJoints, leftFrame, true, leftGlove);
        }
        void Capture(string name)
        {
            camera.targetTexture = target; camera.Render();
            var old = RenderTexture.active;
            try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); }
            finally { RenderTexture.active = old; camera.targetTexture = null; }
            File.WriteAllBytes(Path.Combine(captures, name + ".png"), image.EncodeToPNG());
        }
        void Look(Vector3 from, Vector3 at) { camera.transform.position = from; camera.transform.LookAt(at); }

        [UnityTest] public IEnumerator CalledBirdLandsOnTheGauntletRailAndCanBePettedAndStepUp()
        {
            var left = OfferedLeft(); Assert.IsTrue(left.Offered, "The synthetic left arm is a valid perch offer");
            var right = default(QuetzalArm);
            bird.Senses.Simulate(Eye, Quaternion.identity, left, right);
            // Materialising out of the projector.
            bird.Advance(.4f);
            Look(DigitalQuetzal.RestContact + new Vector3(.35f, .12f, -.6f), DigitalQuetzal.RestContact + Vector3.up * .1f); Capture("00-materialising");
            Look(Eye + new Vector3(.05f, .02f, 0), new Vector3(-.4f, 1.25f, 1.2f));
            Capture("00-rest-and-offer");
            // Approach: curved flight toward the arm.
            for (int i = 0; i < 300 && bird.State != DigitalQuetzal.Mode.Approach; i++) bird.Advance(.02f);
            Assert.AreEqual(DigitalQuetzal.Mode.Approach, bird.State);
            bird.Advance(.9f); yield return null;
            Look(Eye + new Vector3(.25f, .05f, -.1f), bird.Bird.position); Capture("01-approach");
            for (int i = 0; i < 400 && bird.State != DigitalQuetzal.Mode.Landing; i++) bird.Advance(.02f);
            Assert.AreEqual(DigitalQuetzal.Mode.Landing, bird.State);
            bird.Advance(.32f); yield return null;
            Look(Eye + new Vector3(.3f, -.1f, .05f), bird.Bird.position + Vector3.down * .05f); Capture("02-landing-flare");
            Look(bird.Bird.position + new Vector3(.75f, .05f, .1f), bird.Bird.position); Capture("03-landing-flare-side");
            for (int i = 0; i < 200 && !bird.Perched; i++) bird.Advance(.02f);
            Assert.IsTrue(bird.Perched); Assert.AreEqual(0, bird.PerchArm);
            bird.Advance(1.5f); yield return null;
            // On the rail: the feet are where the gauntlet's rail is.
            Assert.Less(Vector3.Distance(bird.FootContact, leftGlove.PerchRailTop), .02f, "The quetzal stands on the glove's perch rail");
            var perched = bird.Bird.position;
            Look(Eye + new Vector3(.02f, -.02f, .02f), perched + Vector3.down * .06f); Capture("04-perched-first-person");
            Look(perched + new Vector3(.32f, .04f, -.42f), perched + Vector3.down * .05f); Capture("05-perched-three-quarter");
            var flank = Vector3.Cross(Vector3.up, bird.Bird.forward).normalized;
            Look(perched + flank * .62f + Vector3.down * .08f, perched + Vector3.down * .12f); Capture("06-perched-profile");
            Look(perched - Vector3.ProjectOnPlane(bird.Bird.forward, Vector3.up).normalized * .7f + Vector3.up * .1f, perched + Vector3.down * .2f); Capture("07-perched-back");
            Look(bird.Head.position + bird.Head.forward * .2f + flank * .12f + Vector3.up * .02f, bird.Head.position); Capture("08-head");
            // Petting the crown with the right index finger.
            var crown = bird.Head.TransformPoint(new Vector3(0, .055f, .03f));
            var rightFrame = Quaternion.LookRotation((crown - new Vector3(.25f, 1.25f, .2f)).normalized, Vector3.up);
            var local = LocalHand(false, true);
            var rightJoints = PlaceHand(local, crown - rightFrame * local[10], rightFrame);
            right = Arm(rightJoints, rightFrame, false, rightGlove);
            bird.Senses.Simulate(Eye, Quaternion.identity, left, right);
            bird.Advance(.8f); yield return null;
            Assert.Greater(bird.Pets, 0); Assert.Greater(bird.Contentment, .2f);
            Look(perched + new Vector3(.35f, .1f, -.3f), perched); Capture("09-petting-crown");
            // Step-up: a straight, level right index finger against the lower breast.
            var step = bird.Bird.TransformPoint(new Vector3(0, -.058f, .062f));
            rightFrame = Quaternion.LookRotation(new Vector3(-1, 0, -.15f).normalized, Vector3.up);
            local = LocalHand(false, true);
            var mid = Vector3.Lerp(local[7], local[10], .45f);
            rightJoints = PlaceHand(local, step - rightFrame * mid, rightFrame);
            right = Arm(rightJoints, rightFrame, false, rightGlove);
            Assert.IsTrue(right.FingerPerch, "The synthetic pointing hand offers a finger perch");
            bird.Senses.Simulate(Eye, Quaternion.identity, left, right);
            for (int i = 0; i < 150 && !(bird.Perched && bird.PerchArm == 1); i++) bird.Advance(.02f);
            Assert.IsTrue(bird.Perched); Assert.AreEqual(1, bird.PerchArm); Assert.AreEqual(DigitalQuetzal.PerchSite.Finger, bird.Site);
            Assert.AreEqual(1, bird.StepUps);
            bird.Advance(1.2f); yield return null;
            Assert.AreEqual(1, bird.PerchArm, "The bird stays on the finger while the other arm is still held out");
            Look(bird.Bird.position + new Vector3(.38f, .02f, -.38f), bird.Bird.position + Vector3.down * .05f); Capture("10-step-up-finger");
            // The gloves on their own.
            Look(Eye + new Vector3(0, -.05f, 0), leftJoints[0] + Vector3.down * .02f + Vector3.forward * .02f); Capture("11-gauntlet-first-person");
            yield return null;
        }
        [UnityTest] public IEnumerator FastArmMakesTheBirdTakeOffAndReturnToTheProjector()
        {
            var left = OfferedLeft();
            bird.Senses.Simulate(Eye, Quaternion.identity, left, default);
            bird.Advance(10); Assert.IsTrue(bird.Perched);
            left.Velocity = Vector3.up * 2; bird.Senses.Simulate(Eye, Quaternion.identity, left, default);
            bird.Advance(.1f); Assert.AreEqual(DigitalQuetzal.Mode.Leaving, bird.State);
            left.Velocity = Vector3.zero; left.Offered = false; bird.Senses.Simulate(Eye, Quaternion.identity, left, default);
            bird.Advance(.6f); yield return null;
            Look(Eye + new Vector3(.2f, 0, 0), bird.Bird.position); Capture("12-takeoff");
            bird.Advance(8);
            Assert.AreEqual(DigitalQuetzal.Mode.Resting, bird.State);
            Assert.Less(Vector3.Distance(bird.FootContact, DigitalQuetzal.RestContact), .03f, "Lands back on the projector's light perch");
            Look(DigitalQuetzal.RestContact + new Vector3(.4f, .15f, -.7f), DigitalQuetzal.RestContact + Vector3.up * .1f); Capture("13-resting-on-projector");
        }
    }
}
