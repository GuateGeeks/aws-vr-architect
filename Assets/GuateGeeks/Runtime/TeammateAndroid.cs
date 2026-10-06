using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    // A teammate drawn as a holographic android in the station colour: a projected upper body that hovers on a
    // light emitter. It has a head with a dark face screen wearing the GuateGeeks eyes, ear receptors and a crest,
    // a segmented neck, an armoured chest with a light core, and two articulated arms with hands.
    // Only the head pose, one (right) hand and the pointing target arrive from the session, so the rest is
    // inferred: the torso trails the head's yaw, the right arm reaches the hand with two-bone IK, the left arm rests.
    // Each rigid part is one shared low-poly hologram mesh plus one mesh of solid light seams, so an android costs
    // about twenty draws (the eyes included), and posing it allocates nothing.
    public sealed class TeammateAndroid
    {
        public const float UpperArm = .26f, Forearm = .245f;
        const float BodyAlpha = .55f, BeamAlpha = .12f, MaxTwist = 50f, PalmOffset = .045f;
        // Head space: origin between the eyes, +Z where the person looks. Torso space: origin at the base of the neck.
        static readonly Vector3 NeckBase = new Vector3(0, -.2f, -.055f), HeadBase = new Vector3(0, -.095f, -.02f),
            RightShoulder = new Vector3(.185f, -.075f, -.01f), LeftShoulder = new Vector3(-.185f, -.075f, -.01f),
            IndexTip = new Vector3(-.022f, 0, .167f);

        public Transform Root { get; private set; }
        public Transform Head { get; private set; }
        public GeekEyes Eyes { get; private set; }
        public Vector3 RightShoulderPosition { get; private set; }
        public Vector3 RightElbow { get; private set; }
        public Vector3 RightWrist { get; private set; }
        // Where a pointer beam leaves the android: the tip of the extended index finger.
        public Vector3 PointerOrigin => rightHand.TransformPoint(IndexTip);
        Transform neck, torso, rightUpper, rightFore, rightHand, leftUpper, leftFore, leftHand;
        MeshFilter rightHandShape;
        LineRenderer hover;
        bool posed, pointing;
        float bodyYaw, phase;

        public static TeammateAndroid Build(Transform parent, Color color, float phase = 0)
        {
            Meshes.Ensure();
            var a = new TeammateAndroid { Root = parent, phase = phase };
            var shell = color; shell.a = BodyAlpha; var glow = color; glow.a = BeamAlpha;
            var hologram = SpecialMaterial("LabHologram", shell);
            var seams = LabVisuals.Material(Color.Lerp(color, White, .35f));
            a.Head = Part(parent, "Android head", Meshes.Head, hologram, Meshes.HeadLights, seams);
            Part(a.Head, "Android face screen", Meshes.Visor, LabVisuals.Material(Hex("#08131C")));
            a.Eyes = GeekEyes.Create(a.Head, .118f, "Teammate eyes");
            a.Eyes.transform.localPosition = new Vector3(0, .014f, .112f); a.Eyes.transform.localRotation = Quaternion.Euler(0, 180, 0);
            a.neck = Part(parent, "Android neck", Meshes.Neck, hologram, Meshes.NeckLights, seams);
            a.torso = Part(parent, "Android torso", Meshes.Torso, hologram, Meshes.TorsoLights, seams);
            Part(a.torso, "Android hover beam", Meshes.HoverBeam, SpecialMaterial("LabHologram", glow));
            a.hover = Ring(a.torso, new Vector3(0, -.5f, -.004f), .12f, color, .01f, 40);
            a.hover.name = "Android hover ring"; a.hover.sharedMaterial = Beam(color, false); a.hover.textureMode = LineTextureMode.Stretch;
            a.rightUpper = Part(parent, "Android right upper arm", Meshes.UpperArm, hologram, Meshes.UpperArmLights, seams);
            a.rightFore = Part(parent, "Android right forearm", Meshes.Forearm, hologram, Meshes.ForearmLights, seams);
            a.rightHand = Part(parent, "Android right hand", Meshes.HandOpen, hologram, Meshes.HandLights, seams);
            a.rightHandShape = a.rightHand.GetComponent<MeshFilter>();
            a.leftUpper = Part(parent, "Android left upper arm", Meshes.UpperArm, hologram, Meshes.UpperArmLights, seams);
            a.leftFore = Part(parent, "Android left forearm", Meshes.Forearm, hologram, Meshes.ForearmLights, seams);
            // The left hand is the right hand mirrored; Unity flips the winding for a negative scale.
            a.leftHand = Part(parent, "Android left hand", Meshes.HandOpen, hologram, Meshes.HandLights, seams);
            a.leftHand.localScale = new Vector3(-1, 1, 1);
            return a;
        }

        public void Pose(PeerState peer, float deltaTime, float time)
        {
            bool reduced = LabFeedback.Current && LabFeedback.Current.ReducedMotion;
            var look = peer.HeadRotation;
            Head.SetPositionAndRotation(peer.Head, look);
            // The body trails the head's yaw, like a person turning their head before their shoulders.
            float headYaw = look.eulerAngles.y;
            if (!posed) { bodyYaw = headYaw; posed = true; }
            else
            {
                bodyYaw = Mathf.LerpAngle(bodyYaw, headYaw, 1 - Mathf.Exp(-2.5f * deltaTime));
                float lag = Mathf.DeltaAngle(bodyYaw, headYaw);
                if (Mathf.Abs(lag) > MaxTwist) bodyYaw += lag - Mathf.Sign(lag) * MaxTwist;
            }
            var yaw = Quaternion.Euler(0, bodyYaw, 0);
            float pitch = Mathf.DeltaAngle(0, look.eulerAngles.x); // positive while looking down
            var body = yaw * Quaternion.Euler(Mathf.Clamp(pitch * .2f, -5, 10), 0, 0);
            float breath = reduced ? 0 : Mathf.Sin(time * 1.3f + phase) * .004f;
            torso.SetPositionAndRotation(peer.Head + yaw * NeckBase + Vector3.up * breath, body);
            var neckEnd = Head.TransformPoint(HeadBase); var span = neckEnd - torso.position; float length = span.magnitude;
            neck.SetPositionAndRotation(torso.position, Quaternion.LookRotation(length > 1e-4f ? span / length : Vector3.up, body * Vector3.back));
            neck.localScale = new Vector3(1, 1, Mathf.Max(length, .01f));
            hover.widthMultiplier = reduced ? .01f : .01f + Mathf.Sin(time * 2.2f + phase) * .003f;

            // Right arm: the hand goes where the person's tracked hand is, aimed at what they point at.
            var forward = yaw * Vector3.forward;
            var shoulder = torso.TransformPoint(RightShoulder);
            var aim = peer.Pointing ? peer.PointAt - peer.Hand : peer.Hand - shoulder + forward * .25f;
            aim = aim.sqrMagnitude > 1e-6f ? aim.normalized : forward;
            var elbow = SolveElbow(shoulder, peer.Hand - aim * PalmOffset, body * new Vector3(.55f, -1, -.45f), UpperArm, Forearm, out var wrist);
            Limb(rightUpper, shoulder, elbow, body); Limb(rightFore, elbow, wrist, body);
            rightHand.SetPositionAndRotation(wrist, Quaternion.LookRotation(aim, body * new Vector3(.35f, 1, 0)));
            RightShoulderPosition = shoulder; RightElbow = elbow; RightWrist = wrist;
            if (pointing != peer.Pointing) { pointing = peer.Pointing; rightHandShape.sharedMesh = pointing ? Meshes.HandPoint : Meshes.HandOpen; }

            // Left arm: relaxed at the side with a slow sway (still under reduced motion).
            shoulder = torso.TransformPoint(LeftShoulder);
            float sway = reduced ? 0 : Mathf.Sin(time * .8f + phase) * .012f;
            elbow = SolveElbow(shoulder, shoulder + body * new Vector3(.035f, -.43f, .1f + sway), body * new Vector3(-.55f, -1, -.6f), UpperArm, Forearm, out wrist);
            Limb(leftUpper, shoulder, elbow, body); Limb(leftFore, elbow, wrist, body);
            var rest = Vector3.Slerp((wrist - elbow).normalized, forward, .35f);
            leftHand.SetPositionAndRotation(wrist, Quaternion.LookRotation(rest, body * new Vector3(-1, .3f, 0)));
        }

        // Two-bone IK. Returns the elbow; `end` is where the wrist lands (the target, or as close as the arm reaches).
        // The elbow bends toward `pole`, and both bones always keep their lengths.
        public static Vector3 SolveElbow(Vector3 root, Vector3 target, Vector3 pole, float upper, float lower, out Vector3 end)
        {
            var toTarget = target - root; float distance = toTarget.magnitude;
            var direction = distance > 1e-5f ? toTarget / distance : Vector3.down;
            float reach = Mathf.Clamp(distance, Mathf.Abs(upper - lower) + 1e-3f, upper + lower - 1e-4f);
            end = root + direction * reach;
            float along = (upper * upper - lower * lower + reach * reach) / (2 * reach);
            float height = Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
            var bend = pole - direction * Vector3.Dot(pole, direction);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.Cross(direction, Mathf.Abs(direction.y) < .95f ? Vector3.up : Vector3.right);
            return root + direction * along + bend.normalized * height;
        }

        static void Limb(Transform bone, Vector3 from, Vector3 to, Quaternion body)
        {
            var direction = (to - from).normalized; var up = body * Vector3.forward;
            if (Mathf.Abs(Vector3.Dot(direction, up)) > .98f) up = body * Vector3.up;
            bone.SetPositionAndRotation(from, Quaternion.LookRotation(direction, up));
        }
        static Transform Part(Transform parent, string name, Mesh shell, Material material, Mesh seams = null, Material seamMaterial = null)
        {
            var part = MeshPart(parent, name, shell, material);
            if (seams) MeshPart(part, name + " · light", seams, seamMaterial);
            return part;
        }
        static Transform MeshPart(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var surface = go.AddComponent<MeshRenderer>(); surface.sharedMaterial = material;
            surface.shadowCastingMode = ShadowCastingMode.Off; surface.receiveShadows = false;
            surface.lightProbeUsage = LightProbeUsage.Off; surface.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        // Shared geometry, built once. Shells are hologram surfaces; "Lights" are the solid seams in the station colour.
        static class Meshes
        {
            public static Mesh Head, HeadLights, Visor, Neck, NeckLights, Torso, TorsoLights, HoverBeam,
                UpperArm, UpperArmLights, Forearm, ForearmLights, HandOpen, HandPoint, HandLights;
            public static void Ensure()
            {
                if (Head && HandLights) return;
                // Head: cranium, jaw, crest and ear receptors around a dark face screen.
                Head = new Solid().Ellipsoid(new Vector3(0, .028f, -.012f), new Vector3(.094f, .112f, .106f))
                    .Ellipsoid(new Vector3(0, -.058f, .012f), new Vector3(.068f, .052f, .074f))
                    .Ellipsoid(new Vector3(0, .118f, -.028f), new Vector3(.013f, .032f, .088f))
                    .Tube(new Vector3(.084f, 0, -.014f), new Vector3(.108f, 0, -.014f), .034f, .03f)
                    .Tube(new Vector3(-.084f, 0, -.014f), new Vector3(-.108f, 0, -.014f), .034f, .03f).Build("Android head");
                HeadLights = new Solid().Tube(new Vector3(.107f, 0, -.014f), new Vector3(.113f, 0, -.014f), .02f, .02f)
                    .Tube(new Vector3(-.107f, 0, -.014f), new Vector3(-.113f, 0, -.014f), .02f, .02f)
                    .Path(.0035f, new Vector3(0, .143f, .03f), new Vector3(0, .151f, -.028f), new Vector3(0, .146f, -.075f))
                    .Path(.0028f, new Vector3(-.026f, -.068f, .079f), new Vector3(0, -.068f, .087f), new Vector3(.026f, -.068f, .079f))
                    .Path(.0028f, new Vector3(-.02f, -.082f, .075f), new Vector3(0, -.082f, .079f), new Vector3(.02f, -.082f, .075f))
                    .Loop(new Vector3(0, .014f, .07f), new Vector3(.09f, 0, 0), new Vector3(0, .052f, 0), .0035f, 28).Build("Android head lights");
                Visor = new Solid().Ellipsoid(new Vector3(0, .014f, .068f), new Vector3(.085f, .048f, .042f)).Build("Android face screen");
                // Neck: unit length along +Z, stretched to the gap between torso and head every frame.
                Neck = new Solid().Tube(Vector3.zero, Vector3.forward, .04f, .034f).Build("Android neck");
                NeckLights = new Solid().Tube(new Vector3(0, 0, .3f), new Vector3(0, 0, .34f), .042f, .042f)
                    .Tube(new Vector3(0, 0, .62f), new Vector3(0, 0, .66f), .038f, .038f).Build("Android neck lights");
                // Torso: collar, chest, shoulder armour, a segmented abdomen around a spine, pelvis and the emitter housing.
                Torso = new Solid().Ellipsoid(new Vector3(0, -.035f, -.005f), new Vector3(.12f, .042f, .07f))
                    .Ellipsoid(new Vector3(0, -.125f, .005f), new Vector3(.165f, .125f, .1f))
                    .Ellipsoid(new Vector3(.182f, -.06f, -.005f), new Vector3(.068f, .058f, .07f))
                    .Ellipsoid(new Vector3(-.182f, -.06f, -.005f), new Vector3(.068f, .058f, .07f))
                    .Tube(new Vector3(0, -.21f, -.01f), new Vector3(0, -.43f, -.01f), .054f, .05f)
                    .Ellipsoid(new Vector3(0, -.27f, 0), new Vector3(.104f, .04f, .078f))
                    .Ellipsoid(new Vector3(0, -.335f, 0), new Vector3(.094f, .036f, .072f))
                    .Ellipsoid(new Vector3(0, -.415f, -.004f), new Vector3(.122f, .06f, .088f))
                    .Ellipsoid(new Vector3(0, -.468f, -.004f), new Vector3(.075f, .022f, .075f)).Build("Android torso");
                TorsoLights = new Solid().Tube(new Vector3(0, -.12f, .093f), new Vector3(0, -.12f, .11f), .024f, .024f)
                    .Loop(new Vector3(0, -.12f, .104f), new Vector3(.04f, 0, 0), new Vector3(0, .04f, 0), .0028f, 20)
                    .Path(.0035f, new Vector3(-.1f, -.045f, .054f), new Vector3(-.05f, -.05f, .074f), new Vector3(0, -.052f, .088f), new Vector3(.05f, -.05f, .074f), new Vector3(.1f, -.045f, .054f))
                    .Path(.003f, new Vector3(0, -.155f, .1f), new Vector3(0, -.185f, .092f), new Vector3(0, -.212f, .077f))
                    .Path(.003f, new Vector3(.182f, -.01f, .035f), new Vector3(.182f, 0, -.005f), new Vector3(.182f, -.01f, -.045f))
                    .Path(.003f, new Vector3(-.182f, -.01f, .035f), new Vector3(-.182f, 0, -.005f), new Vector3(-.182f, -.01f, -.045f))
                    .Loop(new Vector3(0, -.303f, -.004f), new Vector3(.066f, 0, 0), new Vector3(0, 0, .058f), .003f, 24)
                    .Loop(new Vector3(0, -.475f, -.004f), new Vector3(.073f, 0, 0), new Vector3(0, 0, .073f), .004f, 24).Build("Android torso lights");
                HoverBeam = new Solid().Tube(new Vector3(0, -.485f, -.004f), new Vector3(0, -.72f, -.004f), .066f, .01f, false).Build("Android hover beam");
                // Arms: limbs along +Z from the joint they hang from.
                UpperArm = new Solid().Ellipsoid(Vector3.zero, Vector3.one * .052f)
                    .Tube(new Vector3(0, 0, .03f), new Vector3(0, 0, .235f), .043f, .036f)
                    .Ellipsoid(new Vector3(0, 0, TeammateAndroid.UpperArm), Vector3.one * .037f).Build("Android upper arm");
                UpperArmLights = new Solid().Tube(new Vector3(0, 0, .11f), new Vector3(0, 0, .122f), .045f, .045f).Build("Android upper arm lights");
                Forearm = new Solid().Ellipsoid(new Vector3(0, 0, .08f), new Vector3(.044f, .044f, .075f))
                    .Tube(new Vector3(0, 0, .02f), new Vector3(0, 0, .225f), .038f, .028f)
                    .Ellipsoid(new Vector3(0, 0, TeammateAndroid.Forearm), Vector3.one * .022f).Build("Android forearm");
                ForearmLights = new Solid().Tube(new Vector3(0, 0, .198f), new Vector3(0, 0, .207f), .032f, .032f).Build("Android forearm lights");
                // Right hand, palm down, fingers along +Z, thumb on -X. Open: four slightly curled fingers.
                HandOpen = Hand(new Solid()).Tube(new Vector3(-.03f, -.004f, .03f), new Vector3(-.048f, -.01f, .07f), .011f, .009f)
                    .Tube(new Vector3(-.024f, -.002f, .094f), new Vector3(-.024f, -.014f, .142f), .0088f, .0078f)
                    .Tube(new Vector3(-.008f, -.002f, .096f), new Vector3(-.008f, -.014f, .148f), .0088f, .0078f)
                    .Tube(new Vector3(.008f, -.002f, .096f), new Vector3(.008f, -.014f, .145f), .0088f, .0078f)
                    .Tube(new Vector3(.024f, -.002f, .092f), new Vector3(.024f, -.013f, .134f), .0082f, .0072f).Build("Android hand open");
                // Pointing: index extended (its tip is IndexTip), the other fingers folded, thumb tucked.
                var point = Hand(new Solid()).Tube(new Vector3(-.028f, -.012f, .035f), new Vector3(-.012f, -.02f, .075f), .011f, .009f)
                    .Tube(new Vector3(-.022f, 0, .094f), new Vector3(-.022f, 0, .158f), .0088f, .0078f);
                foreach (var x in new[] { -.006f, .01f, .025f })
                    point.Tube(new Vector3(x, -.006f, .092f), new Vector3(x, -.03f, .104f), .009f, .008f).Tube(new Vector3(x, -.03f, .104f), new Vector3(x, -.034f, .086f), .008f, .007f);
                HandPoint = point.Build("Android hand pointing");
                HandLights = new Solid().Path(.003f, new Vector3(-.03f, .013f, .085f), new Vector3(.03f, .013f, .085f)).Build("Android hand lights");
            }
            static Solid Hand(Solid solid) => solid.Ellipsoid(Vector3.zero, Vector3.one * .022f).Ellipsoid(new Vector3(0, .002f, .05f), new Vector3(.038f, .017f, .05f));
        }

        // Minimal mesh builder: ellipsoids, tapered tubes and tube paths, all with outward clockwise faces.
        sealed class Solid
        {
            readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            readonly List<int> triangles = new List<int>();
            void Triangle(int a, int b, int c) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
            public Solid Ellipsoid(Vector3 center, Vector3 radii, int sides = 14, int rings = 9)
            {
                int start = vertices.Count;
                for (int i = 0; i <= rings; i++)
                    for (int j = 0; j <= sides; j++)
                    {
                        float phi = Mathf.PI * i / rings, theta = 2 * Mathf.PI * j / sides;
                        var unit = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                        vertices.Add(center + Vector3.Scale(unit, radii));
                        normals.Add(new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z).normalized);
                    }
                for (int i = 0; i < rings; i++)
                    for (int j = 0; j < sides; j++)
                    {
                        int a = start + i * (sides + 1) + j, b = a + sides + 1;
                        Triangle(a, a + 1, b); Triangle(a + 1, b + 1, b);
                    }
                return this;
            }
            public Solid Tube(Vector3 from, Vector3 to, float fromRadius, float toRadius, bool caps = true, int sides = 12)
            {
                var axis = to - from; float length = axis.magnitude;
                if (length < 1e-5f) return this;
                axis /= length;
                var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right).normalized;
                var w = Vector3.Cross(axis, u);
                float slope = (fromRadius - toRadius) / length;
                int start = vertices.Count;
                for (int ring = 0; ring < 2; ring++)
                    for (int s = 0; s <= sides; s++)
                    {
                        float theta = 2 * Mathf.PI * s / sides;
                        var radial = u * Mathf.Cos(theta) + w * Mathf.Sin(theta);
                        vertices.Add((ring == 0 ? from : to) + radial * (ring == 0 ? fromRadius : toRadius));
                        normals.Add((radial + axis * slope).normalized);
                    }
                for (int s = 0; s < sides; s++)
                {
                    int a = start + s, b = a + sides + 1;
                    Triangle(a, a + 1, b); Triangle(a + 1, b + 1, b);
                }
                if (caps) { Cap(from, -axis, fromRadius, u, w, sides, false); Cap(to, axis, toRadius, u, w, sides, true); }
                return this;
            }
            void Cap(Vector3 center, Vector3 normal, float radius, Vector3 u, Vector3 w, int sides, bool end)
            {
                if (radius <= 0) return;
                int c = vertices.Count; vertices.Add(center); normals.Add(normal);
                for (int s = 0; s <= sides; s++)
                {
                    float theta = 2 * Mathf.PI * s / sides;
                    vertices.Add(center + (u * Mathf.Cos(theta) + w * Mathf.Sin(theta)) * radius); normals.Add(normal);
                }
                for (int s = 0; s < sides; s++) if (end) Triangle(c, c + 1 + s, c + 2 + s); else Triangle(c, c + 2 + s, c + 1 + s);
            }
            // A thin light seam through the points.
            public Solid Path(float radius, params Vector3[] points)
            {
                for (int i = 1; i < points.Length; i++) Tube(points[i - 1], points[i], radius, radius, true, 6);
                return this;
            }
            // A closed seam: an ellipse spanned by two axes around a centre.
            public Solid Loop(Vector3 center, Vector3 axisA, Vector3 axisB, float radius, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    float t0 = 2 * Mathf.PI * i / count, t1 = 2 * Mathf.PI * (i + 1) / count;
                    Tube(center + axisA * Mathf.Cos(t0) + axisB * Mathf.Sin(t0), center + axisA * Mathf.Cos(t1) + axisB * Mathf.Sin(t1), radius, radius, false, 6);
                }
                return this;
            }
            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
