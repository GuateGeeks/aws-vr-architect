using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // Reconstructed against the production ARM64 Step/Flash paths.
    public sealed class BeaconButton
    {
        public const float Radius = .02f, Contact = .012f, Rearm = .028f, Reach = .06f, Lockout = .5f;
        bool armed;
        float lastPress = -10;
        public float Proximity { get; private set; }
        public bool Step(bool valid, Vector3 tip, Vector3 center, Vector3 normal, float time)
        {
            if (!valid) { armed = false; Proximity = 0; return false; }
            var delta = tip - center;
            float front = Vector3.Dot(delta, normal);
            Proximity = front > -.01f ? Mathf.Clamp01(1 - (delta.magnitude - Contact) / (Reach - Contact)) : 0;
            float planar = (delta - normal * front).magnitude;
            if (front < -.015f || planar > .05f) { armed = false; return false; }
            if (front > Rearm) armed = true;
            if (!armed || planar >= Radius || front >= Contact || time - lastPress <= Lockout) return false;
            armed = false; lastPress = time; return true;
        }
        public float Flash(float time) => Mathf.Clamp01(1 - (time - lastPress) / .35f);
    }
}
