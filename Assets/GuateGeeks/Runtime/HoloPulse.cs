using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // A one-shot shockwave of light across the projection table: confirms "the system took it".
    // Skipped entirely with reduced motion; it is decoration and never represents AWS traffic.
    public sealed class HoloPulse : MonoBehaviour
    {
        LineRenderer line;
        float age, delay, duration, from, to, width;
        public static void Spawn(Transform parent, Vector3 center, float from, float to, Color color, float width = .035f, float delay = 0, float duration = .9f)
        {
            if (!parent || (LabFeedback.Current && LabFeedback.Current.ReducedMotion)) return;
            var ring = LabVisuals.Ring(parent, Vector3.zero, 1, color, width, 96);
            ring.name = "Confirmation pulse"; ring.sharedMaterial = LabVisuals.Beam(color, false); ring.textureMode = LineTextureMode.Stretch;
            ring.transform.localPosition = center; ring.transform.localScale = new Vector3(from, 1, from); ring.enabled = delay <= 0;
            var pulse = ring.gameObject.AddComponent<HoloPulse>();
            pulse.line = ring; pulse.from = from; pulse.to = to; pulse.width = width; pulse.delay = delay; pulse.duration = duration;
        }
        void Update()
        {
            age += Time.unscaledDeltaTime;
            float t = (age - delay) / duration;
            if (t < 0) return;
            line.enabled = true;
            if (t >= 1 || (LabFeedback.Current && LabFeedback.Current.ReducedMotion)) { Destroy(gameObject); return; }
            float eased = 1 - Mathf.Pow(1 - t, 3), radius = Mathf.Lerp(from, to, eased);
            transform.localScale = new Vector3(radius, 1, radius);
            line.widthMultiplier = width * (1 - t) * (1 - t);
        }
    }
}
