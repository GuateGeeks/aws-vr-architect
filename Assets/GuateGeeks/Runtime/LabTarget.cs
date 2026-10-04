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
        int hoverCount;
        public void Hover(bool value) { hoverCount = Mathf.Max(0, hoverCount + (value ? 1 : -1)); if (Node) Node.SetHovered(hoverCount > 0); Refresh(); }
        public void SetAvailable(bool value) { Available = value; Refresh(); }
        void Refresh()
        {
            if (Surface) Surface.color = !Available ? LabVisuals.Hex("#091C28") : hoverCount > 0 ? LabVisuals.Hex("#235568") : LabVisuals.Hex("#102F40");
            if (Label) Label.color = Available ? Accent : LabVisuals.Muted * .65f;
        }
        public void Activate() { if (Available) { LabFeedback.Current?.Play(); Action?.Invoke(); } }
    }
}
