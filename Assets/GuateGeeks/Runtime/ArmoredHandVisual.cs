using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace GuateGeeks.AwsVr
{
    // One dynamic solid mesh per hand plus a forearm gauntlet. Joint poses remain in XR tracking-origin space.
    // Hand: graphite under-suit on tapered finger segments, titanium plates on every phalanx, a pearl dorsal shield and
    // knuckle guard, a thenar pad, luminous knuckle nodes and a quetzal-teal wrist seal (one draw call; vertex colours,
    // vertex alpha marks self-lit parts, LabEmblem shader). Fingertips carry the touch emitters.
    // Gauntlet: overlapping armour lames along the forearm with teal side strips and, on top, the perch rail where the
    // quetzal stands. The rail fills with light while an offer is being held and glows steadily while the bird is on it;
    // it is dark when perching is turned off for this arm. The forearm axis is QuetzalSenses' estimate, so the bird
    // lands on the rail it sees.
    public sealed class ArmoredHandVisual : MonoBehaviour
    {
        const int Bones = 19, Knuckles = 4, Extras = 5, Sides = 12, Rings = 8, Parts = Bones * 2 + Extras + Knuckles;
        const int Around = 22, RailRings = 9, RailAround = 10;
        // Gauntlet rings: distance behind the wrist, radius factor (an inner lip at the wrist, a raised lead edge on each
        // lame, a rounded end cap toward the elbow).
        static readonly float[] ringBack = { .012f, .012f, .02f, .054f, .058f, .096f, .1f, .138f, .142f, .168f, .184f, .194f, .199f };
        static readonly float[] ringScale = { .86f, 1.05f, 1.0f, .97f, 1.06f, 1.0f, 1.06f, 1.0f, 1.06f, 1.02f, .88f, .58f, .02f };
        public const float RailFrom = .04f, RailTo = .112f, RailRadius = .0048f;
        readonly Vector3[] vertices = new Vector3[Parts * Sides * Rings];
        readonly Vector3[] normals = new Vector3[Parts * Sides * Rings];
        readonly Color[] colors = new Color[Parts * Sides * Rings];
        readonly Vector3[] armVertices = new Vector3[ringBack.Length * Around + RailRings * RailAround];
        readonly Color[] armColors = new Color[ringBack.Length * Around + RailRings * RailAround];
        static readonly XRHandJointID[] knuckleJoints = { XRHandJointID.IndexProximal, XRHandJointID.MiddleProximal, XRHandJointID.RingProximal, XRHandJointID.LittleProximal };
        readonly Vector3[] joints = new Vector3[26];
        readonly bool[] valid = new bool[26];
        Mesh mesh, armMesh;
        MeshRenderer surface, armSurface;
        Transform beacon;
        LineRenderer beaconRing;
        bool beaconOpen, left;
        Color beaconColor;
        MaterialPropertyBlock beaconBlock;
        float railOffer = -1, railShown = -1; bool railAllowed = true, railPerched;
        Vector3 railTop;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        // The camera, for the shared forearm estimate (without it the forearm continues the hand).
        public Transform Viewer { get; set; }
        public bool BeaconShown => beacon && beacon.gameObject.activeInHierarchy;
        public bool BeaconOpen => beaconOpen;
        public Vector3 BeaconPosition => beacon ? beacon.position : transform.position;
        public Vector3 BeaconNormal => beacon ? beacon.up : transform.up;
        public bool GauntletShown => armSurface && armSurface.enabled;
        // Top of the perch rail where the bird stands, in world space.
        public Vector3 PerchRailTop => transform.TransformPoint(railTop);
        public void SetBeacon(bool open, float hover, float flash)
        {
            beaconOpen = open;
            if (!beacon) return;
            beacon.localScale = new Vector3(.019f, .005f, .019f) * (1 + Mathf.Clamp01(hover) * .35f + Mathf.Clamp01(flash) * .25f);
            var color = Color.Lerp(open ? LabVisuals.Green : beaconColor, LabVisuals.White, Mathf.Max(hover, flash));
            beaconBlock.SetColor(ColorId, color);
            beacon.GetComponent<Renderer>().SetPropertyBlock(beaconBlock);
            if (beaconRing) { beaconRing.enabled = BeaconShown; beaconRing.SetPropertyBlock(beaconBlock); }
        }
        // Perch rail light: offer (0..1) fills the rail from the wrist; perched keeps it full and steady.
        public void SetPerch(float offer, bool allowed, bool perched)
        {
            offer = Mathf.Clamp01(offer);
            float pulse = perched ? 1 : offer > 0 && offer < 1 ? .75f + .25f * Mathf.Sin(Time.unscaledTime * 12) : 1;
            if (Mathf.Abs(offer - railOffer) < .01f && Mathf.Abs(pulse - railShown) < .02f && allowed == railAllowed && perched == railPerched) return;
            railOffer = offer; railShown = pulse; railAllowed = allowed; railPerched = perched;
            PaintRail();
        }
        // One luminous emitter per fingertip: every finger can press, so every finger carries the light.
        static readonly XRHandJointID[] tipJoints = { XRHandJointID.ThumbTip, XRHandJointID.IndexTip, XRHandJointID.MiddleTip, XRHandJointID.RingTip, XRHandJointID.LittleTip };
        static readonly string[] tipNames = { "Thumb emitter", "Index emitter", "Middle emitter", "Ring emitter", "Little emitter" };
        readonly Transform[] tips = new Transform[5];
        readonly float[] tipGlow = new float[5];
        readonly bool[] tipPressing = new bool[5];
        public int EmitterCount => tips.Length;
        static readonly XRHandJointID[][] digits = {
            new[] { XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip },
            new[] { XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip },
            new[] { XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip },
            new[] { XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip },
            new[] { XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip }
        };
        // Finger thickness at the base and tip of each digit (thumb, index, middle, ring, little).
        static readonly float[] digitBase = { .0108f, .0094f, .0097f, .0091f, .008f }, digitTip = { .0084f, .0073f, .0076f, .0072f, .0063f };
        Color suit, plate, palmTone, pearl, seal, node, armTop, armEdge, armUnder, armLip, strip, railOn, railOff;
        public void Initialize(bool left)
        {
            this.left = left;
            beaconBlock = new MaterialPropertyBlock();
            suit = Tone("#1A2630", 0); plate = Tone("#7F95A4", 0); palmTone = Tone("#22303A", 0); pearl = Tone("#A8BCC8", 0);
            seal = Tone("#12B8A6", .45f); node = Tone("#7FFFE0", 1); armTop = Tone("#8EA3B1", 0); armEdge = Tone("#56707F", 0); armUnder = Tone("#1E2A33", 0);
            armLip = Tone("#0E151B", 0); strip = Tone("#19D3B5", .9f); railOn = Tone("#2BF0C0", 1); railOff = Tone("#3A444D", 0);
            mesh = new Mesh { name = "Articulated armored glove" }; mesh.MarkDynamic();
            var triangles = new List<int>();
            for (int p = 0; p < Parts; p++) for (int r = 0; r < Rings - 1; r++) for (int s = 0; s < Sides; s++)
            {
                int a = p * Sides * Rings + r * Sides + s, b = p * Sides * Rings + r * Sides + (s + 1) % Sides;
                triangles.Add(a); triangles.Add(b); triangles.Add(a + Sides);
                triangles.Add(b); triangles.Add(b + Sides); triangles.Add(a + Sides);
            }
            // Part order: finger segments, phalanx plates, palm, thenar pad, dorsal shield, knuckle guard, wrist seal, knuckle nodes.
            for (int p = 0; p < Parts; p++)
            {
                int extra = p - Bones * 2;
                Color c = p < Bones ? suit : p < Bones * 2 ? plate : extra == 0 ? palmTone : extra == 1 ? palmTone : extra == 2 ? pearl : extra == 3 ? plate : extra == 4 ? seal : node;
                for (int v = 0; v < Sides * Rings; v++) colors[p * Sides * Rings + v] = c;
            }
            mesh.vertices = vertices; mesh.normals = normals; mesh.colors = colors; mesh.SetTriangles(triangles, 0);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var material = LabVisuals.SpecialMaterial("LabEmblem", new Color(.85f, .9f, .95f, 1));
            surface = gameObject.AddComponent<MeshRenderer>(); surface.shadowCastingMode = ShadowCastingMode.Off; surface.receiveShadows = false;
            surface.sharedMaterial = material;
            BuildGauntlet(material);
            for (int f = 0; f < tips.Length; f++)
            {
                tips[f] = LabVisuals.Shape(transform, tipNames[f], PrimitiveType.Sphere, Vector3.zero, Vector3.one * TipSize(f), LabVisuals.Ice).transform;
                tips[f].gameObject.SetActive(false);
            }
            beaconColor = left ? LabVisuals.Cyan : LabVisuals.Orange;
            beacon = LabVisuals.Shape(transform, "Glove dorsal emitter", PrimitiveType.Sphere, Vector3.zero, new Vector3(.019f, .005f, .019f), beaconColor).transform;
            // LineRenderer widths are in world units: a hairline ring just outside the emitter.
            beaconRing = LabVisuals.Ring(beacon, Vector3.zero, .8f, beaconColor, .0012f, 32);
            beaconRing.name = "Perch permission ring";
            surface.enabled = false; armSurface.enabled = false; beacon.gameObject.SetActive(false);
        }
        void BuildGauntlet(Material material)
        {
            var go = new GameObject("Perch gauntlet", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(transform, false);
            armMesh = new Mesh { name = "Perch gauntlet" }; armMesh.MarkDynamic();
            var triangles = new List<int>();
            for (int r = 0; r < ringBack.Length - 1; r++) for (int j = 0; j < Around; j++)
            {
                int a = r * Around + j, b = r * Around + (j + 1) % Around;
                triangles.Add(a); triangles.Add(b); triangles.Add(a + Around);
                triangles.Add(b); triangles.Add(b + Around); triangles.Add(a + Around);
            }
            int rail = ringBack.Length * Around;
            for (int r = 0; r < RailRings - 1; r++) for (int j = 0; j < RailAround; j++)
            {
                int a = rail + r * RailAround + j, b = rail + r * RailAround + (j + 1) % RailAround;
                triangles.Add(a); triangles.Add(b); triangles.Add(a + RailAround);
                triangles.Add(b); triangles.Add(b + RailAround); triangles.Add(a + RailAround);
            }
            // Shell colours: dark lips, the teal seal at the wrist, pearl-titanium on top, graphite underneath, teal strips on the sides.
            for (int r = 0; r < ringBack.Length; r++) for (int j = 0; j < Around; j++)
            {
                float angle = j * Mathf.PI * 2 / Around, up = Mathf.Sin(angle), side = Mathf.Abs(Mathf.Cos(angle));
                Color c = r == 0 ? armLip : r == 1 ? seal : up > -.15f ? ringScale[r] > 1.05f ? armEdge : armTop : armUnder;
                if (r > 1 && r < ringBack.Length - 3 && side > .93f && up > 0) c = strip;
                armColors[r * Around + j] = c;
            }
            armMesh.vertices = armVertices; armMesh.colors = armColors; armMesh.SetTriangles(triangles, 0);
            go.GetComponent<MeshFilter>().sharedMesh = armMesh;
            armSurface = go.GetComponent<MeshRenderer>(); armSurface.sharedMaterial = material;
            armSurface.shadowCastingMode = ShadowCastingMode.Off; armSurface.receiveShadows = false;
            PaintRail();
        }
        void PaintRail()
        {
            if (!armMesh) return;
            int rail = ringBack.Length * Around;
            float offer = Mathf.Max(0, railOffer);
            for (int r = 0; r < RailRings; r++)
            {
                float t = r / (RailRings - 1f);
                Color c;
                if (!railAllowed) c = railOff;
                else
                {
                    float lit = railPerched ? 1 : Mathf.Lerp(.35f, 1, Mathf.Clamp01((offer - t) * 6 + offer * .3f)) * Mathf.Max(.0001f, railShown);
                    c = Color.Lerp(Tone("#155E52", .35f), railOn, lit); c.a = Mathf.Lerp(.35f, 1, lit);
                }
                for (int j = 0; j < RailAround; j++) armColors[rail + r * RailAround + j] = c;
            }
            armMesh.colors = armColors;
        }
        public void UpdateHand(XRHand hand)
        {
            for (int i = 0; i < 26; i++) { valid[i] = hand.isTracked && hand.GetJoint(XRHandJointIDUtility.FromIndex(i)).TryGetPose(out _); }
            for (int i = 0; i < 26; i++) if (valid[i]) { hand.GetJoint(XRHandJointIDUtility.FromIndex(i)).TryGetPose(out var p); joints[i] = p.position; }
            bool palmValid = hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm);
            ApplyPoses(joints, valid, palmValid ? palm.rotation : Quaternion.identity);
        }
        static float TipSize(int finger) => finger == 0 || finger == 1 ? .0095f : finger == 4 ? .0075f : .0085f;
        // Proximity feedback: a fingertip near a touchable surface swells and brightens; contact turns it green.
        public void SetTouch(int finger, float proximity, bool pressing)
        {
            if (finger < 0 || finger >= tips.Length || !tips[finger]) return;
            proximity = Mathf.Clamp01(proximity);
            if (Mathf.Approximately(tipGlow[finger], proximity) && tipPressing[finger] == pressing) return;
            tipGlow[finger] = proximity; tipPressing[finger] = pressing;
            tips[finger].localScale = Vector3.one * TipSize(finger) * (1 + .55f * proximity);
            tips[finger].GetComponent<Renderer>().sharedMaterial = LabVisuals.Material(pressing ? LabVisuals.Green : proximity > 0 ? LabVisuals.White : LabVisuals.Ice);
        }
        static Vector3 Normal(Vector3 v, Vector3 fallback) => v.sqrMagnitude > 1e-10f ? v.normalized : fallback;
        // Also used by deterministic visual/geometry validation without a headset.
        public void ApplyPoses(Vector3[] positions, bool[] tracked, Quaternion rotation)
        {
            int wrist = XRHandJointID.Wrist.ToIndex(), middle = XRHandJointID.MiddleProximal.ToIndex();
            bool visible = tracked[wrist] && tracked[middle] && tracked[XRHandJointID.Palm.ToIndex()];
            surface.enabled = visible; armSurface.enabled = visible; beacon.gameObject.SetActive(visible);
            for (int f = 0; f < tips.Length; f++)
            {
                int tip = tipJoints[f].ToIndex();
                tips[f].gameObject.SetActive(visible && tracked[tip]);
                if (visible && tracked[tip]) tips[f].localPosition = positions[tip];
            }
            if (!visible) return;
            int part = 0;
            Vector3 dorsalUp = rotation * Vector3.up, palmDown = -dorsalUp;
            for (int f = 0; f < digits.Length; f++) for (int j = 1; j < digits[f].Length; j++)
            {
                int a = digits[f][j - 1].ToIndex(), b = digits[f][j].ToIndex();
                float u0 = (j - 1f) / (digits[f].Length - 1), u1 = j / (digits[f].Length - 1f);
                float r0 = Mathf.Lerp(digitBase[f], digitTip[f], u0), r1 = Mathf.Lerp(digitBase[f], digitTip[f], u1);
                Vector3 from = positions[a], to = positions[b];
                bool ok = tracked[a] && tracked[b];
                if (!ok) { from = to = positions[wrist]; r0 = r1 = 0; }
                Capsule(part, from, to, r0 * .86f, r1 * .86f);
                // Articulated plate riding on the back of each phalanx, with a small gap at every joint.
                Vector3 axis = to - from; float span = axis.magnitude, radius = (r0 + r1) * .5f;
                if (ok && span > .002f)
                {
                    axis /= span; var up = dorsalUp - axis * Vector3.Dot(dorsalUp, axis);
                    if (up.sqrMagnitude < 1e-6f) up = Vector3.Cross(axis, Vector3.right);
                    up.Normalize();
                    Ellipsoid(Bones + part, (from + to) * .5f + up * radius * .55f, Quaternion.LookRotation(axis, up), new Vector3(radius * .9f, radius * .42f, span * .47f));
                }
                else Ellipsoid(Bones + part, positions[wrist], Quaternion.identity, Vector3.one * 1e-5f);
                part++;
            }
            part = Bones * 2;
            Vector3 center = Vector3.Lerp(positions[wrist], positions[middle], .5f);
            float length = Mathf.Clamp(Vector3.Distance(positions[wrist], positions[middle]), .05f, .12f);
            int indexKnuckle = XRHandJointID.IndexProximal.ToIndex(), littleKnuckle = XRHandJointID.LittleProximal.ToIndex();
            bool knuckles = tracked[indexKnuckle] && tracked[littleKnuckle];
            float width = knuckles ? Mathf.Clamp(Vector3.Distance(positions[indexKnuckle], positions[littleKnuckle]), .04f, .09f) : .062f;
            Ellipsoid(part++, center, rotation, new Vector3(width * .58f, .0175f, length * .6f));
            // Thenar pad: the thumb's muscle under the palm, between the wrist and the thumb's base.
            int thumbBase = XRHandJointID.ThumbProximal.ToIndex();
            if (tracked[thumbBase])
            {
                var along = Normal(positions[thumbBase] - positions[wrist], rotation * Vector3.forward);
                var up = Normal(dorsalUp - along * Vector3.Dot(dorsalUp, along), dorsalUp);
                Ellipsoid(part++, Vector3.Lerp(positions[wrist], positions[thumbBase], .5f) + palmDown * .004f, Quaternion.LookRotation(along, up), new Vector3(.015f, .012f, Vector3.Distance(positions[wrist], positions[thumbBase]) * .5f + .006f));
            }
            else Ellipsoid(part++, positions[wrist], Quaternion.identity, Vector3.one * 1e-5f);
            // Raised pearl dorsal shield, a knuckle guard across the four knuckles and a substantial wrist seal.
            Ellipsoid(part++, center + rotation * new Vector3(0, .0125f, -.004f), rotation, new Vector3(width * .43f, .0095f, length * .38f));
            if (knuckles)
            {
                var across = Normal(positions[littleKnuckle] - positions[indexKnuckle], rotation * Vector3.right);
                var up = Normal(dorsalUp - across * Vector3.Dot(dorsalUp, across), dorsalUp);
                Ellipsoid(part++, Vector3.Lerp(positions[indexKnuckle], positions[littleKnuckle], .5f) + dorsalUp * .0085f, Quaternion.LookRotation(Vector3.Cross(across, up), up), new Vector3(width * .56f, .0055f, .0085f));
            }
            else Ellipsoid(part++, positions[wrist], Quaternion.identity, Vector3.one * 1e-5f);
            Ellipsoid(part++, positions[wrist], rotation, new Vector3(width * .47f, .0195f, .015f));
            for (int k = 0; k < Knuckles; k++)
            {
                int joint = knuckleJoints[k].ToIndex();
                if (tracked[joint]) Ellipsoid(part++, positions[joint] + dorsalUp * .0145f, rotation, new Vector3(.0034f, .0022f, .0034f));
                else Ellipsoid(part++, positions[wrist], rotation, Vector3.one * 1e-5f);
            }
            beacon.SetLocalPositionAndRotation(center + rotation * new Vector3(0, .0225f, -.006f), rotation);
            mesh.vertices = vertices; mesh.normals = normals; mesh.RecalculateBounds();
            Gauntlet(positions[wrist], Normal(positions[middle] - positions[wrist], rotation * Vector3.forward), dorsalUp);
        }
        // Lames along the forearm (elliptical, wider than deep), and the perch rail on top.
        void Gauntlet(Vector3 wrist, Vector3 hand, Vector3 dorsal)
        {
            var axis = hand;
            if (Viewer)
            {
                var eye = transform.InverseTransformPoint(Viewer.position);
                var forward = Vector3.ProjectOnPlane(transform.InverseTransformDirection(Viewer.forward), Vector3.up);
                if (forward.sqrMagnitude > 1e-4f) axis = Normal(wrist - QuetzalSenses.ForearmElbow(wrist, hand, left, eye, forward.normalized), hand);
            }
            var up = Normal(dorsal - axis * Vector3.Dot(dorsal, axis), Vector3.Cross(axis, Vector3.right));
            var side = Vector3.Cross(axis, up).normalized;
            float full = ringBack[ringBack.Length - 1];
            for (int r = 0; r < ringBack.Length; r++)
            {
                float back = ringBack[r], u = back / full;
                float a = Mathf.Lerp(.030f, .037f, u) * ringScale[r], b = Mathf.Lerp(.024f, .030f, u) * ringScale[r];
                var c = wrist - axis * back;
                for (int j = 0; j < Around; j++)
                {
                    float angle = j * Mathf.PI * 2 / Around;
                    armVertices[r * Around + j] = c + side * Mathf.Cos(angle) * a + up * Mathf.Sin(angle) * b;
                }
            }
            int rail = ringBack.Length * Around;
            for (int r = 0; r < RailRings; r++)
            {
                float t = r / (RailRings - 1f), back = Mathf.Lerp(RailFrom, RailTo, t);
                float radius = RailRadius * Mathf.Max(.2f, Mathf.Sqrt(Mathf.Sin(Mathf.PI * Mathf.Lerp(.04f, .96f, t))));
                float lame = Mathf.Lerp(.024f, .030f, back / full);
                var c = wrist - axis * back + up * (lame + .0035f);
                if (r == RailRings / 2) railTop = c + up * RailRadius;
                for (int j = 0; j < RailAround; j++)
                {
                    float angle = j * Mathf.PI * 2 / RailAround;
                    armVertices[rail + r * RailAround + j] = c + side * Mathf.Cos(angle) * radius + up * Mathf.Sin(angle) * radius;
                }
            }
            armMesh.vertices = armVertices; armMesh.RecalculateNormals(); armMesh.RecalculateBounds();
        }
        static Color Tone(string hex, float glow) { var c = LabVisuals.Hex(hex); if (QualitySettings.activeColorSpace == ColorSpace.Linear) c = c.linear; c.a = glow; return c; }
        // Tapered capsule: hemispherical caps of radius ra at a and rb at b.
        void Capsule(int part, Vector3 a, Vector3 b, float ra, float rb)
        {
            Vector3 axis = b - a; float length = axis.magnitude;
            var rotation = length > .0001f ? Quaternion.FromToRotation(Vector3.up, axis / length) : Quaternion.identity;
            for (int r = 0; r < Rings; r++)
            {
                float phi = Mathf.PI * r / (Rings - 1), y = Mathf.Cos(phi), radial = Mathf.Sin(phi);
                bool top = y >= 0; float radius = top ? rb : ra;
                for (int s = 0; s < Sides; s++)
                {
                    float theta = 2 * Mathf.PI * s / Sides; var n = new Vector3(radial * Mathf.Cos(theta), y, radial * Mathf.Sin(theta));
                    int i = part * Sides * Rings + r * Sides + s;
                    vertices[i] = (top ? b : a) + rotation * (n * radius);
                    normals[i] = rotation * n;
                }
            }
        }
        void Ellipsoid(int part, Vector3 center, Quaternion rotation, Vector3 size)
        {
            for (int r = 0; r < Rings; r++) for (int s = 0; s < Sides; s++)
            {
                float phi = Mathf.PI * r / (Rings - 1), theta = 2 * Mathf.PI * s / Sides;
                var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                int i = part * Sides * Rings + r * Sides + s;
                vertices[i] = center + rotation * Vector3.Scale(n, size);
                normals[i] = rotation * new Vector3(n.x / size.x, n.y / size.y, n.z / size.z).normalized;
            }
        }
        void OnDestroy() { if (mesh) Destroy(mesh); if (armMesh) Destroy(armMesh); }
    }
}
