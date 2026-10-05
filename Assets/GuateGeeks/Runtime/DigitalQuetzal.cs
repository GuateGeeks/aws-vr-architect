using System.Collections.Generic;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // A digital quetzal: holographic body, flapping wings with luminous feather lines and two long tail streamers
    // that trail the flight path. It swoops through the lab over the table and flies off into the volcano horizon,
    // shrinking and dissolving with distance, then returns from the other side. Hidden with reduced motion.
    public sealed class DigitalQuetzal : MonoBehaviour
    {
        static readonly Color Emerald = LabVisuals.Hex("#1FBF74"), Crest = LabVisuals.Hex("#5DB54B"), Chest = LabVisuals.Hex("#E0303F"),
            Beak = LabVisuals.Hex("#C6E030"), Tail = LabVisuals.Hex("#0E8A8C");
        // Lab-space waypoints: enters high on one side, crosses above the projection table and the logo, then heads into the horizon.
        static readonly Vector3[] Route = {
            new Vector3(-7.2f, 4.9f, -.6f), new Vector3(-4.6f, 3.9f, .9f), new Vector3(-1.8f, 3.6f, 2.3f), new Vector3(.9f, 3.3f, 3.2f),
            new Vector3(2.9f, 3.9f, 4.9f), new Vector3(3.2f, 4.9f, 6.9f), new Vector3(1.6f, 5.5f, 9.1f), new Vector3(-.4f, 5.9f, 10.1f) };
        const float FlightSeconds = 14, RestSeconds = 16, SampleSpacing = .022f;
        Transform bird, leftWing, rightWing;
        LineRenderer[] tails, tailCoverts;
        readonly List<Vector3> history = new List<Vector3>();
        float clock, lastSample; int pass; bool flying;
        public bool Flying => flying;
        public Transform Bird => bird;
        // Deterministic pose for validation renders: jump to a point of the first route.
        public void Preview(float progress) { clock = Mathf.Clamp01(progress) * FlightSeconds; pass = 0; }
        public static DigitalQuetzal Create(Transform world)
        {
            var root = new GameObject("Digital quetzal").transform; root.SetParent(world, false);
            var quetzal = root.gameObject.AddComponent<DigitalQuetzal>(); quetzal.Build(); quetzal.clock = FlightSeconds + RestSeconds - 4; // first pass soon after start
            return quetzal;
        }
        static Material Holo(Color c, float alpha = .9f) { c.a = alpha; return LabVisuals.SpecialMaterial("LabHologram", c); }
        GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color, float alpha = .9f)
        {
            var go = LabVisuals.Shape(parent, name, type, position, scale, color);
            go.GetComponent<Renderer>().sharedMaterial = Holo(color, alpha); return go;
        }
        void Build()
        {
            bird = new GameObject("Quetzal body").transform; bird.SetParent(transform, false);
            // Body proportions after the resplendent quetzal: compact emerald body, red belly, crested head, yellow bill.
            Part(bird, "Body", PrimitiveType.Sphere, Vector3.zero, new Vector3(.17f, .16f, .42f), Emerald);
            Part(bird, "Red belly", PrimitiveType.Sphere, new Vector3(0, -.05f, .07f), new Vector3(.13f, .1f, .24f), Chest, .95f);
            Part(bird, "Head", PrimitiveType.Sphere, new Vector3(0, .07f, .25f), Vector3.one * .15f, Emerald);
            var crest = Part(bird, "Crest", PrimitiveType.Sphere, new Vector3(0, .15f, .22f), new Vector3(.05f, .1f, .16f), Crest);
            crest.transform.localRotation = Quaternion.Euler(-25, 0, 0);
            LabVisuals.Shape(bird, "Bill", PrimitiveType.Cube, new Vector3(0, .055f, .345f), new Vector3(.03f, .03f, .06f), Beak);
            for (int s = -1; s <= 1; s += 2) LabVisuals.Shape(bird, "Eye", PrimitiveType.Sphere, new Vector3(s * .062f, .09f, .29f), Vector3.one * .024f, LabVisuals.Ice);
            // Fan-shaped crest and a pale undertail, as on the resplendent quetzal.
            var fan = Part(bird, "Crest fan", PrimitiveType.Sphere, new Vector3(0, .19f, .2f), new Vector3(.035f, .08f, .13f), Crest, .85f);
            fan.transform.localRotation = Quaternion.Euler(-10, 0, 0);
            // Long tail coverts: body-local streamers that ripple behind the bird, the quetzal's signature.
            tailCoverts = new LineRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                var covert = LabVisuals.Line(bird, i == 2 ? "Undertail" : "Tail covert", new Vector3[16], i == 2 ? LabVisuals.Ice : Emerald, .05f);
                covert.sharedMaterial = LabVisuals.Beam(i == 2 ? LabVisuals.Hex("#BFE8D8") : i == 0 ? Emerald : Crest);
                covert.textureMode = LineTextureMode.Stretch; covert.numCapVertices = 0;
                covert.widthCurve = i == 2 ? new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, .2f))
                    : new AnimationCurve(new Keyframe(0, 1), new Keyframe(.45f, .55f), new Keyframe(.85f, .8f), new Keyframe(1, 0));
                tailCoverts[i] = covert;
            }
            leftWing = Wing("Left wing", -1); rightWing = Wing("Right wing", 1);
            tails = new LineRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                var tail = LabVisuals.Line(transform, "Flight trail", new Vector3[2], Tail, .02f);
                tail.useWorldSpace = true; tail.sharedMaterial = LabVisuals.Beam(i == 0 ? Tail : Emerald); tail.textureMode = LineTextureMode.Stretch; tail.numCapVertices = 0;
                tail.widthCurve = new AnimationCurve(new Keyframe(0, .9f), new Keyframe(.55f, .45f), new Keyframe(.88f, .75f), new Keyframe(1, 0));
                tails[i] = tail;
            }
            gameObject.SetActive(true); SetVisible(false);
        }
        Transform Wing(string name, int side)
        {
            var pivot = new GameObject(name).transform; pivot.SetParent(bird, false); pivot.localPosition = new Vector3(side * .07f, .05f, .03f);
            // Feathered outline (outward along +x), double-sided so both faces show while flapping.
            var outline = new[] { new Vector2(0, .12f), new Vector2(.22f, .15f), new Vector2(.48f, .09f), new Vector2(.66f, -.04f),
                new Vector2(.58f, -.1f), new Vector2(.46f, -.06f), new Vector2(.4f, -.14f), new Vector2(.3f, -.09f), new Vector2(.22f, -.17f), new Vector2(.12f, -.1f), new Vector2(0, -.12f) };
            var vertices = new List<Vector3> { Vector3.zero }; var triangles = new List<int>();
            foreach (var p in outline) vertices.Add(new Vector3(p.x * side, 0, p.y));
            for (int i = 1; i < outline.Length; i++) { triangles.Add(0); triangles.Add(i); triangles.Add(i + 1); triangles.Add(0); triangles.Add(i + 1); triangles.Add(i); }
            var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            ownedMeshes.Add(mesh);
            var go = new GameObject(name + " membrane", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(pivot, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = Holo(Emerald, .75f);
            // Flight-feather lines run from mid-wing to each trailing-edge point.
            var feathers = new List<Vector3>();
            for (int i = 3; i < outline.Length - 1; i++) HoloGeometry.Path(feathers, new Vector3(outline[i].x * .45f * side, 0, .02f), new Vector3(outline[i].x * side, 0, outline[i].y));
            HoloGeometry.Path(feathers, new Vector3(0, 0, .12f), new Vector3(.22f * side, 0, .15f), new Vector3(.48f * side, 0, .09f), new Vector3(.66f * side, 0, -.04f));
            HoloGeometry.Strokes(pivot, name + " feathers", feathers, LabVisuals.Hex("#8FF0B8"), .0022f);
            return pivot;
        }
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        void OnDestroy() { foreach (var m in ownedMeshes) if (m) Destroy(m); }
        void SetVisible(bool value) { bird.gameObject.SetActive(value); foreach (var t in tails) t.enabled = value; }
        void AnimateTail(float size)
        {
            for (int k = 0; k < tailCoverts.Length; k++)
            {
                var covert = tailCoverts[k]; int n = covert.positionCount; float length = k == 2 ? .26f : 1.35f;
                for (int i = 0; i < n; i++)
                {
                    float u = i / (n - 1f);
                    float sway = Mathf.Sin(clock * 5.5f - u * 4 + k * .9f) * .09f * u * u;
                    float spread = (k == 0 ? -1 : k == 1 ? 1 : 0) * .035f * u;
                    covert.SetPosition(i, new Vector3(spread + sway * .4f, -.03f - u * .14f + sway, -.18f - u * length));
                }
                covert.widthMultiplier = (k == 2 ? .07f : .055f) * size;
            }
        }
        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return .5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
        }
        Vector3 RoutePoint(float u, bool mirror)
        {
            float f = Mathf.Clamp01(u) * (Route.Length - 1); int i = Mathf.Min((int)f, Route.Length - 2); float t = f - i;
            Vector3 Get(int k) { var p = Route[Mathf.Clamp(k, 0, Route.Length - 1)]; return mirror ? new Vector3(-p.x, p.y, p.z) : p; }
            return CatmullRom(Get(i - 1), Get(i), Get(i + 1), Get(i + 2), t);
        }
        void Update()
        {
            bool reduced = LabFeedback.Current && LabFeedback.Current.ReducedMotion;
            if (reduced) { if (flying) { flying = false; SetVisible(false); } return; }
            clock += Time.unscaledDeltaTime;
            float cycle = FlightSeconds + RestSeconds;
            if (clock >= cycle) { clock -= cycle; pass++; }
            bool shouldFly = clock < FlightSeconds;
            if (shouldFly != flying) { flying = shouldFly; SetVisible(flying); history.Clear(); }
            if (!flying) return;
            float u = clock / FlightSeconds; bool mirror = pass % 2 == 1;
            // Ease the start; keep speed up through the room and let distance do the slowing near the horizon.
            float s = u * u * (3 - 2 * u) * .35f + u * .65f;
            var position = RoutePoint(s, mirror);
            var ahead = RoutePoint(Mathf.Min(1, s + .01f), mirror);
            var forward = (ahead - position).sqrMagnitude > 1e-6f ? (ahead - position).normalized : transform.forward;
            var worldPos = transform.parent.TransformPoint(position);
            // Bank into turns, pitch with climbs.
            var behind = RoutePoint(Mathf.Max(0, s - .01f), mirror);
            float turn = Vector3.Cross((position - behind).normalized, forward).y;
            bird.position = worldPos;
            bird.rotation = transform.parent.rotation * Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0, 0, Mathf.Clamp(-turn * 900, -50, 50));
            // Distance shrink: the bird recedes into the horizon over the last third of the route.
            float far = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.62f, 1, u));
            float size = Mathf.Lerp(1.6f, .2f, far) * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .04f, u)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.95f, 1, u)));
            bird.localScale = Vector3.one * size;
            // Flap fast while climbing, glide between bursts.
            float glide = .5f + .5f * Mathf.Sin(clock * .9f);
            float flap = Mathf.Sin(clock * Mathf.Lerp(16, 9, glide)) * Mathf.Lerp(42, 14, glide) + 8;
            leftWing.localRotation = Quaternion.Euler(0, 0, -flap); rightWing.localRotation = Quaternion.Euler(0, 0, flap);
            AnimateTail(size);
            // Tail streamers: sampled flight history, offset to each side and rippling.
            if (history.Count == 0 || Time.unscaledTime - lastSample > SampleSpacing) { history.Insert(0, bird.TransformPoint(new Vector3(0, -.01f, -.2f))); lastSample = Time.unscaledTime; }
            if (history.Count > 28) history.RemoveAt(history.Count - 1);
            for (int k = 0; k < tails.Length; k++)
            {
                int count = Mathf.Max(2, history.Count);
                tails[k].positionCount = count;
                var side = bird.right * (k == 0 ? -.025f : .025f) * size;
                for (int i = 0; i < count; i++)
                {
                    var p = history[Mathf.Min(i, history.Count - 1)];
                    float wave = Mathf.Sin(clock * 6 - i * .45f + k) * .02f * i / count;
                    tails[k].SetPosition(i, p + side * (1 + i * .15f) + bird.up * wave * size);
                }
                tails[k].widthMultiplier = .018f * size;
            }
        }
    }
}
