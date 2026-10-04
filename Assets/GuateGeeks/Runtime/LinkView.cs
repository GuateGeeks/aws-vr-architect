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
        string meaning;
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
            line = LabVisuals.Line(transform, "Directed connection", points, color, IsObservation ? .004f : .008f);
            arrow = LabVisuals.Line(transform, "Direction", arrowPoints, color, .012f);
            arrow.enabled = !IsObservation;
            packet = LabVisuals.Shape(transform, "Illustrative message", PrimitiveType.Capsule, Vector3.zero, new Vector3(.045f, .055f, .045f), LabVisuals.Green).transform;
            packet.gameObject.SetActive(false);
            label = LabVisuals.Panel(transform, "Connection meaning", Vector3.zero, new Vector2(290, 66), background: false, movable: false);
            labelScale=label.localScale;
            var action = LabVisuals.Button(label, DesignSemantics.Operation(a.Model.kind, b.Model.kind), Vector2.zero, new Vector2(290, 66), () => owner.InspectLink(FromId, ToId), color);
            action.Label.fontSize = 17;
            actionText=action.Label; meaning=actionText.text;
        }
        public void SetHighlighted(bool value)
        {
            highlighted=value;
            line.sharedMaterial = LabVisuals.Material(value ? LabVisuals.White : IsObservation ? LabVisuals.Muted : LabVisuals.Cyan);
            line.widthMultiplier = value ? .014f : IsObservation ? .004f : .008f;
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
            label.localScale=labelScale*Mathf.Max(.6f,from.transform.localScale.x);
            for (int i = 0; i < points.Length; i++) points[i] = transform.InverseTransformPoint(Point((float)i / (points.Length - 1)));
            line.SetPositions(points);
            if(Time.unscaledTime<assistantUntil)line.widthMultiplier=LabFeedback.Current && LabFeedback.Current.ReducedMotion?.014f:.011f+.004f*Mathf.Sin(Time.unscaledTime*7);
            else line.widthMultiplier=highlighted?.014f:IsObservation?.004f:.008f;
            var tip = Point(.8f); var direction = (Point(.81f) - Point(.79f)).normalized;
            var side = Vector3.Cross(direction, Mathf.Abs(direction.y) > .9f ? Vector3.forward : Vector3.up).normalized * .035f;
            arrowPoints[0] = transform.InverseTransformPoint(tip - direction * .07f + side);
            arrowPoints[1] = transform.InverseTransformPoint(tip);
            arrowPoints[2] = transform.InverseTransformPoint(tip - direction * .07f - side);
            arrow.SetPositions(arrowPoints);
            label.position = Point(.5f) + Vector3.up * .10f;
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
            } else { actionText.text=visible?"SIMULACIÓN · "+meaning:meaning; actionText.color=IsObservation?LabVisuals.Muted:LabVisuals.Cyan; }
        }
    }
}
