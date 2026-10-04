using NUnit.Framework;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class HandPinchTests
    {
        [Test] public void TrackingRecoveryCannotActivateUntilHandOpens()
        {
            var pinch = new HandPinchState();
            pinch.Step(true, 1); Assert.IsFalse(pinch.Started);
            pinch.Step(true, 0); pinch.Step(true, 1); Assert.IsTrue(pinch.Started);
            pinch.Step(false, 1); Assert.IsTrue(pinch.Released); Assert.IsFalse(pinch.Pressed);
            pinch.Step(true, 1); Assert.IsFalse(pinch.Started); Assert.IsFalse(pinch.Pressed);
            pinch.Step(true, 0); pinch.Step(true, .9f); Assert.IsTrue(pinch.Started);
        }
        [Test] public void PinchThresholdJitterDoesNotGenerateRepeatedClicks()
        {
            var pinch = new HandPinchState(); pinch.Step(true, 0); pinch.Step(true, .85f);
            foreach (float value in new[] { .78f, .81f, .61f, .8f })
            { pinch.Step(true, value); Assert.IsTrue(pinch.Pressed); Assert.IsFalse(pinch.Started); Assert.IsFalse(pinch.Released); }
            pinch.Step(true, .4f); Assert.IsTrue(pinch.Released); Assert.IsFalse(pinch.Pressed);
            pinch.Step(true, .4f); Assert.IsFalse(pinch.Released);
        }
    }
}
