using System;
using UnityEngine;
using UnityEngine.UI;

namespace GuateGeeks.AwsVr
{
    public sealed class LabTarget : MonoBehaviour
    {
        public Action Action;
        public HoloPanelGraphic Surface;
        public TMPro.TMP_Text Label;
        public Color Accent = Color.white;
        public NodeView Node;
        public int Port;
        public LabMenu Menu;
        public bool Available = true;
        // Optional text shown only while aimed at (e.g. a quiet grip bar that explains itself on hover).
        public string HoverLabel;
        string idleLabel;
        int hoverCount;
        float highlight, flash;
        public bool Hovered => hoverCount > 0;
        public void Hover(bool value)
        {
            bool was = hoverCount > 0;
            hoverCount = Mathf.Max(0, hoverCount + (value ? 1 : -1));
            if (Node) Node.SetHovered(hoverCount > 0);
            if (!was && hoverCount > 0 && Available && (Surface || Port != 0 || Node)) LabFeedback.Current?.Play(LabFeedback.Hover);
            if (Label && !string.IsNullOrEmpty(HoverLabel))
            {
                if (hoverCount > 0 && !was) { idleLabel = Label.text; Label.text = HoverLabel; }
                else if (hoverCount == 0 && was && idleLabel != null) Label.text = idleLabel;
            }
            Refresh();
        }
        public void SetAvailable(bool value) { Available = value; Refresh(); }
        bool Ghost => Surface && Surface.Ghost;
        static readonly Color PressedFill = new Color(.16f, .5f, .6f, .96f);
        void Refresh()
        {
            bool hot = hoverCount > 0 && Available;
            if (Surface)
            {
                var fill = !Available ? (Ghost ? LabVisuals.Rgba("#081A24", .12f) : LabVisuals.Rgba("#081A24", .55f))
                    : hot ? LabVisuals.Rgba("#1B4C61", .92f)
                    : Ghost ? LabVisuals.Rgba("#0D2A3A", .22f) : LabVisuals.Rgba("#0D2B3B", .80f);
                // A press flashes the slab brighter for a moment, so a fingertip press reads as a physical click.
                Surface.color = flash > 0 && Available ? Color.Lerp(fill, PressedFill, flash) : fill;
            }
            if (Label) Label.color = !Available ? LabVisuals.Muted * .65f : hot ? Color.Lerp(Accent, LabVisuals.Ice, .45f) : Accent;
            if (!Surface) return;
            if (!isActiveAndEnabled || (LabFeedback.Current && LabFeedback.Current.ReducedMotion)) { highlight = hot ? 1 : 0; Surface.Highlight = highlight; }
        }
        void Update()
        {
            if (!Surface) return;
            if (flash > 0) { flash = LabFeedback.Current && LabFeedback.Current.ReducedMotion ? 0 : Mathf.Max(0, flash - Time.unscaledDeltaTime * 5); Refresh(); }
            float goal = hoverCount > 0 && Available ? 1 : 0;
            if (Mathf.Approximately(highlight, goal)) return;
            highlight = LabFeedback.Current && LabFeedback.Current.ReducedMotion ? goal : Mathf.MoveTowards(highlight, goal, Time.unscaledDeltaTime * 7);
            Surface.Highlight = highlight;
        }
        void OnDisable()
        {
            // A destroyed or hidden control must not keep a stale hover label or glow.
            if (Label && idleLabel != null && hoverCount > 0) Label.text = idleLabel;
            hoverCount = 0; highlight = 0; flash = 0; if (Surface) Surface.Highlight = 0; Refresh();
        }
        public void Activate()
        {
            if (!Available) return;
            if (Surface && isActiveAndEnabled) { flash = 1; Refresh(); }
            LabFeedback.Current?.Play(); Action?.Invoke();
        }
    }
}
