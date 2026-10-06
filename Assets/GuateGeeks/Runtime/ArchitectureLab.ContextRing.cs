using System;
using System.Collections.Generic;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    // Object-first editing: selecting a hologram blooms a radial ring of actions around the object itself,
    // so the common next steps happen where the user is looking instead of on a panel two metres away.
    // The inspector keeps the complete definition; every ring action calls the same undoable lab method.
    public sealed partial class ArchitectureLab
    {
        sealed class ContextAction { public LabTarget Target; public Func<NodeView, bool> Show; public float Angle; }
        const float ContextRadius = 400;
        RectTransform contextRing;
        Transform contextSpin, contextCounterSpin;
        readonly List<ContextAction> contextActions = new List<ContextAction>();
        string contextLayout;
        float contextOpenedAt;
        public bool ContextRingVisible => contextRing && contextRing.gameObject.activeSelf;

        void BuildContextRing()
        {
            contextRing = Panel(world, "Object context ring", Vector3.zero, new Vector2(1500, 1500), background: false, movable: false);
            contextRing.localScale = Vector3.one * .001f;
            contextRing.GetComponent<Canvas>().sortingOrder = ContextSortingOrder;
            var halo = new List<Vector3>();
            HoloGeometry.Circle(halo, Vector3.zero, ContextRadius, 96, true);
            for (int i = 0; i < 72; i++)
            {
                float a = i * 5 * Mathf.Deg2Rad; var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                HoloGeometry.Path(halo, d * (ContextRadius - (i % 6 == 0 ? 26 : 12)), d * (ContextRadius - 4));
            }
            HoloGeometry.Strokes(contextRing, "Context reticle", halo, Color.Lerp(Hex("#0B2633"), Cyan, .55f), 1.6f);
            contextSpin = new GameObject("Context orbit").transform; contextSpin.SetParent(contextRing, false);
            var arcs = new List<Vector3>();
            for (int i = 0; i < 3; i++) HoloGeometry.Circle(arcs, Vector3.zero, ContextRadius + 34, 24, true, i * 120 + 10, 62);
            HoloGeometry.Strokes(contextSpin, "Context orbit arcs", arcs, Cyan, 2.6f);
            contextCounterSpin = new GameObject("Context counter orbit").transform; contextCounterSpin.SetParent(contextRing, false);
            var counter = new List<Vector3>();
            for (int i = 0; i < 4; i++) HoloGeometry.Circle(counter, Vector3.zero, ContextRadius - 44, 12, true, i * 90 + 30, 28);
            HoloGeometry.Strokes(contextCounterSpin, "Context counter arcs", counter, Orange, 2f);

            // Slots avoid the name label above the object and the ENTRADA/SALIDA ports on its sides.
            // Outputs leave to the right, so CONECTAR sits on the right; destructive QUITAR sits low and apart.
            AddContextAction("CONECTAR", 15, v => v.Model.kind != ServiceKind.CloudWatch, () => StartConnectionFrom(selected));
            AddContextAction("RELACIONES", 165, v => true, () => ShowRelations(0));
            AddContextAction("FICHA", 130, v => true, ShowDefinitionHelp);
            AddContextAction("CÓDIGO", 50, v => v.Model.kind == ServiceKind.Lambda, () => { if (selected) OpenLambdaEditor(selected.Model.id); });
            AddContextAction("DIAGNÓSTICO", 215, v => IsCloud, OpenSelectedDiagnostics);
            AddContextAction("QUITAR", -35, v => true, RemoveSelectedComponent, Orange);
            contextRing.gameObject.SetActive(false);
        }
        void AddContextAction(string label, float angle, Func<NodeView, bool> show, Action action, Color? accent = null)
        {
            var target = EditButton(contextRing, label, Vector2.zero, new Vector2(244, 68), action, accent);
            target.Label.fontSize = 30; Track(target.Label, label, new Vector2(224, 64), 4);
            // A short leader ties each action to the reticle, like a callout on an instrument.
            var tint = accent ?? Cyan; tint.a = .6f;
            var leader = Block(target.transform, Vector2.zero, new Vector2(2, 22), tint);
            target.gameObject.AddComponent<ContextLeader>().Leader = leader.rectTransform;
            contextActions.Add(new ContextAction { Target = target, Show = show, Angle = angle });
        }
        public void StartConnectionFrom(NodeView view)
        {
            if (!view || Busy || Placing || EditingText || ConfiguringConnection) return;
            if (view.Model.kind == ServiceKind.CloudWatch) { SetStatus("CloudWatch solo recibe asociaciones: elige otro origen."); Feedback.Play(LabFeedback.Error); return; }
            ConnectingMode = true; connectionSource = view.Model.id; selected = view;
            RefreshSelection(); UpdateButtons();
            SetStatus("Origen: " + view.Model.name + ". Selecciona el destino. B / Esc para salir.");
        }
        bool ContextRingAllowed => selected && !selected.Grabbed && !ConnectingMode && !Placing && !EditingText && !ConfiguringConnection
            && !Busy && !codeBusy && !voicePreviewActive && !confirming && !resetting && world && world.gameObject.activeInHierarchy;
        void TickContextRing()
        {
            if (!contextRing) return;
            bool show = ContextRingAllowed;
            if (contextRing.gameObject.activeSelf != show) contextRing.gameObject.SetActive(show);
            if (!show) { contextLayout = null; return; }
            string layout = selected.Model.id + "|" + selected.Model.kind + "|" + IsCloud;
            if (layout != contextLayout)
            {
                contextLayout = layout; contextOpenedAt = Time.unscaledTime;
                foreach (var action in contextActions)
                {
                    bool visible = action.Show(selected);
                    action.Target.gameObject.SetActive(visible);
                    float a = action.Angle * Mathf.Deg2Rad;
                    var rect = (RectTransform)action.Target.transform;
                    rect.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (ContextRadius + 44);
                    action.Target.GetComponent<ContextLeader>().Aim();
                }
                UpdateButtons();
                Feedback.Play(LabFeedback.Open);
            }
            var cam = Rig && Rig.ViewCamera ? Rig.ViewCamera : Camera.main;
            if (!cam) return;
            Vector3 center = selected.transform.position;
            var toward = cam.transform.position - center; toward.y = 0;
            if (toward.sqrMagnitude < .0001f) toward = Vector3.back;
            toward.Normalize();
            // The hologram's size in the room (component size × table size); the actions keep their visual angle,
            // so on a smaller table they shrink with the viewing distance rather than with the hologram.
            float scale = selected.transform.lossyScale.x / Mathf.Max(.0001f, world.lossyScale.x), ui = scale * Table.LabelScale;
            bool reduced = LabFeedback.Current && LabFeedback.Current.ReducedMotion;
            float t = reduced ? 1 : Mathf.Clamp01((Time.unscaledTime - contextOpenedAt) / .24f);
            float bloom = 1 + 2.2f * Mathf.Pow(t - 1, 3) + 1.2f * Mathf.Pow(t - 1, 2); // ease-out with a small overshoot
            contextRing.position = center + toward * .3f * scale; // in front of the projector plinth
            contextRing.rotation = Quaternion.LookRotation(-toward, Vector3.up);
            contextRing.localScale = Vector3.one * .001f * ui * Mathf.Lerp(.72f, 1, bloom);
            contextSpin.localRotation = Quaternion.Euler(0, 0, LabFeedback.Clock * 14);
            contextCounterSpin.localRotation = Quaternion.Euler(0, 0, -LabFeedback.Clock * 22);
        }
    }

    // Points each action's leader line back at the reticle centre, wherever the slot sits.
    public sealed class ContextLeader : MonoBehaviour
    {
        public RectTransform Leader;
        void OnEnable() => Aim();
        public void Aim()
        {
            if (!Leader) return;
            var rect = (RectTransform)transform; Vector2 inward = -rect.anchoredPosition.normalized;
            if (inward.sqrMagnitude < .5f) return;
            float edge = Mathf.Abs(inward.x) * rect.sizeDelta.x / 2 > Mathf.Abs(inward.y) * rect.sizeDelta.y / 2
                ? rect.sizeDelta.x / 2 / Mathf.Max(.001f, Mathf.Abs(inward.x)) : rect.sizeDelta.y / 2 / Mathf.Max(.001f, Mathf.Abs(inward.y));
            Leader.anchoredPosition = inward * (edge + 12);
            Leader.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(inward.y, inward.x) * Mathf.Rad2Deg + 90);
        }
    }
}
