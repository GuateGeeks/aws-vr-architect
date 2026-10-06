using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // The quetzal (QuetzalMesh, LabQuetzal shader) and how it moves, after reference footage of the resplendent quetzal:
    // - undulating flight: bursts of deep wingbeats with a slight rise, then short bounds with the wings tucked and a dip;
    // - wingbeats: wings fully spread on the downstroke, wrist folded and swept back on the upstroke;
    // - hover: body nearly upright, wings beating over the back in a figure of eight, tail hanging, as quetzals hover to
    //   pluck fruit; the bird flares into it and pitches forward out of it;
    // - head: stabilised in world space (birds hold their head still while the body moves) and turned toward the viewer;
    // - streamers: four chains simulated with Verlet integration, so they trail behind in flight, hang in S-curves in the
    //   hover and carry the waves that each wingbeat sends down them.
    // Each pass enters between two ribs, swoops past the left of station 1 and over the far side of the table, hovers over
    // the table in three-quarter profile, then rises over the reactor's rim and recedes into the horizon. Passes alternate
    // mirrored. Decorative; hidden with reduced motion.
    public sealed class DigitalQuetzal : MonoBehaviour
    {
        // Lab space (table centre at z 2.65). The hover is above the near half of the table, ~2.2 m from station 1.
        public static readonly Vector3 HoverPoint = new Vector3(0, 2.72f, 2.05f);
        static readonly Vector3[] RouteIn = {
            new Vector3(-7.15f, 4.0f, -2.13f), new Vector3(-5.65f, 3.8f, -1.13f), new Vector3(-3.4f, 3.5f, 1.0f),
            new Vector3(-2.2f, 3.2f, 3.9f), new Vector3(-.7f, 2.9f, 3.2f), HoverPoint };
        static readonly Vector3[] RouteOut = {
            HoverPoint, new Vector3(.9f, 3.0f, 3.2f), new Vector3(2.4f, 3.7f, 5.6f), new Vector3(1.9f, 4.3f, 8.0f),
            new Vector3(1.33f, 4.7f, 9.32f), new Vector3(1.68f, 4.75f, 11.08f) };
        const float InSeconds = 6.5f, HoverSeconds = 5f, OutSeconds = 8, RestSeconds = 11, NearSize = 1.45f, BoundCycle = 1.25f;
        public const float FlightSeconds = InSeconds + HoverSeconds + OutSeconds;

        sealed class Streamer { public Mesh mesh; public Transform view; public Vector3 root, lastRoot; public float length, phase; public Vector3[] points, previous, vertices; }
        Transform bird, head, leftArm, rightArm, leftHand, rightHand;
        Streamer[] streamers;
        Material plumage;
        readonly System.Collections.Generic.List<Object> owned = new System.Collections.Generic.List<Object>();
        float clock, wingPhase, hoverBlend, tuck, nextGlance; int pass; bool flying, resetStreamers = true;
        Vector3 lastForward = Vector3.forward, glance, lastPosition, lastRest = Vector3.back; float lastStep = 1 / 90f;
        public bool Flying => flying;
        public bool Hovering => flying && clock >= InSeconds && clock < InSeconds + HoverSeconds;
        public Transform Bird => bird;
        public Transform Head => head;
        // Deterministic poses for validation renders.
        public void Preview(float progress) { clock = Mathf.Clamp01(progress) * FlightSeconds; pass = 0; resetStreamers = true; }
        public void PreviewHover() { clock = InSeconds + HoverSeconds * .5f; pass = 0; resetStreamers = true; }
        public static DigitalQuetzal Create(Transform world)
        {
            var root = new GameObject("Digital quetzal").transform; root.SetParent(world, false);
            var quetzal = root.gameObject.AddComponent<DigitalQuetzal>(); quetzal.Build(); quetzal.clock = FlightSeconds + RestSeconds - 4; // first pass soon after start
            return quetzal;
        }
        Transform Part(Transform parent, string name, Mesh mesh, Vector3 position)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false); go.transform.localPosition = position;
            go.GetComponent<MeshFilter>().sharedMesh = mesh; owned.Add(mesh);
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = plumage;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            return go.transform;
        }
        void Build()
        {
            plumage = new Material(Resources.Load<Shader>("LabQuetzal")) { name = "Quetzal plumage" }; owned.Add(plumage);
            bird = new GameObject("Quetzal body").transform; bird.SetParent(transform, false);
            Part(bird, "Body and tail", QuetzalMesh.Body(), Vector3.zero);
            head = new GameObject("Head").transform; head.SetParent(bird, false); head.localPosition = QuetzalMesh.NeckPivot;
            Part(head, "Head, crest and bill", QuetzalMesh.Head(), Vector3.zero);
            var shoulder = QuetzalMesh.ShoulderPivot;
            leftArm = Part(bird, "Left wing", QuetzalMesh.Arm(-1), new Vector3(-shoulder.x, shoulder.y, shoulder.z));
            rightArm = Part(bird, "Right wing", QuetzalMesh.Arm(1), shoulder);
            leftHand = Part(leftArm, "Left hand", QuetzalMesh.Hand(-1), new Vector3(-QuetzalMesh.ArmLength, 0, 0));
            rightHand = Part(rightArm, "Right hand", QuetzalMesh.Hand(1), new Vector3(QuetzalMesh.ArmLength, 0, 0));
            // Streamers live in lab space (not under the body) because they are simulated in the world.
            int count = QuetzalMesh.StreamerRoots.Length; streamers = new Streamer[count];
            for (int i = 0; i < count; i++)
            {
                var mesh = QuetzalMesh.Streamer("Tail streamer " + (i + 1));
                streamers[i] = new Streamer
                {
                    mesh = mesh, view = Part(transform, mesh.name, mesh, Vector3.zero), root = QuetzalMesh.StreamerRoots[i], length = QuetzalMesh.StreamerLengths[i],
                    phase = i * 1.9f, points = new Vector3[QuetzalMesh.StreamerPoints], previous = new Vector3[QuetzalMesh.StreamerPoints],
                    vertices = new Vector3[QuetzalMesh.StreamerPoints * 3]
                };
            }
            gameObject.SetActive(true); SetVisible(false);
        }
        void OnDestroy() { foreach (var o in owned) if (o) Destroy(o); owned.Clear(); }
        void SetVisible(bool value) { bird.gameObject.SetActive(value); foreach (var s in streamers) s.view.gameObject.SetActive(value); }
        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return .5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
        }
        static Vector3 RoutePoint(Vector3[] route, float u, bool mirror)
        {
            float f = Mathf.Clamp01(u) * (route.Length - 1); int i = Mathf.Min((int)f, route.Length - 2); float t = f - i;
            Vector3 Get(int k) { var p = route[Mathf.Clamp(k, 0, route.Length - 1)]; return mirror ? new Vector3(-p.x, p.y, p.z) : p; }
            return CatmullRom(Get(i - 1), Get(i), Get(i + 1), Get(i + 2), t);
        }
        void Update()
        {
            bool reduced = LabFeedback.Current && LabFeedback.Current.ReducedMotion;
            if (reduced) { if (flying) { flying = false; SetVisible(false); } return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
            clock += Time.unscaledDeltaTime;
            float cycle = FlightSeconds + RestSeconds;
            if (clock >= cycle) { clock -= cycle; pass++; }
            bool shouldFly = clock < FlightSeconds;
            if (shouldFly != flying) { flying = shouldFly; SetVisible(flying); resetStreamers = true; }
            if (!flying) return;
            bool mirror = pass % 2 == 1;
            var parent = transform.parent;
            Vector3 position, forward = lastForward; float size = NearSize, bank = 0, flare = 0;
            if (clock < InSeconds)
            {
                // Arrive: decelerate and flare into the hover.
                float u = clock / InSeconds, s = 1 - Mathf.Pow(1 - u, 2.2f);
                position = RoutePoint(RouteIn, s, mirror);
                var ahead = RoutePoint(RouteIn, Mathf.Min(1, s + .015f), mirror); var behind = RoutePoint(RouteIn, Mathf.Max(0, s - .015f), mirror);
                if ((ahead - behind).sqrMagnitude > 1e-5f) forward = (ahead - behind).normalized;
                bank = Vector3.Cross((position - behind).normalized, forward).y;
                hoverBlend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.72f, 1, u));
                flare = Mathf.Sin(Mathf.InverseLerp(.6f, 1, u) * Mathf.PI);
                size *= Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .06f, u));
            }
            else if (clock < InSeconds + HoverSeconds)
            {
                float h = clock - InSeconds;
                var p = HoverPoint; if (mirror) p.x = -p.x;
                position = p + new Vector3(Mathf.Sin(h * 1.1f) * .04f, Mathf.Sin(h * 2.3f) * .03f, Mathf.Sin(h * .8f) * .03f);
                hoverBlend = 1;
            }
            else
            {
                // Depart: pitch forward, accelerate away, and let distance do the shrinking over the last third.
                float u = (clock - InSeconds - HoverSeconds) / OutSeconds, s = Mathf.Pow(u, 1.7f);
                position = RoutePoint(RouteOut, s, mirror);
                var ahead = RoutePoint(RouteOut, Mathf.Min(1, s + .015f), mirror); var behind = RoutePoint(RouteOut, Mathf.Max(0, s - .015f), mirror);
                if ((ahead - behind).sqrMagnitude > 1e-5f) forward = (ahead - behind).normalized;
                bank = Vector3.Cross((position - behind).normalized, forward).y;
                hoverBlend = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .16f, u));
                float far = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.6f, 1, u));
                size *= Mathf.Lerp(1, .14f, far) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.95f, 1, u)));
            }
            lastForward = forward;
            // Undulating flight: flap bursts with a slight rise, then a short bound with the wings tucked and a dip.
            float bound = Mathf.Repeat(clock, BoundCycle) / BoundCycle;
            float tuckGoal = (1 - hoverBlend) * (1 - flare) * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.74f, .8f, bound)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.94f, 1, bound)));
            tuck = Mathf.Lerp(tuck, tuckGoal, 1 - Mathf.Exp(-18 * dt));
            float undulation = Mathf.Sin(bound * Mathf.PI * 2 - .6f) * .05f * (1 - hoverBlend);
            position += Vector3.up * (undulation + Mathf.Sin(wingPhase) * .012f * size * (1 - tuck));
            bird.position = parent.TransformPoint(position);
            // Poses: banked level flight; a flare that pitches the body up; the near-upright hover in three-quarter profile.
            var flightPose = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(-35 * flare, 0, Mathf.Clamp(-bank * 300, -35, 35));
            var cam = Camera.main;
            var toViewer = cam ? parent.InverseTransformPoint(cam.transform.position) - position : Vector3.back;
            toViewer.y = 0; if (toViewer.sqrMagnitude < 1e-4f) toViewer = Vector3.back;
            var hoverPose = Quaternion.LookRotation(Quaternion.Euler(0, mirror ? 50 : -50, 0) * toViewer.normalized, Vector3.up) * Quaternion.Euler(-60, 0, 0);
            bird.rotation = parent.rotation * Quaternion.Slerp(flightPose, hoverPose, hoverBlend);
            bird.localScale = Vector3.one * size;
            AnimateWings(dt);
            AnimateHead(dt, cam, parent.rotation * forward);
            if ((bird.position - lastPosition).sqrMagnitude > 1) resetStreamers = true;
            lastPosition = bird.position;
            SimulateStreamers(Mathf.Min(Time.unscaledDeltaTime, .25f), size);
        }
        void AnimateWings(float dt)
        {
            // Deep wingbeats (~4 Hz in flight, ~5 Hz in the hover); the hover beats over the back with a figure-of-eight sweep.
            float frequency = Mathf.Lerp(26, 32, hoverBlend), amplitude = Mathf.Lerp(48, 64, hoverBlend), mean = Mathf.Lerp(8, 26, hoverBlend);
            wingPhase += dt * frequency * (1 - tuck * .85f);
            float elevation = Mathf.Sin(wingPhase) * amplitude + mean;
            float upstroke = Mathf.Clamp01(Mathf.Cos(wingPhase));                   // rising wing: wrist folds, hand sweeps back
            float sweep = Mathf.Cos(wingPhase) * Mathf.Lerp(8, 28, hoverBlend);
            float handSweep = upstroke * 38, handElevation = Mathf.Sin(wingPhase - .8f) * amplitude * .4f - upstroke * 12;
            // Bound: wings closed against the body, hands folded back over the arms.
            elevation = Mathf.Lerp(elevation, -32, tuck); sweep = Mathf.Lerp(sweep, 58, tuck);
            handSweep = Mathf.Lerp(handSweep, 125, tuck); handElevation = Mathf.Lerp(handElevation, 0, tuck);
            float twist = -upstroke * 18;                                          // feathers feather on the upstroke
            leftArm.localRotation = Quaternion.Euler(twist, -sweep, -elevation); rightArm.localRotation = Quaternion.Euler(twist, sweep, elevation);
            leftHand.localRotation = Quaternion.Euler(0, -handSweep, -handElevation); rightHand.localRotation = Quaternion.Euler(0, handSweep, handElevation);
        }
        // The head is held steady in the world (level, looking along the flight or at the viewer) whatever the body does,
        // with small curious glances.
        void AnimateHead(float dt, Camera cam, Vector3 flightDirection)
        {
            if (Time.unscaledTime > nextGlance) { glance = new Vector3(Random.Range(-8f, 8f), Random.Range(-12f, 12f), Random.Range(-16f, 16f)); nextGlance = Time.unscaledTime + Random.Range(.5f, 1.4f); }
            var look = new Vector3(flightDirection.x, flightDirection.y * .5f, flightDirection.z);
            if (cam)
            {
                var toCam = cam.transform.position - head.position;
                float interest = Mathf.Max(hoverBlend, Mathf.Clamp01(1 - (toCam.magnitude - 2.5f) / 3) * .75f);
                look = Vector3.Slerp(look.normalized, toCam.normalized, interest);
            }
            var local = bird.InverseTransformDirection(look.normalized);
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -80, 80);
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(local.y, -1, 1)) * Mathf.Rad2Deg, -75, 75);
            var goal = Quaternion.Euler(pitch + glance.x, yaw + glance.y, glance.z);
            head.localRotation = Quaternion.Slerp(head.localRotation, goal, 1 - Mathf.Exp(-18 * dt));
        }
        // Verlet chains in world space. Each point is drawn toward the feather's rest line (straight back from the tail,
        // drooping slightly) with a stiffness that fades toward the tip, so the streamers follow the body like real
        // upper-tail coverts: trailing in flight, hanging in the upright hover, and carrying the waves that each wingbeat,
        // bound and turn send down them. Drag, light gravity and a little flutter do the rest.
        void SimulateStreamers(float dt, float size)
        {
            var back = -bird.forward; var right = bird.right; var up = bird.up;
            var rest = (back * .966f - up * .26f).normalized;
            if (resetStreamers)
            {
                foreach (var s in streamers)
                {
                    float seg = s.length * size / (s.points.Length - 1); var root = bird.TransformPoint(s.root);
                    for (int i = 0; i < s.points.Length; i++) s.points[i] = s.previous[i] = root + (back + Vector3.down * .5f).normalized * seg * i;
                    s.lastRoot = root;
                }
                lastRest = rest; resetStreamers = false; lastStep = 1 / 90f;
            }
            // Substeps cover the whole frame and the tail base is interpolated across them, so a long frame (or a hitch on
            // the headset) never yanks the chain; velocities are clamped as a last guard.
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 90), 1, 12); float step = dt / steps;
            // Time-corrected Verlet: the stored displacement is rescaled when the step length changes between frames.
            float drag = Mathf.Exp(-1.2f * step), maxMove = 9f * step, firstCarry = step / lastStep;
            foreach (var s in streamers)
            {
                int n = s.points.Length; float seg = s.length * size / (n - 1), carry = firstCarry;
                var rootNow = bird.TransformPoint(s.root);
                for (int k = 1; k <= steps; k++)
                {
                    float f = k / (float)steps; float time = clock - dt * (1 - f);
                    var root = Vector3.Lerp(s.lastRoot, rootNow, f); var restDir = Vector3.Slerp(lastRest, rest, f);
                    for (int i = 2; i < n; i++)
                    {
                        float u = i / (n - 1f);
                        var flutter = right * Mathf.Sin(time * 6.2f + s.phase - u * 5.5f) * .28f * u + up * Mathf.Sin(time * 4.1f + s.phase * 1.7f - u * 4.5f) * .18f * u;
                        // Semi-stiff rachis: strong near the tail, still holding the tip, so waves ripple rather than whip.
                        // A travelling S-wave along the rest line: gentle in flight, fuller in the hover where the tail hangs and sways.
                        var wave = (right * Mathf.Sin(time * 2.3f - u * 5.2f + s.phase) + up * .5f * Mathf.Sin(time * 1.7f - u * 4.4f + s.phase * .7f))
                            * (Mathf.Lerp(.02f, .07f, hoverBlend) * u * size);
                        var spring = (root + restDir * seg * i + wave - s.points[i]) * (60 * Mathf.Pow(1 - u, 1.5f) + 10);
                        var p = s.points[i]; var velocity = Vector3.ClampMagnitude((p - s.previous[i]) * (carry * drag), maxMove);
                        s.previous[i] = p; s.points[i] = p + velocity + (Vector3.down * 2.5f + spring + flutter * size) * step * step;
                    }
                    carry = 1;
                    s.points[0] = s.previous[0] = root;
                    s.points[1] = s.previous[1] = root + restDir * seg;
                    for (int iteration = 0; iteration < 4; iteration++)
                    {
                        for (int i = 1; i < n - 1; i++)
                        {
                            var delta = s.points[i + 1] - s.points[i]; float d = delta.magnitude; if (d < 1e-6f) continue;
                            var correction = delta * ((d - seg) / d);
                            if (i == 1) s.points[i + 1] -= correction; else { s.points[i] += correction * .5f; s.points[i + 1] -= correction * .5f; }
                        }
                        // Bending stiffness: neighbours two apart may not fold closer than ~1.6 segments.
                        for (int i = 1; i < n - 2; i++)
                        {
                            var delta = s.points[i + 2] - s.points[i]; float d = delta.magnitude; float min = seg * 1.6f;
                            if (d >= min || d < 1e-6f) continue;
                            var correction = delta * ((d - min) / d);
                            if (i == 1) s.points[i + 2] -= correction; else { s.points[i] += correction * .5f; s.points[i + 2] -= correction * .5f; }
                        }
                    }
                }
                s.lastRoot = rootNow;
            }
            lastRest = rest; lastStep = step;
            // Ribbons: the vane lies roughly in the body's horizontal plane with a slow twist, the shaft slightly raised.
            foreach (var s in streamers)
            {
                int n = s.points.Length;
                for (int i = 0; i < n; i++)
                {
                    float u = i / (n - 1f);
                    var tangent = (s.points[Mathf.Min(i + 1, n - 1)] - s.points[Mathf.Max(i - 1, 0)]).normalized;
                    var side = right - tangent * Vector3.Dot(right, tangent); if (side.sqrMagnitude < 1e-6f) side = up;
                    side = Quaternion.AngleAxis(Mathf.Sin(clock * 1.7f + u * 4 + s.phase) * 35, tangent) * side.normalized;
                    var normal = Vector3.Cross(tangent, side);
                    float width = QuetzalMesh.StreamerWidth(u) * size;
                    s.vertices[i * 3] = transform.InverseTransformPoint(s.points[i] - side * width);
                    s.vertices[i * 3 + 1] = transform.InverseTransformPoint(s.points[i] + normal * width * .35f);
                    s.vertices[i * 3 + 2] = transform.InverseTransformPoint(s.points[i] + side * width);
                }
                s.mesh.SetVertices(s.vertices); s.mesh.RecalculateNormals(); s.mesh.RecalculateBounds();
            }
        }
    }
}
