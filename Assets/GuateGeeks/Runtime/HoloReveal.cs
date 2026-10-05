using System.Collections.Generic;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // Panels materialise instead of popping: a scan line sweeps the glass while content fades in.
    // Panels created in the same frame are staggered, so the lab assembles itself on start.
    // Reduced motion shows panels immediately. Interaction is never blocked during the reveal.
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class HoloReveal : MonoBehaviour
    {
        public const float Duration = .32f;
        static readonly List<HoloReveal> running = new List<HoloReveal>();
        static int staggerFrame = -1, staggerIndex;
        CanvasGroup group;
        HoloPanelGraphic surface;
        float elapsed, delay;
        bool animating;
        public bool Revealing => animating;

        // Finish every reveal now (used before deterministic screenshots).
        // Canvas meshes rebuild before normal rendering, but a manual Camera.Render() needs an explicit rebuild.
        public static void CompleteAll() { for (int i = running.Count - 1; i >= 0; i--) if (running[i]) running[i].Finish(); running.Clear(); Canvas.ForceUpdateCanvases(); }

        void OnEnable()
        {
            if (!group) group = GetComponent<CanvasGroup>();
            if (!surface) surface = GetComponent<HoloPanelGraphic>();
            if (LabFeedback.Current && LabFeedback.Current.ReducedMotion) { Finish(); return; }
            if (Time.frameCount != staggerFrame) { staggerFrame = Time.frameCount; staggerIndex = 0; }
            delay = Mathf.Min(staggerIndex++ * .07f, .6f); elapsed = 0; animating = true;
            if (!running.Contains(this)) running.Add(this);
            Apply(0);
            if (delay <= 0) LabFeedback.Current?.Play(LabFeedback.Open);
        }
        void OnDisable() { if (animating) Finish(); running.Remove(this); }
        void Update()
        {
            if (!animating) return;
            float before = elapsed - delay; elapsed += Time.unscaledDeltaTime; float t = elapsed - delay;
            if (before < 0 && t >= 0 && delay > 0) LabFeedback.Current?.Play(LabFeedback.Open);
            if (LabFeedback.Current && LabFeedback.Current.ReducedMotion) { Finish(); return; }
            float progress = Mathf.Clamp01(t / Duration);
            Apply(progress);
            if (progress >= 1) Finish();
        }
        void Apply(float progress)
        {
            float eased = 1 - (1 - progress) * (1 - progress);
            if (group) group.alpha = Mathf.Lerp(0, 1, Mathf.Clamp01(eased * 1.25f));
            if (surface) surface.Reveal = eased;
        }
        void Finish()
        {
            animating = false; running.Remove(this);
            if (group) group.alpha = 1;
            if (surface) surface.Reveal = 1;
        }
    }
}
