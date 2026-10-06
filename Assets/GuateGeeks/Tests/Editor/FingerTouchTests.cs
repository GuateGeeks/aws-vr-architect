using NUnit.Framework;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class FingerTouchTests
    {
        static readonly object A = new object(), B = new object();
        // Approach from 3 cm, then hold contact: returns the finger that pressed (or -1) after the dwell.
        static int Press(MultiFingerTouch touch, object[] targets, float[] fronts, ref float now)
        {
            int pressed = -1;
            for (int i = 0; i < 6; i++) { now += .03f; int f = touch.Step(targets, fronts, now, .03f); if (f >= 0) pressed = f; }
            return pressed;
        }
        static void Approach(MultiFingerTouch touch, object[] targets, float[] fronts, ref float now, params int[] fingers)
        {
            var far = (float[])fronts.Clone(); foreach (int f in fingers) far[f] = .03f;
            now += .03f; touch.Step(targets, far, now, .03f);
        }
        [Test] public void EveryFingerCanPress()
        {
            for (int finger = 0; finger < MultiFingerTouch.Fingers; finger++)
            {
                var touch = new MultiFingerTouch(); var targets = new object[5]; var fronts = new float[5]; float now = 0;
                targets[finger] = A; Approach(touch, targets, fronts, ref now, finger);
                Assert.AreEqual(finger, Press(touch, targets, fronts, ref now), MultiFingerTouch.Names[finger] + " presses");
            }
        }
        [Test] public void FlatHandPressesOnlyTheDeepestFingerAndNeighboursMustWithdraw()
        {
            var touch = new MultiFingerTouch(); var targets = new object[5]; var fronts = new float[5]; float now = 0;
            targets[1] = A; targets[2] = B;
            Approach(touch, targets, fronts, ref now, 1, 2);
            fronts[1] = .001f; fronts[2] = .003f;
            Assert.AreEqual(1, Press(touch, targets, fronts, ref now), "The deeper index wins");
            now += 1; Assert.AreEqual(-1, Press(touch, targets, fronts, ref now), "A resting middle finger never fires later");
            Approach(touch, targets, fronts, ref now, 2); Assert.AreEqual(2, Press(touch, targets, fronts, ref now), "…until it withdraws and presses again");
        }
        [Test] public void RefractoryBlocksAnAccidentalSecondKey()
        {
            var touch = new MultiFingerTouch(); var targets = new object[5]; var fronts = new float[5]; float now = 0;
            targets[1] = A; targets[3] = B; fronts[1] = fronts[3] = .03f; touch.Step(targets, fronts, now, .03f);
            fronts[1] = 0; int first = -1;
            for (int i = 0; i < 3; i++) { now += .03f; int f = touch.Step(targets, fronts, now, .03f); if (f >= 0) first = f; }
            Assert.AreEqual(1, first, "The index presses A");
            // The ring finger lands on B right after and completes its dwell within 120 ms of the first press.
            fronts[3] = 0; int late = -1;
            for (int i = 0; i < 3; i++) { now += .03f; int f = touch.Step(targets, fronts, now, .03f); if (f >= 0) late = f; }
            Assert.AreEqual(-1, late, "Two keys from one hand within 120 ms are an accident");
            now += 1; Assert.AreEqual(-1, Press(touch, targets, fronts, ref now), "…and that finger must withdraw first");
        }
        [Test] public void ResetCancelsAPressInProgress()
        {
            var touch = new MultiFingerTouch(); var targets = new object[5]; var fronts = new float[5]; float now = 0;
            targets[4] = A; Approach(touch, targets, fronts, ref now, 4);
            now += .03f; touch.Step(targets, fronts, now, .03f); touch.Reset();
            Assert.AreEqual(-1, Press(touch, targets, fronts, ref now), "Tracking loss mid-press never completes it");
        }
    }
}
