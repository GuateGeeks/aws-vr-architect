using UnityEngine;

namespace GuateGeeks.AwsVr
{
    public sealed class LinkView : MonoBehaviour
    {
        NodeView from, to;
        LineRenderer line, arrow;
        Transform packet;
        RectTransform label;
        Vector3 labelScale;
        readonly Vector3[] points = new Vector3[25];
        readonly Vector3[] arrowPoints = new Vector3[3];
        float previewTime = -1, delay, duration;
        bool reducedMotion, highlighted;
        TMPro.TMP_Text actionText;
        string meaning, simulationMeaning;
        Vector3 lastFrom, lastTo; float lastScaleFrom, lastScaleTo; bool shaped;
        float observedUntil,assistantUntil;
        public void FlashAssistant(){assistantUntil=Time.unscaledTime+2;}
        public bool ObservedConfirmation => Time.unscaledTime < observedUntil;
        public bool Flowing;
        public bool IsObservation => to && to.Model.kind == ServiceKind.CloudWatch;
        public string FromId => from.Model.id;
        public string ToId => to.Model.id;
        public bool PacketVisible => packet && packet.gameObject.activeSelf;
        public void Initialize(NodeView a, NodeView b, ArchitectureLab owner)
        {
            from = a; to = b;
            var color = IsObservation ? LabVisuals.Muted : LabVisuals.Cyan;
            line = LabVisuals.Line(transform, "Directed connection", points, color, BaseWidth);
            line.sharedMaterial = Beam(color); line.textureMode = LineTextureMode.Stretch; line.numCapVertices = 0;
            arrow = LabVisuals.Line(transform, "Direction", arrowPoints, color, .012f);
            arrow.enabled = !IsObservation;
            packet = LabVisuals.Shape(transform, "Illustrative message", PrimitiveType.Capsule, Vector3.zero, new Vector3(.045f, .055f, .045f), LabVisuals.Green).transform;
            packet.gameObject.SetActive(false);
            label = LabVisuals.Panel(transform, "Connection meaning", Vector3.zero, new Vector2(290, 66), background: false, movable: false);
            labelScale=label.localScale;
            var action = LabVisuals.Ghost(LabVisuals.Button(label, DesignSemantics.Operation(a.Model.kind, b.Model.kind), Vector2.zero, new Vector2(290, 66), () => owner.InspectLink(FromId, ToId), color));
            action.Label.fontSize = 18;
            actionText=action.Label; meaning=actionText.text; simulationMeaning="SIMULACIÓN · "+meaning;
        }
        // Data edges are soft additive beams with travelling dashes; observation edges stay quiet and static.
        float BaseWidth => IsObservation ? .012f : .024f;
        Material Beam(Color color) => LabVisuals.Beam(color, !IsObservation);
        public void SetHighlighted(bool value)
        {
            highlighted=value;
            line.sharedMaterial = Beam(value ? LabVisuals.Ice : IsObservation ? LabVisuals.Muted : LabVisuals.Cyan);
            line.widthMultiplier = value ? .036f : BaseWidth;
        }
        public void Preview(float startDelay, float seconds, bool reduced)
        {
            if (IsObservation) return;
            delay = startDelay; duration = seconds; previewTime = 0; reducedMotion = reduced; Flowing = true;
            observedUntil=0;
        }
        public void ConfirmObserved(string eventId)
        {
            if(IsObservation || string.IsNullOrEmpty(eventId)) return;
            observedUntil=Time.unscaledTime+3;
        }
        public void StopPreview() { Flowing = false; previewTime = -1; if (packet) packet.gameObject.SetActive(false); }
        Vector3 Point(float t)
        {
            var a = from.transform.TransformPoint(new Vector3(.37f, -.12f, -.08f));
            var b = to.transform.TransformPoint(new Vector3(-.37f, -.12f, -.08f));
            return Vector3.Lerp(a, b, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * .16f;
        }
        void LateUpdate()
        {
            if (!from || !to) return;
            // Rebuild the 25-point curve and arrow only when an endpoint moves or rescales.
            var fromPosition = from.transform.position; var toPosition = to.transform.position;
            float fromScale = from.transform.localScale.x, toScale = to.transform.localScale.x;
            bool moved = !shaped || fromPosition != lastFrom || toPosition != lastTo || fromScale != lastScaleFrom || toScale != lastScaleTo;
            lastFrom = fromPosition; lastTo = toPosition; lastScaleFrom = fromScale; lastScaleTo = toScale; shaped = true;
            var direction = (Point(.81f) - Point(.79f)).normalized;
            if (moved)
            {
                label.localScale=labelScale*Mathf.Max(.6f,fromScale);
                for (int i = 0; i < points.Length; i++) points[i] = transform.InverseTransformPoint(Point((float)i / (points.Length - 1)));
                line.SetPositions(points);
                var tip = Point(.8f);
                var side = Vector3.Cross(direction, Mathf.Abs(direction.y) > .9f ? Vector3.forward : Vector3.up).normalized * .035f;
                arrowPoints[0] = transform.InverseTransformPoint(tip - direction * .07f + side);
                arrowPoints[1] = transform.InverseTransformPoint(tip);
                arrowPoints[2] = transform.InverseTransformPoint(tip - direction * .07f - side);
                arrow.SetPositions(arrowPoints);
                label.position = Point(.5f) + Vector3.up * .10f;
            }
            if(Time.unscaledTime<assistantUntil)line.widthMultiplier=LabFeedback.Current && LabFeedback.Current.ReducedMotion?.036f:.03f+.01f*Mathf.Sin(Time.unscaledTime*7);
            else line.widthMultiplier=highlighted?.036f:BaseWidth;
            if (Camera.main) label.rotation = Quaternion.LookRotation(label.position - Camera.main.transform.position);
            if (previewTime >= 0) previewTime += Time.unscaledDeltaTime;
            bool visible = Flowing && !IsObservation && previewTime >= delay && previewTime <= delay + duration;
            packet.gameObject.SetActive(visible);
            if (visible) { packet.position = Point(reducedMotion || (LabFeedback.Current && LabFeedback.Current.ReducedMotion) ? .5f : (previewTime - delay) / duration); packet.up = direction; }
            if (previewTime > delay + duration) StopPreview();
            if(ObservedConfirmation) {
                packet.gameObject.SetActive(true); packet.position=Point(.5f);
                packet.localScale=Vector3.one*(LabFeedback.Current && LabFeedback.Current.ReducedMotion ? .065f : .065f+Mathf.Sin(LabFeedback.Clock*5)*.008f);
                actionText.text="CONFIRMADO · AWS"; actionText.color=LabVisuals.Orange;
            } else { actionText.text=visible?simulationMeaning:meaning; actionText.color=IsObservation?LabVisuals.Muted:LabVisuals.Cyan; }
        }
    }
}
