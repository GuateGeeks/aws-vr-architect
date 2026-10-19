using NUnit.Framework;
using UnityEngine;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class QuetzalRecoveryTests
    {
        GameObject world;
        DigitalQuetzal bird;
        bool leftAllowed, rightAllowed;
        static QuetzalArm Offered => new QuetzalArm { Tracked = true, Offered = true,
            Wrist = new Vector3(-.25f, 1.2f, .4f), Elbow = new Vector3(-.5f, 1.2f, .4f), Dorsal = Vector3.up };
        [SetUp] public void SetUp()
        {
            leftAllowed = DigitalQuetzal.PerchAllowed(0); rightAllowed = DigitalQuetzal.PerchAllowed(1);
            DigitalQuetzal.SetPerchAllowed(0, true, false); DigitalQuetzal.SetPerchAllowed(1, true, false);
            world = new GameObject("Recovery test world"); bird = DigitalQuetzal.Create(world.transform);
            bird.Senses.Simulate(new Vector3(0, 1.65f, 0), Quaternion.identity, Offered, default);
        }
        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(world);
            DigitalQuetzal.SetPerchAllowed(0, leftAllowed, false); DigitalQuetzal.SetPerchAllowed(1, rightAllowed, false);
        }
        [Test] public void OfferedArmLeadsToApproachLandingAndStablePerch()
        {
            bird.Advance(1.5f); Assert.AreEqual(DigitalQuetzal.Mode.Approach, bird.State);
            bird.Advance(10); Assert.IsTrue(bird.Perched); Assert.AreEqual(0, bird.PerchArm);
            Assert.That(bird.transform.localPosition.sqrMagnitude, Is.LessThan(.000001f), "Spatial audio must not move the whole bird simulation root");
            Assert.Less(Vector3.Distance(bird.FootContact, world.transform.TransformPoint(bird.PerchContact)), .025f);
            Assert.Greater(bird.Reveal, .99f); Assert.AreEqual(QuetzalMesh.CrestSegments, bird.CrestFeathers.Length);
        }
        [Test] public void TrackingLossAndPermissionRevocationReleaseTheBird()
        {
            bird.Advance(12); Assert.IsTrue(bird.Perched);
            bird.Senses.Simulate(new Vector3(0, 1.65f, 0), Quaternion.identity, default, default);
            bird.Advance(.3f); Assert.AreEqual(DigitalQuetzal.Mode.Leaving, bird.State);
            bird.Materialize(); bird.Senses.Simulate(new Vector3(0, 1.65f, 0), Quaternion.identity, Offered, default);
            bird.Advance(12); Assert.IsTrue(bird.Perched);
            DigitalQuetzal.SetPerchAllowed(0, false, false);
            bird.Advance(.02f); Assert.AreEqual(DigitalQuetzal.Mode.Leaving, bird.State);
        }
        [Test] public void DisabledOrBusyArmIsNeverUsedForLanding()
        {
            DigitalQuetzal.SetPerchAllowed(0, false, false); bird.Advance(10); Assert.AreEqual(-1, bird.PerchArm);
            DigitalQuetzal.SetPerchAllowed(0, true, false);
            var a = Offered; a.Busy = true; bird.Senses.Simulate(Vector3.up * 1.65f, Quaternion.identity, a, default);
            bird.Advance(10); Assert.IsFalse(bird.Perched); Assert.AreEqual(-1, bird.PerchArm);
        }
        [Test] public void CrownContactPetsButFastContactMakesTheBirdLeave()
        {
            bird.Advance(12); Assert.IsTrue(bird.Perched);
            var other = new QuetzalArm { Tracked = true, HasFingertip = true };
            other.SetTouchPoint(1, bird.Head.TransformPoint(new Vector3(0, .055f, .03f)));
            bird.Senses.Simulate(Vector3.up * 1.65f, Quaternion.identity, Offered, other);
            bird.Advance(.02f); Assert.Greater(bird.Pets, 0);
            other.Velocity = Vector3.right * 2;
            other.SetTouchPoint(1, bird.Head.TransformPoint(new Vector3(0, .055f, .03f)));
            bird.Senses.Simulate(Vector3.up * 1.65f, Quaternion.identity, Offered, other);
            bird.Advance(.6f); Assert.Greater(bird.Shoos, 0); Assert.IsFalse(bird.Perched);
        }
        [Test] public void DorsalButtonRequiresApproachWithdrawalAndLockout()
        {
            var button = new BeaconButton(); var normal = Vector3.up;
            Assert.IsFalse(button.Step(true, normal * .005f, Vector3.zero, normal, 1));
            Assert.IsFalse(button.Step(true, normal * .035f, Vector3.zero, normal, 1.1f));
            Assert.IsTrue(button.Step(true, normal * .005f, Vector3.zero, normal, 1.2f));
            Assert.IsFalse(button.Step(true, normal * .005f, Vector3.zero, normal, 2));
            button.Step(true, normal * .035f, Vector3.zero, normal, 2.1f);
            Assert.IsTrue(button.Step(true, normal * .005f, Vector3.zero, normal, 2.2f));
            button.Step(false, Vector3.zero, Vector3.zero, normal, 3);
            Assert.IsFalse(button.Step(true, normal * .005f, Vector3.zero, normal, 4));
        }
        [Test] public void ControllerInputDoesNotFabricateTouchAndForearmEstimateIsBounded()
        {
            var a = QuetzalSenses.FromController(new Vector3(-.2f, 1.2f, .4f), Quaternion.identity, Vector3.zero,
                Vector3.forward, true, true, Vector3.up * 1.65f, Vector3.forward);
            Assert.AreEqual(0, a.TouchMask); Assert.IsFalse(a.HasFingertip); Assert.IsTrue(a.Pointing);
            Assert.That(Vector3.Distance(a.Wrist, a.Elbow), Is.EqualTo(QuetzalSenses.ForearmLength).Within(.0001f));
        }
        [Test] public void NearPanelGrabKeepsGripOffsetAndExcludesAnotherOwner()
        {
            var panel = new GameObject("Near panel"); panel.transform.SetParent(world.transform);
            var menu = panel.AddComponent<LabMenu>(); menu.Persist = false;
            panel.transform.position = new Vector3(0, 1, 1);
            object owner = new object();
            Assert.IsTrue(menu.TryGrabNear(owner, new Vector3(.1f, 1, 1), Quaternion.identity));
            Assert.IsFalse(menu.TryGrabNear(new object(), Vector3.zero, Quaternion.identity));
            menu.MoveNear(owner, new Vector3(.2f, 1, 1), Quaternion.identity);
            Assert.That(panel.transform.position.x, Is.EqualTo(.1f).Within(.0001f));
            menu.Release(owner); Assert.IsFalse(menu.Grabbed);
        }
        // A bird's head turns about the vertical and stays level; it must not corkscrew with the upright body.
        [Test] public void HeadInspectsASideTargetLevelAndInQuickFixations()
        {
            Random.InitState(5);
            bird.Advance(12); Assert.IsTrue(bird.Perched);
            var heading = Vector3.ProjectOnPlane(bird.Bird.forward, Vector3.up).normalized;
            var finger = bird.Head.position + Quaternion.AngleAxis(70, Vector3.up) * heading * .2f;
            var other = new QuetzalArm { Tracked = true, HasFingertip = true, Wrist = finger + Vector3.right * .15f, Elbow = finger + Vector3.right * .4f };
            other.SetTouchPoint(1, finger);
            bird.Senses.Simulate(new Vector3(0, 1.65f, 0), Quaternion.identity, Offered, other);
            bird.Advance(.5f);
            int frames = 120, level = 0, onTarget = 0, still = 0; int saccades = bird.Saccades; var last = bird.Head.rotation;
            for (int i = 0; i < frames; i++)
            {
                bird.Advance(.02f);
                if (Vector3.Dot(bird.Head.up, Vector3.up) > .85f) level++;
                var to = (finger - bird.Head.position).normalized;
                var left = bird.Head.rotation * new Vector3(-.866f, 0, .5f); var right = bird.Head.rotation * new Vector3(.866f, 0, .5f);
                if (Mathf.Max(Vector3.Dot(to, left), Vector3.Dot(to, right), Vector3.Dot(to, bird.Head.forward)) > .9f) onTarget++;
                if (Quaternion.Angle(last, bird.Head.rotation) < .2f) still++;
                last = bird.Head.rotation;
            }
            Assert.AreEqual("Finger", bird.Looking);
            Assert.Greater(level, frames * .85f, "The head stays level while it turns");
            Assert.Greater(onTarget, frames * .8f, "An eye or the bill is on the finger");
            Assert.Greater(still, frames * .5f, "Between saccades the head holds still");
            Assert.Greater(bird.Saccades - saccades, 1, "The finger is inspected in several fixations");
        }
        [Test] public void HeadStaysSteadyInSpaceWhileThePerchBobs()
        {
            Random.InitState(9);
            bird.Advance(12); Assert.IsTrue(bird.Perched);
            float bodyLow = float.MaxValue, bodyHigh = float.MinValue, headLow = float.MaxValue, headHigh = float.MinValue;
            for (int i = 0; i < 100; i++)
            {
                var arm = Offered; var bobbing = Vector3.up * Mathf.Sin(i * .02f * Mathf.PI * 2 * 2) * .02f;
                arm.Wrist += bobbing; arm.Elbow += bobbing;
                bird.Senses.Simulate(new Vector3(0, 1.65f, 0), Quaternion.identity, arm, default);
                bird.Advance(.02f);
                if (i < 25) continue;
                bodyLow = Mathf.Min(bodyLow, bird.Bird.position.y); bodyHigh = Mathf.Max(bodyHigh, bird.Bird.position.y);
                headLow = Mathf.Min(headLow, bird.Head.position.y); headHigh = Mathf.Max(headHigh, bird.Head.position.y);
            }
            Assert.IsTrue(bird.Perched);
            Assert.Less(headHigh - headLow, (bodyHigh - bodyLow) * .7f, "The head rides out the perch's motion");
        }
        // Pokes make it flinch and peck back; enough of them shoo it off the arm, and it comes back to land.
        [Test] public void PokesEndInAPeckThenAShooAndTheBirdReturns()
        {
            Random.InitState(3);
            bird.Advance(12); Assert.IsTrue(bird.Perched);
            for (int poke = 0; poke < 6 && bird.Perched; poke++)
            {
                // Each poke is aimed at the crown where it is now (the bird ducks and shifts after a poke).
                var crown = bird.Head.TransformPoint(new Vector3(0, .055f, .03f));
                var away = crown + Vector3.right * .3f;
                var other = new QuetzalArm { Tracked = true, HasFingertip = true, Velocity = Vector3.left * .8f };
                other.SetTouchPoint(1, crown);
                bird.Senses.Simulate(Vector3.up * 1.65f, Quaternion.identity, Offered, other); bird.Advance(.06f);
                if (poke == 0) { bird.Advance(.5f); Assert.Greater(bird.Pecks, 0, "It pecks back at a finger still at its head"); }
                other = new QuetzalArm { Tracked = true, HasFingertip = true }; other.SetTouchPoint(1, away);
                bird.Senses.Simulate(Vector3.up * 1.65f, Quaternion.identity, Offered, other); bird.Advance(.3f);
            }
            Assert.GreaterOrEqual(bird.Flinches, 2); Assert.Greater(bird.Shoos, 0); Assert.IsFalse(bird.Perched);
            bird.Senses.Simulate(Vector3.up * 1.65f, Quaternion.identity, Offered, default);
            bird.Advance(12);
            Assert.IsTrue(bird.Perched, "Once calm it comes back to the offered arm"); Assert.AreEqual(0, bird.PerchArm);
        }
        [Test] public void APalmOverItsHeadMakesItCrouchAndWatch()
        {
            Random.InitState(5);
            bird.Advance(12); Assert.IsTrue(bird.Perched);
            float standing = bird.Bird.position.y;   // the body crouches; the head itself turns up to watch the hand
            var other = new QuetzalArm { Tracked = true, PalmFacing = Vector3.down };
            other.SetTouchPoint(QuetzalArm.PalmPoint, bird.Head.position + Vector3.up * .14f);
            bird.Senses.Simulate(Vector3.up * 1.65f, Quaternion.identity, Offered, other);
            bird.Advance(1);
            Assert.AreEqual("Palm", bird.Looking);
            Assert.Less(bird.Bird.position.y, standing - .004f, "It crouches under the hand");
        }
        [Test] public void ALargeTurnOfThePersonIsTakenWithAHop()
        {
            bird.Advance(12); Assert.IsTrue(bird.Perched);
            var before = Vector3.ProjectOnPlane(bird.Bird.forward, Vector3.up).normalized;
            var eye = new Vector3(-.6f, 1.65f, -.3f);
            bird.Senses.Simulate(eye, Quaternion.identity, Offered, default);
            bool hopped = false;
            for (int i = 0; i < 60; i++) { bird.Advance(.02f); hopped |= bird.Activity == "Turn"; }
            bird.Advance(1);
            var toPerson = Vector3.ProjectOnPlane(eye - bird.Bird.position, Vector3.up).normalized;
            var after = Vector3.ProjectOnPlane(bird.Bird.forward, Vector3.up).normalized;
            Assert.IsTrue(hopped, "A big change of facing is a hop-turn");
            Assert.Greater(Vector3.Dot(after, toPerson), .9f); Assert.Less(Vector3.Dot(before, after), .8f);
            Assert.Less(Vector3.Distance(bird.FootContact, world.transform.TransformPoint(bird.PerchContact)), .025f, "It lands back on the perch");
        }
        // The synthesised voice: every sound is audible, bounded and finite; written as WAV files for listening.
        [Test] public void SynthesisedSoundsAreCleanAndExported()
        {
            string folder = System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("GG_RECOVERY_CAPTURE_DIR") ?? "Validation", "quetzal-sounds");
            System.IO.Directory.CreateDirectory(folder);
            int count = 0;
            foreach (var (name, samples) in DigitalQuetzal.SoundBank())
            {
                float peak = 0; foreach (var s in samples) { Assert.IsFalse(float.IsNaN(s) || float.IsInfinity(s), name); peak = Mathf.Max(peak, Mathf.Abs(s)); }
                Assert.Greater(peak, .01f, name + " is audible"); Assert.Greater(samples.Length, DigitalQuetzal.SoundRate / 50, name);
                using (var file = new System.IO.BinaryWriter(System.IO.File.Create(System.IO.Path.Combine(folder, name + ".wav"))))
                {
                    int rate = DigitalQuetzal.SoundRate, bytes = samples.Length * 2;
                    file.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); file.Write(36 + bytes); file.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                    file.Write(16); file.Write((short)1); file.Write((short)1); file.Write(rate); file.Write(rate * 2); file.Write((short)2); file.Write((short)16);
                    file.Write(System.Text.Encoding.ASCII.GetBytes("data")); file.Write(bytes);
                    foreach (var s in samples) file.Write((short)Mathf.RoundToInt(Mathf.Clamp(s / peak * .85f, -1, 1) * 32767));
                }
                count++;
            }
            Assert.AreEqual(7, count, "The seven sounds of the shipped quetzal");
        }
        [Test] public void PerchedFeetRemainOnTheArmWithATranslatedRotatedLab()
        {
            world.transform.SetPositionAndRotation(new Vector3(2, 0, -1), Quaternion.Euler(0, 38, 0));
            bird.Advance(12); Assert.IsTrue(bird.Perched);
            Assert.Less(Vector3.Distance(bird.FootContact, world.transform.TransformPoint(bird.PerchContact)), .025f);
            var toPerson = bird.Senses.Eye - bird.Bird.position; toPerson.y = 0;
            var forward = bird.Bird.forward; forward.y = 0;
            Assert.Greater(Vector3.Dot(forward.normalized, toPerson.normalized), .7f);
        }
    }
}
