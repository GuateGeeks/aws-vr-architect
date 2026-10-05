using System.Collections.Generic;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // The GuateGeeks eyes mark as a live 3D object: geometry from Branding/eyes.svg (68.85 × 37.95 units),
    // built from flat discs so it stays faithful to the logo. Pupils follow the viewer (or a target), the eyes
    // blink now and then and make small saccades. Front faces -Z. Reduced motion keeps tracking but stops blinking.
    public sealed class GeekEyes : MonoBehaviour
    {
        const float SvgWidth = 68.85f, SvgHeight = 37.95f, PupilTravel = 7f;
        sealed class Eye { public Transform content, pupil; public Vector2 rest; public Vector3 sclera; }
        readonly List<Eye> eyes = new List<Eye>();
        public Transform Target;
        float unit, nextBlink, blinkStart = -1, nextSaccade; Vector2 saccade;
        public static Color Ink = new Color(.02f, .025f, .03f), Sclera = Color.white;

        // width: total width in the parent's local units.
        public static GeekEyes Create(Transform parent, float width, string name = "GuateGeeks eyes")
        {
            var root = new GameObject(name).transform; root.SetParent(parent, false);
            var eyesMark = root.gameObject.AddComponent<GeekEyes>(); eyesMark.unit = width / SvgWidth;
            eyesMark.Build(); eyesMark.nextBlink = Time.unscaledTime + Random.Range(1.5f, 4f);
            return eyesMark;
        }
        Vector3 P(float x, float y, float layer) => new Vector3((x - SvgWidth / 2) * unit, (SvgHeight / 2 - y) * unit, -layer * unit);
        Transform Disc(Transform parent, string name, Vector3 local, float radius, Color color, float thickness = .35f)
        {
            var disc = LabVisuals.Shape(parent, name, PrimitiveType.Cylinder, local, new Vector3(radius * 2 * unit, thickness * unit * .5f, radius * 2 * unit), color).transform;
            disc.localRotation = Quaternion.Euler(90, 0, 0); return disc;
        }
        void Build()
        {
            // Bridge of the glasses: a dark arc between the eyes (drawn first, behind the rims).
            var bridge = new List<Vector3>();
            HoloGeometry.Path(bridge, P(27.4f, 16.2f, 0), P(29.8f, 13.6f, 0), P(33.4f, 12.2f, 0), P(36.9f, 12.4f, 0), P(39.7f, 13.7f, 0));
            HoloGeometry.Strokes(transform, "Glasses bridge", bridge, LabVisuals.Hex("#2A3138"), .9f * unit);
            AddEye(new Vector2(15f, 22.95f), new Vector2(15.8f, 23.19f), new Vector2(18.81f, 21.87f));
            AddEye(new Vector2(53.86f, 15f), new Vector2(53.22f, 15.54f), new Vector2(49.8f, 15.53f));
        }
        void AddEye(Vector2 rim, Vector2 sclera, Vector2 pupil)
        {
            Disc(transform, "Eye rim", P(rim.x, rim.y, .2f), 14.99f, Ink);
            var content = new GameObject("Eye content").transform; content.SetParent(transform, false); content.localPosition = P(sclera.x, sclera.y, 0);
            Disc(content, "Sclera", new Vector3(0, 0, -.6f * unit), 13.47f, Sclera);
            var pupilRoot = new GameObject("Pupil").transform; pupilRoot.SetParent(content, false);
            Disc(pupilRoot, "Iris", new Vector3(0, 0, -1.0f * unit), 4.91f, Ink);
            Disc(pupilRoot, "Highlight", new Vector3(1.28f * unit, 1.5f * unit, -1.3f * unit), 1.44f, Sclera, .2f);
            Disc(pupilRoot, "Glint", new Vector3(-.41f * unit, 2.77f * unit, -1.3f * unit), .43f, Sclera, .2f);
            var rest = new Vector2(pupil.x - sclera.x, sclera.y - pupil.y);
            pupilRoot.localPosition = new Vector3(rest.x * unit, rest.y * unit, 0);
            eyes.Add(new Eye { content = content, pupil = pupilRoot, rest = rest, sclera = content.localPosition });
        }
        void LateUpdate()
        {
            var target = Target ? Target : Camera.main ? Camera.main.transform : null;
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            bool reduced = LabFeedback.Current && LabFeedback.Current.ReducedMotion;
            if (now > nextSaccade) { saccade = Random.insideUnitCircle * 1.2f; nextSaccade = now + Random.Range(.6f, 2.2f); }
            foreach (var eye in eyes)
            {
                Vector2 look = eye.rest;
                if (target)
                {
                    var local = transform.InverseTransformPoint(target.position) - eye.sclera;
                    // Toward the viewer is -Z; pupils move toward the target's projected direction.
                    var dir = new Vector2(local.x, local.y) / Mathf.Max(.0001f, Mathf.Abs(local.z) + local.magnitude * .35f);
                    look = Vector2.ClampMagnitude(dir * PupilTravel * 1.4f + (reduced ? Vector2.zero : saccade), PupilTravel);
                }
                var goal = new Vector3(look.x * unit, look.y * unit, 0);
                eye.pupil.localPosition = Vector3.Lerp(eye.pupil.localPosition, goal, 1 - Mathf.Exp(-12 * dt));
            }
            float open = 1;
            if (!reduced)
            {
                if (blinkStart < 0 && now > nextBlink) blinkStart = now;
                if (blinkStart >= 0)
                {
                    float b = (now - blinkStart) / .16f;
                    open = b < 1 ? Mathf.Lerp(1, .08f, Mathf.Sin(b * Mathf.PI)) : 1;
                    if (b >= 1) { blinkStart = -1; nextBlink = now + Random.Range(2.5f, 6.5f); }
                }
            }
            foreach (var eye in eyes) eye.content.localScale = new Vector3(1, open, 1);
        }
    }
}
