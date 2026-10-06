using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // Procedural geometry for the quetzal (Pharomachrus mocinno, male), in model space: +z forward, +y up, metres for a
    // bird of ~30 cm without its streamers. Modelled from reference photographs:
    // - head: a dense, spiky brush crest from the forehead to the nape that spills forward over a short yellow bill,
    //   and a large dark eye set well forward;
    // - body: iridescent emerald back and breast (feather-row texture in the shader), crimson belly, white undertail;
    // - wings: long pointed emerald coverts that sweep back over dark flight feathers whose undersides are pale grey;
    // - tail: dark central feathers, white outer feathers, and four long lanceolate upper-tail coverts (the streamers),
    //   simulated as chains by DigitalQuetzal.
    // Vertex colours carry the plumage; alpha 0 marks feathers whose back face shows the pale underside colour.
    // uv0.x marks iridescent feathers and uv0.y self-lit areas (LabQuetzal shader, double-sided).
    public static class QuetzalMesh
    {
        public static readonly Color Emerald = LabVisuals.Hex("#0B8050"), EmeraldDeep = LabVisuals.Hex("#064E34"), Gold = LabVisuals.Hex("#8CCB3E"),
            CrestTip = LabVisuals.Hex("#B4DC4A"), Turquoise = LabVisuals.Hex("#13A88F"), Crimson = LabVisuals.Hex("#B8160F"),
            BillYellow = LabVisuals.Hex("#F2C230"), FlightDark = LabVisuals.Hex("#1A1916"), TailDark = LabVisuals.Hex("#0F1D26"),
            Undertail = LabVisuals.Hex("#EEF3F1"), EyeDark = LabVisuals.Hex("#030607"), Glow = LabVisuals.Hex("#7FF6FF");
        public static readonly Color FlightUnderside = LabVisuals.Hex("#7E8587");
        // Where the head pivot sits on the body (the neck) and where the wings attach, in body space.
        public static readonly Vector3 NeckPivot = new Vector3(0, .045f, .105f);
        public static readonly Vector3 ShoulderPivot = new Vector3(.043f, .034f, .036f);
        public const float ArmLength = .10f, HandLength = .08f;
        // Streamer roots on the tail base (x mirrored per side) and their lengths in model units.
        public static readonly Vector3[] StreamerRoots = { new Vector3(-.006f, .012f, -.15f), new Vector3(.006f, .012f, -.15f), new Vector3(-.012f, .008f, -.145f), new Vector3(.012f, .008f, -.145f) };
        public static readonly float[] StreamerLengths = { .78f, .72f, .44f, .38f };

        public struct Paint
        {
            public Color color; public float irid, emit;
            public Paint(Color color, float irid = 0, float emit = 0) { this.color = color; this.irid = irid; this.emit = emit; }
            public static Paint Lerp(Paint a, Paint b, float t) => new Paint(Color.Lerp(a.color, b.color, t), Mathf.Lerp(a.irid, b.irid, t), Mathf.Lerp(a.emit, b.emit, t));
            // Feather with a pale underside on its back face.
            public Paint TwoTone() { var c = color; c.a = 0; return new Paint(c, irid, emit); }
        }
        sealed class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>(); readonly List<Color> c = new List<Color>(); readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();
            public int Add(Vector3 p, Paint paint) { v.Add(p); c.Add(paint.color); uv.Add(new Vector2(paint.irid, paint.emit)); return v.Count - 1; }
            public void Tri(int a, int b, int d) { t.Add(a); t.Add(b); t.Add(d); }
            public void Quad(int a, int b, int d, int e) { Tri(a, b, d); Tri(a, d, e); }
            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name }; mesh.SetVertices(v); mesh.SetColors(c); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
            }
        }
        // Closed ellipse sections along a path in the bird's YZ plane (x is the side axis), capped at both ends.
        static void Loft(Builder b, Vector3[] centres, Vector2[] radii, int around, Func<Vector3, Vector3, Paint> paint)
        {
            int n = centres.Length, start = b.v.Count; Vector3 first = Vector3.forward, last = Vector3.forward;
            for (int k = 0; k < n; k++)
            {
                var forward = (centres[Mathf.Min(k + 1, n - 1)] - centres[Mathf.Max(k - 1, 0)]).normalized;
                if (k == 0) first = forward;
                if (k == n - 1) last = forward;
                var up = Vector3.Cross(forward, Vector3.right).normalized;
                for (int j = 0; j < around; j++)
                {
                    float a = j * Mathf.PI * 2 / around;
                    var outward = Vector3.right * Mathf.Cos(a) + up * Mathf.Sin(a);
                    b.Add(centres[k] + Vector3.right * Mathf.Cos(a) * radii[k].x + up * Mathf.Sin(a) * radii[k].y, paint(centres[k], outward));
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
                float lat = Mathf.PI * r / rings - Mathf.PI / 2;
                for (int j = 0; j <= around; j++)
                {
                    float lon = Mathf.PI * 2 * j / around;
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
                float a = Mathf.PI * 2 * i / segments; var d = u * Mathf.Cos(a) + w * Mathf.Sin(a);
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
        // A thin pyramid: one bristle of the crest.
        static void Spike(Builder b, Vector3 basePoint, Vector3 dir, float length, float radius, Paint root, Paint tip)
        {
            var u = Vector3.Cross(dir, Mathf.Abs(dir.y) > .9f ? Vector3.right : Vector3.up).normalized; var w = Vector3.Cross(dir, u);
            int a = b.Add(basePoint + u * radius, root);
            int c = b.Add(basePoint + (-u * .5f + w * .866f) * radius, root);
            int d = b.Add(basePoint + (-u * .5f - w * .866f) * radius, root);
            int t = b.Add(basePoint + dir * length, tip);
            b.Tri(a, c, t); b.Tri(c, d, t); b.Tri(d, a, t);
        }
        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

        // Body from the tail base to the neck, with the true tail: dark central feathers and white outer feathers.
        public static Mesh Body()
        {
            var b = new Builder();
            float[] z = { -.170f, -.150f, -.120f, -.085f, -.045f, -.005f, .035f, .070f, .095f, .112f };
            float[] rx = { .016f, .026f, .040f, .054f, .063f, .067f, .064f, .055f, .045f, .036f };
            float[] ry = { .015f, .025f, .040f, .056f, .066f, .071f, .069f, .060f, .048f, .038f };
            float[] cy = { .012f, .010f, .006f, .002f, -.002f, -.003f, .002f, .013f, .026f, .036f };
            Loft(b, Points(z, cy), Radii(rx, ry), 24, (centre, o) =>
            {
                // Crimson belly from the lower breast to the vent; emerald bib above it; white undertail behind.
                float red = Smooth(.05f, -.35f, o.y) * Smooth(.04f, -.01f, centre.z) * Smooth(-.168f, -.13f, centre.z);
                float white = Smooth(-.125f, -.16f, centre.z) * Smooth(.1f, -.3f, o.y);
                var green = Color.Lerp(Color.Lerp(EmeraldDeep, Emerald, Smooth(-.7f, .2f, o.y)), Gold, Smooth(.55f, 1, o.y) * .3f);
                green = Color.Lerp(green, Turquoise, Smooth(.3f, .9f, Mathf.Abs(o.x)) * .35f);
                var p = Paint.Lerp(new Paint(green, 1), new Paint(Crimson, 0, .1f), red);
                return Paint.Lerp(p, new Paint(Undertail), white);
            });
            // Outer tail feathers: white on both faces, spreading and hanging a little.
            for (int i = 0; i < 4; i++)
            {
                float side = i < 2 ? -1 : 1, spread = (i % 2 == 0 ? 14 : 24) * side * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(spread), -.28f, -Mathf.Cos(spread)).normalized;
                Feather(b, new Vector3(side * .006f, .0f - (i % 2) * .002f, -.146f), dir, Vector3.up, i % 2 == 0 ? .16f : .135f, .032f, new Paint(Undertail), new Paint(Color.Lerp(Undertail, FlightUnderside, .4f)), side * .08f);
            }
            // Central tail feathers: blue-black, above the white ones.
            for (int i = -1; i <= 1; i += 2)
                Feather(b, new Vector3(i * .004f, .006f, -.148f), new Vector3(i * .06f, -.16f, -1).normalized, Vector3.up, .17f, .03f, new Paint(TailDark, .4f), new Paint(TailDark, .4f));
            return b.Build("Quetzal body");
        }

        // Head in neck-pivot space: rounded skull under a brush crest of fine bristles that spills forward over the
        // short yellow bill, and a large dark eye set well forward with a catchlight.
        public static Mesh Head()
        {
            var b = new Builder();
            float[] z = { -.030f, -.016f, .002f, .024f, .046f, .064f, .077f, .087f };
            float[] rx = { .014f, .034f, .044f, .047f, .043f, .034f, .023f, .012f };
            float[] ry = { .014f, .036f, .047f, .049f, .044f, .035f, .023f, .012f };
            float[] cy = { .000f, .004f, .008f, .011f, .011f, .008f, .003f, .000f };
            Loft(b, Points(z, cy), Radii(rx, ry), 22, (centre, o) =>
                new Paint(Color.Lerp(Color.Lerp(EmeraldDeep, Emerald, Smooth(-.8f, .1f, o.y)), Gold, Smooth(.5f, 1, o.y) * .3f), 1));
            // Crest base: a rounded cap from the forehead over the crown to the nape.
            var crest = new[] { new Vector3(0, .030f, .080f), new Vector3(0, .054f, .058f), new Vector3(0, .066f, .032f), new Vector3(0, .067f, .008f),
                new Vector3(0, .058f, -.012f), new Vector3(0, .040f, -.026f) };
            var crestRadii = new[] { new Vector2(.018f, .010f), new Vector2(.032f, .016f), new Vector2(.040f, .019f), new Vector2(.041f, .019f),
                new Vector2(.033f, .015f), new Vector2(.020f, .010f) };
            var baseRadii = new Vector2[crestRadii.Length]; for (int i = 0; i < baseRadii.Length; i++) baseRadii[i] = crestRadii[i] * .85f;
            Loft(b, crest, baseRadii, 18, (centre, o) => new Paint(Color.Lerp(EmeraldDeep, Emerald, .6f), 1));
            // Bristles: rows along the crest, fanning sideways; the front rows lean forward over the bill, the rear ones back.
            var random = new System.Random(2026);
            float Rand() => (float)random.NextDouble();
            const int rows = 26;
            for (int r = 0; r <= rows; r++)
            {
                float s = r / (float)rows, f = s * (crest.Length - 1); int k = Mathf.Min((int)f, crest.Length - 2); float t = f - k;
                var centre = Vector3.Lerp(crest[k], crest[k + 1], t); var radii = Vector2.Lerp(crestRadii[k], crestRadii[k + 1], t);
                int count = 9 + Mathf.RoundToInt(radii.x / .04f * 19);
                for (int j = 0; j < count; j++)
                {
                    float a = Mathf.Lerp(-104, 104, (j + .5f * (r % 2)) / (count - .5f)) * Mathf.Deg2Rad;
                    var basePoint = centre + new Vector3(Mathf.Sin(a) * radii.x * .86f, Mathf.Cos(a) * radii.y * .86f, 0);
                    var outward = new Vector3(Mathf.Sin(a) / radii.x, Mathf.Cos(a) / radii.y, 0).normalized;
                    float lean = Mathf.Lerp(1.15f, -.65f, s);
                    var dir = (outward + new Vector3(0, 0, lean) + new Vector3(Rand() - .5f, Rand() - .5f, Rand() - .5f) * .35f).normalized;
                    float crown = Mathf.Sin(Mathf.PI * Mathf.Lerp(.18f, .95f, s));
                    float length = (.009f + .019f * Mathf.Pow(Mathf.Max(Mathf.Cos(a), .05f), .7f) * crown) * (.8f + .4f * Rand());
                    Spike(b, basePoint, dir, length, .0015f + .0006f * Rand(), new Paint(EmeraldDeep, .8f), new Paint(Color.Lerp(Color.Lerp(Emerald, Gold, .42f), CrestTip, Rand() * .3f), .7f, .02f));
                }
            }
            // Forehead bristles over the base of the bill.
            for (int i = 0; i < 12; i++)
            {
                float x = Mathf.Lerp(-.013f, .013f, (i % 6) / 5f), y = i < 6 ? .012f : .024f;
                var dir = new Vector3(x * 6 + (Rand() - .5f) * .2f, -.15f + (Rand() - .5f) * .2f, 1).normalized;
                Spike(b, new Vector3(x, y, .080f), dir, .014f + .006f * Rand(), .0028f, new Paint(EmeraldDeep, .8f), new Paint(CrestTip, .5f));
            }
            // Bill: short and stout, slightly hooked, mostly hidden by the bristles.
            var bill = new[] { new Vector3(0, .004f, .080f), new Vector3(0, .001f, .096f), new Vector3(0, -.002f, .106f), new Vector3(0, -.006f, .113f), new Vector3(0, -.010f, .117f) };
            var billRadii = new[] { new Vector2(.010f, .009f), new Vector2(.0085f, .0078f), new Vector2(.0062f, .0058f), new Vector2(.0035f, .0035f), new Vector2(.001f, .001f) };
            Loft(b, bill, billRadii, 12, (centre, o) => new Paint(Color.Lerp(BillYellow, new Color(1, .93f, .62f), Smooth(.1f, .117f, centre.z)), 0, .06f));
            for (int s = -1; s <= 1; s += 2)
            {
                var eye = new Vector3(s * .031f, .017f, .060f);
                Ellipsoid(b, eye, Vector3.one * .0128f, 8, 12, new Paint(EyeDark));
                Ellipsoid(b, eye + new Vector3(s * .0108f, .0052f, .0055f), Vector3.one * .0027f, 4, 6, new Paint(Color.white, 0, 1));
                Annulus(b, eye + new Vector3(s * .0075f, 0, 0), new Vector3(s, 0, 0), .0128f, .0141f, 18, new Paint(Color.Lerp(EmeraldDeep, Glow, .25f), 0, .2f));
            }
            return b.Build("Quetzal head");
        }

        static readonly Paint FlightVane = new Paint(FlightDark, .15f).TwoTone(), FlightTip = Darker(new Paint(Color.Lerp(FlightDark, Glow, .1f), .1f, .06f).TwoTone(), .55f);
        static Paint Darker(Paint p, float undersideShade) { p.color.a = undersideShade; return p; }
        static Paint CovertPaint(float u) => new Paint(Color.Lerp(Color.Lerp(Emerald, Gold, .2f), Turquoise, u * .4f), 1);
        static Paint CovertTip(float u) => new Paint(Color.Lerp(Turquoise, Gold, .25f + .2f * u), 1, .05f);
        // Inner wing (shoulder to wrist) along +x for side = 1 and -x for side = -1: emerald lesser coverts at the
        // leading edge, seven dark secondaries, and the long pointed greater coverts sweeping back over them.
        public static Mesh Arm(int side)
        {
            var b = new Builder(); const int steps = 6;
            for (int i = 0; i <= steps; i++)
            {
                float u = i / (float)steps, x = u * ArmLength * side, camber = .006f * Mathf.Sin(Mathf.PI * u);
                b.Add(new Vector3(x, camber + .002f, .028f - .01f * u), CovertPaint(u));
                b.Add(new Vector3(x, camber + .005f, .006f - .004f * u), CovertPaint(u));
            }
            for (int i = 0; i < steps; i++) { int a = i * 2; b.Quad(a, a + 2, a + 3, a + 1); }
            for (int i = 0; i < 7; i++)
            {
                float u = (i + .5f) / 7; var root = new Vector3(u * ArmLength * side, -.002f - i * .0006f, .0f);
                var dir = new Vector3(.12f * side * u, -.03f, -1).normalized;
                Feather(b, root, dir, Vector3.up, .088f - .008f * u, .026f, Darker(FlightVane, i % 2 * .22f), FlightTip);
            }
            for (int i = 0; i < 6; i++)
            {
                float u = (i + .5f) / 6; var root = new Vector3(u * ArmLength * side * .95f, .004f + i * .0006f, .014f);
                var dir = new Vector3(.2f * side * u + .04f * side, .01f, -1).normalized;
                Feather(b, root, dir, Vector3.up, .07f + .03f * u, .024f, CovertPaint(u), CovertTip(u), .12f * side);
            }
            return b.Build(side < 0 ? "Quetzal left arm" : "Quetzal right arm");
        }
        // Outer wing (wrist to tip): emerald primary coverts over eight dark primaries fanning from back to outward.
        public static Mesh Hand(int side)
        {
            var b = new Builder();
            for (int j = 0; j < 8; j++)
            {
                float u = j / 7f, angle = Mathf.Lerp(10, 84, u) * Mathf.Deg2Rad;
                var root = new Vector3(Mathf.Lerp(.006f, HandLength, u) * side, -.002f - j * .0006f, -.002f);
                var dir = new Vector3(Mathf.Sin(angle) * side, -.02f, -Mathf.Cos(angle)).normalized;
                float length = Mathf.Lerp(.095f, .13f, Mathf.Sin(u * Mathf.PI * .8f));
                Feather(b, root, dir, Vector3.up, length, Mathf.Lerp(.024f, .018f, u), Darker(FlightVane, j % 2 * .22f), FlightTip, -.05f * side);
            }
            for (int j = 0; j < 4; j++)
            {
                float u = j / 3f, angle = Mathf.Lerp(18, 62, u) * Mathf.Deg2Rad;
                var root = new Vector3(Mathf.Lerp(0, HandLength * .8f, u) * side, .004f + j * .0006f, .012f);
                var dir = new Vector3(Mathf.Sin(angle) * side, 0, -Mathf.Cos(angle)).normalized;
                Feather(b, root, dir, Vector3.up, .05f, .022f, CovertPaint(.5f + .5f * u), CovertTip(u));
            }
            return b.Build(side < 0 ? "Quetzal left hand" : "Quetzal right hand");
        }

        // Streamer ribbons: fixed topology (left edge, raised shaft, right edge per point), positions rebuilt every frame.
        public const int StreamerPoints = 20;
        public static Mesh Streamer(string name)
        {
            var b = new Builder();
            for (int i = 0; i < StreamerPoints; i++)
            {
                float u = i / (StreamerPoints - 1f);
                var vane = new Paint(Color.Lerp(Emerald, Turquoise, .2f + .5f * u), 1);
                b.Add(Vector3.zero, vane);
                b.Add(Vector3.zero, new Paint(Color.Lerp(EmeraldDeep, Emerald, .4f), 1));
                b.Add(Vector3.zero, vane);
            }
            for (int i = 0; i < StreamerPoints - 1; i++) for (int j = 0; j < 2; j++) { int a = i * 3 + j; b.Quad(a, a + 3, a + 4, a + 1); }
            var mesh = b.Build(name); mesh.MarkDynamic(); return mesh;
        }
        // Lanceolate vane: a thin shaft at the root that widens along the feather and ends in a rounded point.
        public static float StreamerWidth(float u) => u < .12f ? Mathf.Lerp(.003f, .009f, u / .12f) : u < .7f ? Mathf.Lerp(.009f, .013f, (u - .12f) / .58f)
            : u < .9f ? Mathf.Lerp(.013f, .016f, (u - .7f) / .2f) : Mathf.Lerp(.016f, .001f, (u - .9f) / .1f);
    }
}
