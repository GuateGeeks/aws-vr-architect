using System.Collections.Generic;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // Reading surfaces (Lambda code, inspection readers, live evidence) ride with the person. A shared body
    // heading follows the head lazily: glancing around the table, leaning in to read, or reading the far edge
    // of a panel never moves anything; a real turn or a step makes every follow panel catch up smoothly.
    // Each panel prefers a slot (code in the middle, inspection to the right, evidence to the left) and slots
    // push apart so open panels never overlap. Dragging a panel by its grip bar pins it where you leave it;
    // «Traer aquí» on the wrist (or «Restaurar paneles») releases it again.
    public sealed class HeadFollow : MonoBehaviour
    {
        public const float XrDistance = .7f, DesktopDistance = 1f, DesktopScale = 1.3f;
        public const float YawDeadzone = 38, StepDeadzone = .25f, HeightDeadzone = .18f, Gap = 2.5f;
        static readonly List<HeadFollow> all = new List<HeadFollow>();
        static readonly List<HeadFollow> active = new List<HeadFollow>();
        static readonly List<HeadFollow> order = new List<HeadFollow>();
        static readonly List<Vector2> placed = new List<Vector2>();
        static float bodyYaw, bodyHeight;
        static Vector3 bodyPosition;
        static bool anchored, turning, stepping, rising, dirty = true;
        static int anchorFrame = -1, resolveFrame = -1;
        public static IReadOnlyList<HeadFollow> Active => active;
        public static float BodyYaw => bodyYaw;

        public float PreferredYaw;
        public float Pitch = 7; // degrees below the eye line
        public int Priority;
        public float Scale = .0004f;
        public bool Pinned { get; private set; }
        public float ResolvedYaw { get; private set; }
        public float HalfAngle { get; private set; }
        ArchitectureLab lab;
        LabMenu menu;
        RectTransform rect;
        bool snapped;

        public static HeadFollow Attach(RectTransform panel, ArchitectureLab owner, float preferredYaw, int priority, float scale)
        {
            var follow = panel.gameObject.AddComponent<HeadFollow>();
            follow.lab = owner; follow.rect = panel; follow.PreferredYaw = preferredYaw; follow.ResolvedYaw = preferredYaw;
            follow.Priority = priority; follow.Scale = scale;
            follow.menu = panel.GetComponent<LabMenu>();
            if (follow.menu)
            {
                follow.menu.Persist = false;
                follow.menu.Moved += () => follow.Pinned = true;
                follow.menu.Restored += () => follow.Unpin();
            }
            follow.ApplyScale(); follow.Snap();
            return follow;
        }
        // Forget the body heading: the next frame anchors it to where the head looks now, and all panels re-follow.
        public static void Recenter()
        {
            anchored = false; turning = stepping = rising = false;
            foreach (var f in all) if (f) { f.Pinned = false; f.snapped = false; }
            dirty = true;
        }
        public void Unpin() { Pinned = false; snapped = false; dirty = true; }

        void Awake() { if (!all.Contains(this)) all.Add(this); }
        void OnDestroy() { all.Remove(this); active.Remove(this); }
        void OnEnable()
        {
            // A panel opened while nothing else follows appears where the person is looking now.
            if (active.Count == 0) anchored = false;
            if (!active.Contains(this)) active.Add(this);
            snapped = false; dirty = true;
        }
        void OnDisable() { active.Remove(this); dirty = true; }

        Transform Head => lab && lab.Rig && lab.Rig.ViewCamera ? lab.Rig.ViewCamera.transform : Camera.main ? Camera.main.transform : null;
        bool Xr => lab && lab.Rig && lab.Rig.IsXR;
        float Distance => Xr ? XrDistance : DesktopDistance;
        float EffectiveScale => Xr ? Scale : Scale * DesktopScale;
        static bool Reduced => LabFeedback.Current && LabFeedback.Current.ReducedMotion;

        void ApplyScale()
        {
            if (!rect) rect = transform as RectTransform;
            float parent = transform.parent ? Mathf.Abs(transform.parent.lossyScale.x) : 1;
            var scale = Vector3.one * EffectiveScale / Mathf.Max(1e-4f, parent);
            if (transform.localScale != scale) transform.localScale = scale;
            HalfAngle = Mathf.Atan2(rect.rect.width * EffectiveScale * .5f, Distance) * Mathf.Rad2Deg;
        }
        void Snap() { var head = Head; if (!head) return; UpdateAnchor(head); ResolveIfNeeded(); Pose(out var p, out var r); transform.SetPositionAndRotation(p, r); snapped = true; }

        void LateUpdate()
        {
            var head = Head; if (!head) return;
            if (menu && menu.Grabbed) { if (!Pinned) dirty = true; Pinned = true; return; }
            if (Pinned) return;
            ApplyScale(); UpdateAnchor(head); ResolveIfNeeded();
            Pose(out var position, out var rotation);
            if (!snapped || Reduced) { transform.SetPositionAndRotation(position, rotation); snapped = true; return; }
            float k = 1 - Mathf.Exp(-10 * Time.unscaledDeltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, position, k), Quaternion.Slerp(transform.rotation, rotation, k));
        }
        void Pose(out Vector3 position, out Quaternion rotation)
        {
            var direction = Quaternion.Euler(Pitch, bodyYaw + ResolvedYaw, 0) * Vector3.forward;
            position = new Vector3(bodyPosition.x, bodyHeight, bodyPosition.z) + direction * Distance;
            // Perpendicular to the line of sight: the panel's front faces the eyes.
            rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        // Once per frame: update the shared body heading, then resolve the slots of every following panel.
        void UpdateAnchor(Transform head)
        {
            if (anchorFrame == Time.frameCount && anchored) return;
            anchorFrame = Time.frameCount;
            float headYaw = head.eulerAngles.y; var eye = head.position;
            if (!anchored) { bodyYaw = headYaw; bodyPosition = eye; bodyHeight = eye.y; anchored = true; turning = stepping = rising = false; }
            ResolveIfNeeded();
            float dt = Time.unscaledDeltaTime, k = Reduced ? 1 : 1 - Mathf.Exp(-5 * dt);
            float delta = Mathf.DeltaAngle(bodyYaw, headYaw);
            // Reading a panel (even near its far edge) never starts a catch-up.
            bool reading = false;
            foreach (var f in active) if (f && !f.Pinned && Mathf.Abs(Mathf.DeltaAngle(headYaw, bodyYaw + f.ResolvedYaw)) < f.HalfAngle + 3) reading = true;
            if (!turning && !reading && Mathf.Abs(delta) > YawDeadzone) turning = true;
            if (turning) { bodyYaw += delta * k; if (Mathf.Abs(Mathf.DeltaAngle(bodyYaw, headYaw)) < 2) turning = false; }
            var flat = eye - bodyPosition; flat.y = 0;
            if (!stepping && flat.magnitude > StepDeadzone) stepping = true;
            if (stepping) { bodyPosition = Vector3.Lerp(bodyPosition, eye, k); var rest = eye - bodyPosition; rest.y = 0; if (rest.magnitude < .02f) stepping = false; }
            if (!rising && Mathf.Abs(eye.y - bodyHeight) > HeightDeadzone) rising = true;
            if (rising) { bodyHeight = Mathf.Lerp(bodyHeight, eye.y, k); if (Mathf.Abs(eye.y - bodyHeight) < .02f) rising = false; }
        }
        // Slots are resolved once per frame, and again at once when a panel opens, closes or is pinned that frame,
        // so a panel never appears in a slot another one already holds.
        static void ResolveIfNeeded()
        {
            if (!dirty && resolveFrame == Time.frameCount) return;
            resolveFrame = Time.frameCount; dirty = false; Resolve();
        }
        // Panels keep their preferred direction unless that overlaps a higher-priority panel; then they move
        // outward (away from the panel they collide with) just far enough to leave a small gap.
        static void Resolve()
        {
            order.Clear();
            foreach (var f in active) if (f && !f.Pinned) { f.ApplyScale(); order.Add(f); }
            order.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            placed.Clear();
            foreach (var f in order)
            {
                float c = f.PreferredYaw, h = f.HalfAngle;
                for (int pass = 0; pass < 4; pass++)
                {
                    bool moved = false;
                    foreach (var q in placed)
                    {
                        float need = q.y + h + Gap;
                        if (Mathf.Abs(c - q.x) >= need) continue;
                        float side = f.PreferredYaw > q.x ? 1 : f.PreferredYaw < q.x ? -1 : (c >= q.x ? 1 : -1);
                        c = q.x + side * need; moved = true;
                    }
                    if (!moved) break;
                }
                f.ResolvedYaw = c; placed.Add(new Vector2(c, h));
            }
        }
    }
}
