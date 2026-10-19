using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // Procedural geometry for the quetzal (Pharomachrus mocinno, male), in model space: +z forward, +y up, metres for a
    // bird of ~30 cm without its streamers. This is the production model of 0.24.3/0.24.4, restored from the shipped
    // IL2CPP code (every method, constant, colour and table; see Recovery/0.24.3-qa/QUETZAL-REFERENCE.md):
    // - body: a lofted emerald body (bluer on the mantle, greener on the flanks, gold on the upper breast, crimson belly)
    //   covered by two rows of contour feathers, white outer and blue-black central tail feathers;
    // - neck and head: a flexible neck and a rounded skull clothed in thousands of short hair filaments, laid back over
    //   the crown and forward over the bill, parting around the eyes (HeadFuzz);
    // - crest: eight sculpted segments along a curved ridge, each a shell with 160 combed filaments (the "mohawk");
    // - eyes: large dark eyes with a catchlight and a turquoise eye ring; a short yellow bill;
    // - wings: mantle-blue leading edge, dark flight feathers with pale undersides, long emerald coverts;
    // - feet: short legs feathered crimson at the top, two toes forward and two back ending in dark claws;
    // - tail streamers: four ribbons simulated as chains by DigitalQuetzal, glowing faintly toward their tips.
    // Vertex colours carry the plumage (alpha 0: pale underside on the back face); uv0 = (iridescence, self-lit),
    // uv1 = (hair, tip) for the LabQuetzal shader. Filaments are marked hair = Hair (two-sided).
    public static class QuetzalMesh
    {
        public static readonly Color Emerald = Html("#0B8050"), EmeraldDeep = Html("#064E34"), Gold = Html("#8CCB3E"),
            CrestTip = Html("#B4DC4A"), Turquoise = Html("#13A88F"), Crimson = Html("#B8160F"), BillYellow = Html("#F5AE25"),
            FlightDark = Html("#1A1916"), TailDark = Html("#0F1D26"), Undertail = Html("#EEF3F1"), EyeDark = Html("#030607"),
            Glow = Html("#7FF6FF"), LegGrey = Html("#8E8C7C"), Claw = Html("#34332C"), HeadDeep = Html("#1E7A2A"),
            HeadGreen = Html("#36A52E"), HeadLight = Html("#7BD34A"), CrestBronze = Html("#8C7A35"), EyeRing = Html("#2BB3C4"),
            MantleBlue = Html("#1590A8"), CovertGreen = Html("#1FA548"), CovertLight = Html("#74CC44"), FlightUnderside = Html("#7E8587");
        // Joints in body space: the head pivot, the base of the neck, the wing roots and the hip.
        public static readonly Vector3 NeckPivot = new Vector3(0, .045f, .105f), NeckBase = new Vector3(0, .026f, .074f);
        public static readonly Vector3 ShoulderPivot = new Vector3(.043f, .034f, .036f), Hip = new Vector3(0, -.056f, -.01f);
        public static readonly Vector3 BillTip = new Vector3(0, -.009f, .109f);
        public const float ArmLength = .1f, HandLength = .08f, FootDrop = .027f, Hair = 2;
        public const int CrestSegments = 8, StreamerPoints = 20;
        // Streamer roots on the tail base (x mirrored per side) and their lengths in model units.
        public static readonly Vector3[] StreamerRoots = { new Vector3(-.006f, .012f, -.15f), new Vector3(.006f, .012f, -.15f), new Vector3(-.012f, .008f, -.145f), new Vector3(.012f, .008f, -.145f) };
        public static readonly float[] StreamerLengths = { .78f, .72f, .44f, .38f };
        // Rig values used by the current animation code (not part of the shipped model): where a folded wing's root sits,
        // the hip above the grip contact, and the radius of the light perch on the projector.
        public static readonly Vector3 FoldedShoulder = ShoulderPivot, FootHip = new Vector3(0, FootDrop, 0);
        public const float GripRadius = .0085f;
        public static Vector3 HeadOnNeck => NeckPivot - NeckBase;

        // The skull: rings along z with their half-widths, half-heights and centre heights.
        static readonly float[] SkullZ = { -.03f, -.023f, -.013f, -.002f, .01f, .022f, .034f, .045f, .055f, .064f, .072f, .079f, .084f, .088f };
        static readonly float[] SkullRX = { .014f, .027f, .037f, .043f, .046f, .047f, .046f, .043f, .039f, .033f, .027f, .02f, .014f, .009f };
        static readonly float[] SkullRY = { .014f, .029f, .039f, .045f, .048f, .049f, .048f, .044f, .04f, .034f, .028f, .021f, .015f, .01f };
        static readonly float[] SkullCY = { 0, .002f, .005f, .008f, .01f, .011f, .011f, .011f, .01f, .008f, .005f, .003f, .001f, 0 };
        // The crest ridge from the forehead over the crown to the nape, and its radii.
        static readonly Vector3[] CrestRidge = { new Vector3(0, .026f, .079f), new Vector3(0, .05f, .062f), new Vector3(0, .061f, .038f), new Vector3(0, .063f, .012f), new Vector3(0, .054f, -.01f), new Vector3(0, .034f, -.025f) };
        static readonly Vector2[] CrestRidgeRadii = { new Vector2(.007f, .006f), new Vector2(.0095f, .0085f), new Vector2(.011f, .01f), new Vector2(.011f, .01f), new Vector2(.0095f, .0085f), new Vector2(.006f, .006f) };
        static readonly Paint FlightVane = new Paint(FlightDark, .15f).TwoTone(), FlightTip = Darker(new Paint(Color.Lerp(FlightDark, Glow, .1f), .1f, .06f).TwoTone(), .55f);

        static Color Html(string html) { ColorUtility.TryParseHtmlString(html, out var color); return color; }

        public struct Paint
        {
            public Color color; public float irid, emit, hair, tip;
            public Paint(Color color, float irid = 0, float emit = 0, float hair = 0, float tip = 0) { this.color = color; this.irid = irid; this.emit = emit; this.hair = hair; this.tip = tip; }
            public static Paint Lerp(Paint a, Paint b, float t) =>
                new Paint(Color.Lerp(a.color, b.color, t), Mathf.Lerp(a.irid, b.irid, t), Mathf.Lerp(a.emit, b.emit, t), Mathf.Lerp(a.hair, b.hair, t), Mathf.Lerp(a.tip, b.tip, t));
            public Paint At(float tipValue) { var p = this; p.tip = tipValue; return p; }
            // Feather with a pale underside on its back face.
            public Paint TwoTone() { var p = this; p.color.a = 0; return p; }
        }
        sealed class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>(); readonly List<Color> c = new List<Color>();
            readonly List<Vector2> uv = new List<Vector2>(), uv1 = new List<Vector2>(); readonly List<Vector3> normals = new List<Vector3>();
            bool shaded; public readonly List<int> t = new List<int>();
            // A zero normal leaves the vertex to RecalculateNormals; filaments give their own.
            public int Add(Vector3 p, Paint paint, Vector3 normal = default)
            {
                v.Add(p); c.Add(Linear(paint.color)); uv.Add(new Vector2(paint.irid, paint.emit)); uv1.Add(new Vector2(paint.hair, paint.tip));
                normals.Add(normal); shaded |= normal.sqrMagnitude >= 1e-10f;
                return v.Count - 1;
            }
            public void Tri(int a, int b, int d) { t.Add(a); t.Add(b); t.Add(d); }
            public void Quad(int a, int b, int d, int e) { Tri(a, b, d); Tri(a, d, e); }
            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(v); mesh.SetColors(c); mesh.SetUVs(0, uv); mesh.SetUVs(1, uv1); mesh.SetTriangles(t, 0);
                mesh.RecalculateNormals();
                if (shaded)
                {
                    var n = mesh.normals;
                    for (int i = 0; i < n.Length; i++) if (normals[i].sqrMagnitude >= 1e-10f) n[i] = normals[i];
                    mesh.normals = n;
                }
                mesh.RecalculateBounds(); return mesh;
            }
        }
        // Vertex colours are authored in sRGB.
        static Color Linear(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear
            ? new Color(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), c.a) : c;

        // Closed ellipse sections along a path (x is the side axis), capped at both ends.
        static void Loft(Builder b, Vector3[] centres, Vector2[] radii, int around, Func<Vector3, Vector3, Paint> paint)
        {
            int n = centres.Length, start = b.v.Count; Vector3 first = Vector3.forward, last = Vector3.forward;
            for (int k = 0; k < n; k++)
            {
                var forward = (centres[Mathf.Min(k + 1, n - 1)] - centres[Mathf.Max(k - 1, 0)]).normalized;
                if (k == 0) first = forward;
                if (k == n - 1) last = forward;
                var up = Vector3.Cross(forward, Vector3.right).normalized;
                var side = Vector3.Cross(up, forward).normalized;
                for (int j = 0; j < around; j++)
                {
                    float a = j * Mathf.PI * 2 / around, cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                    var outward = side * cos + up * sin;
                    b.Add(centres[k] + side * cos * radii[k].x + up * sin * radii[k].y, paint(centres[k], outward));
                }
            }
            for (int k = 0; k < n - 1; k++)
                for (int j = 0; j < around; j++)
                {
                    int a = start + k * around + j, bb = start + k * around + (j + 1) % around;
                    b.Quad(a, a + around, bb + around, bb);
                }
            int capA = b.Add(centres[0] - first * radii[0].y * .6f, paint(centres[0], -first));
            int capB = b.Add(centres[n - 1] + last * radii[n - 1].y * .6f, paint(centres[n - 1], last));
            for (int j = 0; j < around; j++)
            {
                b.Tri(capA, start + (j + 1) % around, start + j);
                b.Tri(capB, start + (n - 1) * around + j, start + (n - 1) * around + (j + 1) % around);
            }
        }
        static Vector3[] Points(float[] z, float[] y) { var p = new Vector3[z.Length]; for (int i = 0; i < z.Length; i++) p[i] = new Vector3(0, y[i], z[i]); return p; }
        static Vector2[] Radii(float[] x, float[] y) { var r = new Vector2[x.Length]; for (int i = 0; i < x.Length; i++) r[i] = new Vector2(x[i], y[i]); return r; }
        static void Ellipsoid(Builder b, Vector3 centre, Vector3 radii, int rings, int around, Paint paint)
        {
            int start = b.v.Count;
            for (int r = 0; r <= rings; r++)
            {
                float lat = r * Mathf.PI / rings - Mathf.PI / 2;
                for (int j = 0; j <= around; j++)
                {
                    float lon = j * Mathf.PI * 2 / around;
                    b.Add(centre + Vector3.Scale(new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon)), radii), paint);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int j = 0; j < around; j++) { int a = start + r * (around + 1) + j; b.Quad(a, a + around + 1, a + around + 2, a + 1); }
        }
        static void Annulus(Builder b, Vector3 centre, Vector3 normal, float inner, float outer, int segments, Paint paint)
        {
            var u = Vector3.Cross(normal, Vector3.up).normalized; var w = Vector3.Cross(normal, u);
            int start = b.v.Count;
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments; var d = u * Mathf.Cos(a) + w * Mathf.Sin(a);
                b.Add(centre + d * inner, paint); b.Add(centre + d * outer, paint);
            }
            for (int i = 0; i < segments; i++) { int a = start + i * 2; b.Quad(a, a + 1, a + 3, a + 2); }
        }
        // A tapered, optionally curved feather in the plane normal to `normal`: root, broad vane, pointed tip.
        // bend curves it sideways (scimitar coverts); the vertex alpha of the paint selects a pale underside.
        static void Feather(Builder b, Vector3 root, Vector3 dir, Vector3 normal, float length, float width, Paint vane, Paint tip, float bend = 0)
        {
            var sideUnit = Vector3.Cross(normal, dir).normalized; var side = sideUnit * width * .5f;
            var tipMix = Paint.Lerp(vane, tip, .5f);
            Vector3 At(float u) => root + dir * length * u + sideUnit * bend * length * u * u;
            int r0 = b.Add(root - side * .5f, vane), r1 = b.Add(root + side * .5f, vane);
            var mid = At(.55f); int m0 = b.Add(mid - side, vane), m1 = b.Add(mid + side, vane);
            var near = At(.85f); int n0 = b.Add(near - side * .7f, tipMix), n1 = b.Add(near + side * .7f, tipMix);
            int t0 = b.Add(At(1), tip);
            b.Quad(r0, m0, m1, r1); b.Quad(m0, n0, n1, m1); b.Tri(n0, t0, n1);
        }
        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        // One hair: a narrow two-segment blade lying along the skin (normals bend from the skin toward the strand).
        static void Filament(Builder b, Vector3 root, Vector3 dir, Vector3 side, float length, float width, Paint rootPaint, Paint tipPaint, Vector3 skin)
        {
            rootPaint.hair = Hair; tipPaint.hair = Hair;
            var tipNormal = (dir * .4f + skin).normalized;
            var half = side * width * .5f;
            int a = b.Add(root - half, rootPaint.At(0), skin), c = b.Add(root + half, rootPaint.At(0), skin);
            var midPaint = Paint.Lerp(rootPaint, tipPaint, .5f).At(.55f);
            var midNormal = (dir * .2f + skin).normalized;
            var mid = root + dir * length * .55f; var narrow = side * width * .42f;
            int m0 = b.Add(mid - narrow, midPaint, midNormal), m1 = b.Add(mid + narrow, midPaint, midNormal);
            int t = b.Add(root + dir * length, tipPaint.At(1), tipNormal);
            b.Quad(a, m0, m1, c); b.Tri(m0, t, m1);
        }
        static Vector3 Jitter(System.Random random, float amount) =>
            new Vector3(((float)random.NextDouble() - .5f) * amount, ((float)random.NextDouble() - .5f) * amount, ((float)random.NextDouble() - .5f) * amount);

        // Body from the tail base to the neck, its contour feathers and the true tail.
        public static Mesh Body()
        {
            var b = new Builder();
            float[] z = { -.17f, -.15f, -.12f, -.085f, -.045f, -.005f, .012f, .022f, .035f, .07f, .095f, .112f };
            float[] rx = { .016f, .026f, .04f, .054f, .063f, .067f, .066f, .065f, .064f, .055f, .045f, .036f };
            float[] ry = { .015f, .025f, .04f, .056f, .066f, .071f, .0705f, .07f, .069f, .06f, .048f, .038f };
            float[] cy = { .012f, .01f, .006f, .002f, -.002f, -.003f, -.001f, 0, .002f, .013f, .026f, .036f };
            Loft(b, Points(z, cy), Radii(rx, ry), 24, (centre, o) =>
            {
                // Crimson belly (its edge lower underneath), emerald turning covert green on the flanks, a little gold on the
                // upper breast, mantle blue on the back and head green toward the nape.
                float red = Smooth(.05f, -.3f, o.y) * Smooth(.024f + .008f * Smooth(-.4f, -.95f, o.y), .012f, centre.z);
                var c = Color.Lerp(Emerald, CovertGreen, Smooth(.25f, .95f, Mathf.Abs(o.x)) * .45f);
                c = Color.Lerp(c, Gold, Smooth(.045f, .1f, centre.z) * Smooth(.1f, -.6f, o.y) * .22f);
                c = Color.Lerp(c, MantleBlue, Smooth(.15f, .85f, o.y) * .6f);
                c = Color.Lerp(c, HeadGreen, Smooth(.085f, .11f, centre.z) * .6f);
                return Paint.Lerp(new Paint(c, 1), new Paint(Crimson, 0, .1f), red);
            });
            // Point on the body surface at a given z and angle around it (0 = side, pi/2 = top), with its normal.
            Vector3 Surface(float atZ, float angle, out Vector3 normal)
            {
                int i = 0; while (i < z.Length - 2 && z[i + 1] < atZ) i++;
                float t = Mathf.Clamp01((atZ - z[i]) / (z[i + 1] - z[i]));
                float radiusX = Mathf.Lerp(rx[i], rx[i + 1], t), radiusY = Mathf.Lerp(ry[i], ry[i + 1], t), centreY = Mathf.Lerp(cy[i], cy[i + 1], t);
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                var flat = new Vector2(cos / radiusX, sin / radiusY).normalized;
                normal = new Vector3(flat.x, flat.y, 0);
                return new Vector3(cos * radiusX, centreY + sin * radiusY, atZ);
            }
            // Contour feathers lying back along the upper flanks, overlapping toward the tail.
            for (int side = -1; side <= 1; side += 2)
                for (int j = 0; j < 9; j++)
                {
                    float u = j / 8f;
                    var p = Surface(.07f - .1f * u, Mathf.Lerp(.62f, .05f, u), out var normal);
                    p.x *= side; normal.x *= side;
                    var dir = (Vector3.back + Vector3.down * (.35f - .2f * u) + normal * .14f).normalized;
                    Feather(b, p + normal * .002f, dir, normal, .075f + .025f * u, .021f, CovertPaint(.3f + .5f * u), CovertTip(u), side * .06f);
                }
            // Outer tail feathers: white on both faces, spreading and hanging a little.
            for (int i = 0; i < 4; i++)
            {
                float side = i <= 1 ? -1 : 1, spread = side * (i % 2 == 0 ? 14 : 24) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(spread), -.28f, -Mathf.Cos(spread)).normalized;
                Feather(b, new Vector3(side * .006f, (i % 2) * -.002f, -.146f), dir, Vector3.up, i % 2 == 0 ? .16f : .135f, .032f, new Paint(Undertail), new Paint(Color.Lerp(Undertail, FlightUnderside, .4f)), side * .08f);
            }
            // Central tail feathers: blue-black, above the white ones.
            for (int i = -1; i <= 1; i += 2)
                Feather(b, new Vector3(i * .004f, .006f, -.148f), new Vector3(i * .06f, -.16f, -1).normalized, Vector3.up, .17f, .03f, new Paint(TailDark, .4f), new Paint(TailDark, .4f));
            return b.Build("Quetzal body");
        }

        // Neck from the shoulders to the head pivot, emerald turning head green, covered in short filaments.
        public static Mesh Neck()
        {
            var b = new Builder(); var head = HeadOnNeck;
            var centres = new[] { new Vector3(0, -.006f, -.022f), new Vector3(0, .002f, -.004f), new Vector3(0, head.y * .55f, head.z * .55f), new Vector3(0, head.y, head.z), new Vector3(0, head.y + .004f, head.z + .012f) };
            var radii = new[] { new Vector2(.024f, .026f), new Vector2(.03f, .033f), new Vector2(.031f, .034f), new Vector2(.03f, .032f), new Vector2(.023f, .025f) };
            Loft(b, centres, radii, 20, (centre, o) =>
            {
                var c = Color.Lerp(EmeraldDeep, Emerald, Smooth(-.8f, .3f, o.y));
                c = Color.Lerp(c, MantleBlue, Smooth(.2f, .9f, o.y) * .4f);
                c = Color.Lerp(c, HeadGreen, Smooth(0, head.z, centre.z));
                return new Paint(c, 1, 0, .6f);
            });
            var random = new System.Random(311);
            for (int k = 0; k < 6; k++)
            {
                float v = k * .14f + .25f, t = Mathf.Clamp01(v);
                var centre = Vector3.Lerp(centres[1], centres[3], t); var r = Vector2.Lerp(radii[1], radii[3], t);
                float offset = (k & 1) * .5f;
                for (int j = 0; j < 34; j++)
                {
                    float a = (offset + j) / 34 * Mathf.PI * 2, cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                    var dir = (new Vector3(cos * .3f, sin * .3f - .287348f, -.957826f) + Jitter(random, .2f)).normalized;
                    var radial = new Vector3(cos, sin, 0);
                    var side = Vector3.Cross(radial, dir); side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;
                    var root = new Vector3(centre.x + r.x * cos - .0005f * cos, centre.y + r.y * sin - .0005f * sin, centre.z);
                    var c = Color.Lerp(Emerald, HeadGreen, t);
                    float length = (float)random.NextDouble() * .004f + .007f;
                    Filament(b, root, dir, side, length, .002f, new Paint(c, .7f), new Paint(Color.Lerp(c, HeadLight, .4f), .6f, .1f), radial);
                }
            }
            return b.Build("Quetzal neck");
        }

        // A point on the skull at a fractional ring and an angle around it, with its normal.
        static Vector3 SkullPoint(float ring, float angle, out Vector3 normal)
        {
            int i = ring < 0 ? 0 : Mathf.Min((int)ring, SkullZ.Length - 2);
            float t = Mathf.Clamp01(ring - i);
            float rx = Mathf.Lerp(SkullRX[i], SkullRX[i + 1], t), ry = Mathf.Lerp(SkullRY[i], SkullRY[i + 1], t);
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            var flat = new Vector2(cos / rx, sin / ry).normalized;
            float slope = -(SkullRY[i + 1] - SkullRY[i]) / (SkullZ[i + 1] - SkullZ[i]) * .9f;
            normal = new Vector3(flat.x, flat.y, slope).normalized;
            return new Vector3(cos * rx, Mathf.Lerp(SkullCY[i], SkullCY[i + 1], t) + sin * ry, Mathf.Lerp(SkullZ[i], SkullZ[i + 1], t));
        }
        // Head in neck-pivot space: skull, short stout bill, and the head's hair.
        public static Mesh Head()
        {
            var b = new Builder();
            Loft(b, Points(SkullZ, SkullCY), Radii(SkullRX, SkullRY), 32, (centre, o) =>
                new Paint(Color.Lerp(Color.Lerp(HeadDeep, HeadGreen, .6f), HeadGreen, Smooth(-.8f, .4f, o.y)), .5f, 0, 1));
            var bill = new[] { new Vector3(0, .003f, .08f), new Vector3(0, 0, .092f), new Vector3(0, -.003f, .1f), new Vector3(0, -.006f, .106f), BillTip };
            var billRadii = new[] { new Vector2(.0105f, .0095f), new Vector2(.009f, .0082f), new Vector2(.0065f, .006f), new Vector2(.0038f, .0036f), new Vector2(.001f, .001f) };
            Loft(b, bill, billRadii, 14, (centre, o) => new Paint(Color.Lerp(BillYellow, new Color(1, .86f, .45f, 1), Smooth(.098f, .109f, centre.z) * .6f), 0, .08f));
            HeadFuzz(b);
            return b.Build("Quetzal head");
        }
        // The head's hair: 40 rings of 54 short filaments. They lie back over the crown, spill forward over the bill at the
        // front, part around the eyes (tinted with the eye ring there), stand up like a brush on the crown and lengthen
        // toward the nape.
        static void HeadFuzz(Builder b)
        {
            var random = new System.Random(1789);
            float Rand() => (float)random.NextDouble();
            for (int r = 0; r < 40; r++)
            {
                float ring = (r + .5f) / 40 * (SkullZ.Length - 1.6f) + .2f, offset = (r & 1) * .5f;
                for (int j = 0; j < 54; j++)
                {
                    float angle = (offset + j) / 54 * Mathf.PI * 2 + (Rand() - .5f) * .15f;
                    var p = SkullPoint(ring, angle, out var normal);
                    var fromEye = p - new Vector3(p.x < 0 ? -.031f : .031f, .017f, .059f);
                    float eyeDistance = fromEye.magnitude;
                    if (eyeDistance < .0175f) continue;              // bare around the eye
                    if (p.z > .078f && p.y < .016f) continue;        // and over the bill
                    float front = Mathf.Clamp01((p.z - .05f) / .025f), chin = Mathf.Clamp01((p.y + .01f) / -.025f);
                    float nearEye = Mathf.Clamp01((eyeDistance - .026f) / -.008f);
                    var away = eyeDistance > 1e-5f ? fromEye / eyeDistance : Vector3.zero;
                    float frontSmooth = front * front * (3 - 2 * front);
                    var flow = (away * .8f + new Vector3(0, -.196116f, -.980581f) * ((1 - frontSmooth) * .7f) + new Vector3(0, -.242536f, .970143f) * (frontSmooth * 1.4f)).normalized;
                    float crownRaw = Mathf.Clamp01((p.y - .034f) / .02f), crown = crownRaw * crownRaw * (3 - 2 * crownRaw);
                    var brush = new Vector3(p.x < 0 ? .2017733f : -.2017733f, .554877f, .807093f);
                    var laid = Vector3.Slerp(flow, brush, crown * .85f);
                    float lift = (Rand() * .2f + .25f) * (1 - nearEye * nearEye * (3 - 2 * nearEye) * .5f);
                    float eyeSmooth = nearEye * nearEye * (3 - 2 * nearEye);
                    var dir = (laid + normal * lift + Jitter(random, .18f)).normalized;
                    var side = Vector3.Cross(normal, dir); side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;
                    float length = Rand() * .004f + .006f;
                    float nape = Mathf.Clamp01((p.z - .01f) / -.03f);
                    var tipColour = Color.Lerp(HeadGreen, HeadLight, Mathf.Clamp01((1 - .6f * crown) * (Rand() * .5f + .35f)));
                    tipColour = Color.Lerp(tipColour, EyeRing, Mathf.Clamp01(eyeSmooth * .7f));
                    var rootColour = Color.Lerp(HeadDeep, HeadGreen, Mathf.Clamp01(Rand() * .35f + .4f));
                    length *= (1 + .25f * nape * nape * (3 - 2 * nape)) * (1 - .45f * eyeSmooth) * (1 - .3f * chin * chin * (3 - 2 * chin)) * (1 + .6f * frontSmooth) * (1 + .8f * crown);
                    Filament(b, p - normal * .0003f, dir, side, length, .0018f, new Paint(rootColour, .5f), new Paint(tipColour, .4f, .1f), normal);
                }
            }
        }

        public static Vector3 EyeCenter(int side) => new Vector3(side * .031f, .017f, .059f);
        // Eye (around EyeCenter): a large dark globe, a catchlight and a turquoise eye ring.
        public static Mesh Eye(int side)
        {
            var b = new Builder();
            Ellipsoid(b, Vector3.zero, Vector3.one * .0135f, 8, 14, new Paint(EyeDark));
            Ellipsoid(b, new Vector3(side * .0113f, .0056f, .0058f), Vector3.one * .003f, 4, 6, new Paint(Color.white, 0, 1));
            Annulus(b, new Vector3(side * .0035f, 0, 0), new Vector3(side, 0, 0), .0127f, .0149f, 24, new Paint(EyeRing, 0, .3f));
            return b.Build(side < 0 ? "Quetzal left eye" : "Quetzal right eye");
        }

        // ---- Crest ----
        // The ridge (Catmull-Rom through CrestRidge) at s in 0..1, its tangent and radii.
        static Vector3 Ridge(float s, out Vector3 tangent, out Vector2 radius)
        {
            int n = CrestRidge.Length;
            float f = Mathf.Clamp01(s) * (n - 1); int i = Mathf.Min((int)f, n - 2); float t = f - i;
            Vector3 P(int k) => CrestRidge[Mathf.Clamp(k, 0, n - 1)];
            Vector3 p0 = P(i - 1), p1 = P(i), p2 = P(i + 1), p3 = P(i + 2);
            var a = 2 * p0 - 5 * p1 + 4 * p2 - p3; var c = 3 * p1 - p0 - 3 * p2 + p3;
            var d = .5f * ((p2 - p0) + 2 * a * t + 3 * c * t * t);
            tangent = d.sqrMagnitude > 1e-10f ? d.normalized : Vector3.back;
            radius = Vector2.Lerp(CrestRidgeRadii[i], CrestRidgeRadii[i + 1], t);
            return .5f * (2 * p1 + (p2 - p0) * t + a * t * t + c * t * t * t);
        }
        // Lean of the crest's feathers along the ridge: forward over the bill, back over the nape.
        static float CrestLean(float s) => (50 - 110 * Mathf.Clamp01(s)) * Mathf.Deg2Rad;
        static float CrestHeight(float s) => s < .45f ? .021f + .015f * Mathf.Clamp01(Mathf.Sin(s * Mathf.PI / .45f * .5f))
            : .036f - .024f * Mathf.Clamp01(Mathf.Pow(Smooth(0, 1, (s - .45f) / .55f), 1.2f));
        static float CrestWidth(float s) => s < .45f ? .012f + .005f * Mathf.Clamp01(Mathf.Sin(s * Mathf.PI / .45f * .5f))
            : .017f - .007f * Mathf.Clamp01(Smooth(0, 1, (s - .45f) / .55f));
        // A point on the crest's surface: theta runs from one side (0) over the top (pi/2) to the other (pi).
        static Vector3 CrestSurface(float s, float theta, out Vector3 normal, out Vector3 radial)
        {
            var p = Ridge(s, out _, out var radius);
            float lean = CrestLean(s);
            radial = new Vector3(0, Mathf.Cos(lean), Mathf.Sin(lean));
            float sin = Mathf.Sin(theta), cos = Mathf.Cos(theta);
            float width = CrestWidth(s) * (1 + .45f * Smooth(0, 1, (sin - .6f) / -.9f)), height = CrestHeight(s) + .004f;
            normal = (Vector3.right * (cos / width) + radial * (sin / height)).normalized;
            return p + Vector3.up * radius.y * .2f - radial * .004f + Vector3.right * (cos * width * (1 - .18f * sin)) + radial * (sin * height);
        }
        public static float CrestPosition(int segment) => (segment + .5f) * .125f;
        public static Vector3 CrestAnchor(int segment) { var p = Ridge(CrestPosition(segment), out _, out var radius); return p + Vector3.up * radius.y * .2f; }
        // Rotation (degrees about x) of a crest segment for a mood: positive raises it, negative flattens it.
        public static float CrestAngle(int segment, float mood)
        {
            float u = Mathf.Clamp01(CrestPosition(segment));
            return mood >= 0 ? (36 * u - 16) * mood : -mood * (14 - 36 * u);
        }
        // One crest segment (around CrestAnchor): a shell along its stretch of the ridge, faded at its lower edges, and 160
        // filaments combed back along the ridge, splayed outward and lifted, bronze-tipped on top, longer in front.
        public static Mesh CrestSegment(int segment)
        {
            var b = new Builder(); var random = new System.Random(segment * 37 + 4021);
            float Rand() => (float)random.NextDouble();
            var anchor = CrestAnchor(segment);
            float from = segment / 8f, to = (segment + 1) * .125f;
            float[] stations = { Mathf.Max(from - .04375f, 0), (from + to) * .5f, Mathf.Min(to + .04375f, 1) };
            int start = b.v.Count;
            foreach (float s in stations)
                for (int j = 0; j <= 12; j++)
                {
                    float theta = Mathf.Clamp01(j / 12f) * 4.34159f - .6f, sin = Mathf.Sin(theta);
                    var p = CrestSurface(s, theta, out _, out _);
                    var c = Color.Lerp(HeadDeep, HeadGreen, Mathf.Clamp01(Mathf.Max(sin, 0) * .3f + .6f));
                    b.Add(p - anchor, new Paint(c, .5f, 0, 1, Smooth(0, 1, (sin - .35f) / -.8f) * .95f));
                }
            for (int k = 0; k < stations.Length - 1; k++)
                for (int j = 0; j < 12; j++) { int a = start + k * 13 + j; b.Quad(a, a + 13, a + 14, a + 1); }
            float lo = from - .021875f, range = to + .021875f - lo;
            for (int f = 0; f < 160; f++)
            {
                float s = Mathf.Clamp01(lo + range * Mathf.Clamp01(Rand()));
                float theta = Mathf.Clamp01(Rand()) * 3.74159f - .3f;
                var p = CrestSurface(s, theta, out var normal, out var radial);
                Ridge(s, out var tangent, out _);
                var dir = (tangent * -.75f + radial * .45f + normal * .15f + Jitter(random, .2f)).normalized;
                var side = Vector3.Cross(normal, dir); side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;
                float sin = Mathf.Sin(theta);
                float top = Mathf.Clamp01((sin - .55f) / .4f), front = Mathf.Clamp01((s - .15f) / .2f), back = Mathf.Clamp01((s - .95f) * -4);
                var tipBase = Color.Lerp(HeadGreen, HeadLight, Mathf.Clamp01(Rand() * .5f + .35f));
                float bronze = Smooth(0, 1, top) * Smooth(0, 1, front) * Smooth(0, 1, back);
                var tipColour = Color.Lerp(tipBase, CrestBronze, Mathf.Clamp01(bronze * (Rand() * .5f + .4f)));
                float lengthRandom = Rand(), forward = Mathf.Clamp01((s - .25f) * -4);
                var rootColour = Color.Lerp(HeadDeep, HeadGreen, Mathf.Clamp01(Rand() * .3f + .6f));
                float length = (lengthRandom * .7f + .6f) * .01f * (Mathf.Max(sin, 0) * .5f + .7f) * (1 + .35f * Smooth(0, 1, forward));
                Filament(b, p - normal * .0004f - anchor, dir, side, length, .0018f, new Paint(rootColour, .5f), new Paint(tipColour, .4f, .12f), normal);
            }
            return b.Build("Quetzal crest " + (segment + 1));
        }

        // Feet (around Hip): short legs feathered crimson at the top, two toes forward and two back curling into claws.
        public static Mesh Feet()
        {
            var b = new Builder();
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * .011f;
                var leg = new[] { new Vector3(x, .004f, .001f), new Vector3(x, -.008f, .003f), new Vector3(x, -.019f, .004f) };
                var legRadii = new[] { new Vector2(.0052f, .0052f), new Vector2(.0038f, .0038f), new Vector2(.0028f, .0028f) };
                Loft(b, leg, legRadii, 8, (centre, o) => Paint.Lerp(new Paint(Crimson), new Paint(LegGrey, 0, .05f), Smooth(-.004f, -.012f, centre.y)));
                for (int t = 0; t < 4; t++)
                {
                    float lateral = (t & 1) != 0 ? 1 : -1, forward = t <= 1 ? 1 : -1, splay = lateral * .0035f;
                    var toe = new[] { new Vector3(x + splay * .3f, -.019f, .004f), new Vector3(x + splay, -.0225f, forward * .008f + .004f),
                        new Vector3(x + splay * 1.2f, -.027f, forward * .0145f + .004f), new Vector3(x + splay * 1.2f, -.033f, forward * .0175f + .004f) };
                    var toeRadii = new[] { new Vector2(.0019f, .0019f), new Vector2(.0017f, .0017f), new Vector2(.0014f, .0014f), new Vector2(.0007f, .0007f) };
                    Loft(b, toe, toeRadii, 6, (centre, o) => Paint.Lerp(new Paint(LegGrey), new Paint(Claw), Smooth(-.029f, -.033f, centre.y)));
                }
            }
            return b.Build("Quetzal feet");
        }

        static Paint Darker(Paint p, float undersideShade) { p.color.a = undersideShade; return p; }
        static Paint CovertPaint(float u) => new Paint(Color.Lerp(Color.Lerp(CovertGreen, Turquoise, .25f - .2f * u), CovertLight, .1f), 1);
        static Paint CovertTip(float u) => new Paint(Color.Lerp(CovertGreen, CovertLight, .55f + .25f * u), .9f, .06f);
        // Inner wing (shoulder to wrist) along +x for side = 1 and -x for side = -1: a mantle-blue leading edge, seven dark
        // secondaries, ten long greater coverts sweeping back over them and eight median coverts above those.
        public static Mesh Arm(int side)
        {
            var b = new Builder(); const int steps = 6;
            for (int i = 0; i <= steps; i++)
            {
                float u = i / (float)steps, x = u * ArmLength * side, camber = .006f * Mathf.Sin(Mathf.PI * u);
                var paint = new Paint(Color.Lerp(MantleBlue, CovertGreen, .3f + .6f * u), 1);
                b.Add(new Vector3(x, camber + .002f, .028f - .01f * u), paint);
                b.Add(new Vector3(x, camber + .005f, .006f - .004f * u), paint);
            }
            for (int i = 0; i < steps; i++) { int a = i * 2; b.Quad(a, a + 2, a + 3, a + 1); }
            for (int i = 0; i < 7; i++)
            {
                float u = (i + .5f) / 7; var root = new Vector3(u * ArmLength * side, -.002f - i * .0006f, 0);
                var dir = new Vector3(.12f * side * u, -.03f, -1).normalized;
                Feather(b, root, dir, Vector3.up, .088f - .008f * u, .026f, Darker(FlightVane, i % 2 * .22f), FlightTip);
            }
            for (int i = 0; i < 10; i++)
            {
                float u = (i + .5f) / 10; var root = new Vector3(u * ArmLength * side * .97f, .004f + i * .0005f, .014f);
                var dir = new Vector3(.03f * side + .18f * side * u, .012f, -1).normalized;
                Feather(b, root, dir, Vector3.up, .095f + .03f * u, .0175f, CovertPaint(u), CovertTip(u), .1f * side);
            }
            for (int i = 0; i < 8; i++)
            {
                float u = (i + .5f) * .125f; var root = new Vector3(u * ArmLength * side * .96f, .0065f + i * .0004f, .022f);
                var dir = new Vector3(.03f * side + .15f * side * u, .012f, -1).normalized;
                Feather(b, root, dir, Vector3.up, .048f + .01f * u, .017f, CovertPaint(u * .6f), CovertTip(u), .08f * side);
            }
            return b.Build(side < 0 ? "Quetzal left arm" : "Quetzal right arm");
        }
        // Outer wing (wrist to tip): eight dark primaries fanning from back to outward under four primary coverts.
        public static Mesh Hand(int side)
        {
            var b = new Builder();
            for (int j = 0; j < 8; j++)
            {
                float u = j / 7f, angle = Mathf.Lerp(10, 84, u) * Mathf.Deg2Rad;
                var root = new Vector3(Mathf.Lerp(.006f, .074f, u) * side, -.002f - j * .0006f, -.002f);
                var dir = new Vector3(Mathf.Sin(angle) * side, -.02f, -Mathf.Cos(angle)).normalized;
                float length = Mathf.Lerp(.095f, .13f, Mathf.Sin(u * Mathf.PI * .8f));
                Feather(b, root, dir, Vector3.up, length, Mathf.Lerp(.024f, .018f, u), Darker(FlightVane, j % 2 * .22f), FlightTip, -.05f * side);
            }
            for (int j = 0; j < 4; j++)
            {
                float u = j / 3f, angle = Mathf.Lerp(18, 62, u) * Mathf.Deg2Rad;
                var root = new Vector3(Mathf.Lerp(0, .064f, u) * side, .004f + j * .0006f, .012f);
                var dir = new Vector3(Mathf.Sin(angle) * side, 0, -Mathf.Cos(angle)).normalized;
                Feather(b, root, dir, Vector3.up, .05f, .022f, CovertPaint(.5f + .5f * u), CovertTip(u));
            }
            return b.Build(side < 0 ? "Quetzal left hand" : "Quetzal right hand");
        }

        // Streamer ribbons: fixed topology (left edge, raised shaft, right edge per point), positions rebuilt every frame.
        // The vane turns from covert green toward turquoise and glows faintly near the tip.
        public static Mesh Streamer(string name)
        {
            var b = new Builder();
            for (int i = 0; i < StreamerPoints; i++)
            {
                float u = i / (StreamerPoints - 1f);
                var vane = new Paint(Color.Lerp(CovertGreen, Turquoise, .1f + .3f * u), 1, Smooth(.82f, 1, u) * .35f);
                b.Add(Vector3.zero, vane);
                b.Add(Vector3.zero, new Paint(Color.Lerp(EmeraldDeep, Emerald, .4f), 1));
                b.Add(Vector3.zero, vane);
            }
            for (int i = 0; i < StreamerPoints - 1; i++) for (int j = 0; j < 2; j++) { int a = i * 3 + j; b.Quad(a, a + 3, a + 4, a + 1); }
            var mesh = b.Build(name); mesh.MarkDynamic(); return mesh;
        }
        // Lanceolate vane: a thin shaft at the root that widens along the feather and ends in a rounded point.
        public static float StreamerWidth(float u) => u < .12f ? .003f + .006f * Mathf.Clamp01(u / .12f) : u < .7f ? .009f + .004f * Mathf.Clamp01((u - .12f) / .58f)
            : u < .9f ? .013f + .003f * Mathf.Clamp01((u - .7f) / .2f) : .016f - .015f * Mathf.Clamp01((u - .9f) / .1f);
    }
}
