using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GuateGeeks.AwsVr
{
    // Builds a solid 3D emblem for each AWS service from the official icon geometry (AwsIconGeometry.Data.cs):
    // a bevelled tile in the official category colour with the white service symbol raised in relief on both faces.
    // The back face is mirrored so the symbol never reads backwards. One shared mesh per service, one draw call per object.
    public static partial class AwsIconGeometry
    {
        public sealed class Shape
        {
            public readonly float[] Points; public readonly int[] Triangles, Loops; public readonly Color Background;
            public Shape(float[] points, int[] triangles, int[] loops, string background)
            {
                Points = points; Triangles = triangles; Loops = loops;
                ColorUtility.TryParseHtmlString(background, out Background);
            }
            public int PointCount => Points.Length / 2;
            public Vector2 Point(int i) => new Vector2(Points[i * 2], Points[i * 2 + 1]);
        }
        // Metres at scale 1: a 30 cm tile, 7 cm thick, 1.2 cm bevel, symbol raised 9 mm.
        public const float Size = .30f, Depth = .07f, Bevel = .012f, Relief = .009f;
        static readonly Dictionary<ServiceKind, Mesh> meshes = new Dictionary<ServiceKind, Mesh>();
        public static Shape Get(ServiceKind kind) => For(kind);
        public static Color Background(ServiceKind kind) => For(kind)?.Background ?? Color.gray;

        public static Mesh Mesh(ServiceKind kind)
        {
            if (meshes.TryGetValue(kind, out var mesh) && mesh) return mesh;
            var shape = For(kind); if (shape == null) return null;
            mesh = Build(shape); mesh.name = "AWS emblem · " + kind; meshes[kind] = mesh; return mesh;
        }
        public static void Release() { foreach (var m in meshes.Values) if (m) Object.Destroy(m); meshes.Clear(); }

        sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>(); public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Color> C = new List<Color>(); public readonly List<int> T = new List<int>();
            public int Add(Vector3 p, Vector3 n, Color c) { V.Add(p); N.Add(n); C.Add(c); return V.Count - 1; }
            // Unity front faces: Cross(b-a, c-a) points toward the viewer, i.e. along the intended normal.
            public void Tri(int a, int b, int c, Vector3 n)
            {
                if (Vector3.Dot(Vector3.Cross(V[b] - V[a], V[c] - V[a]), n) < 0) { int s = b; b = c; c = s; }
                T.Add(a); T.Add(b); T.Add(c);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color col)
            {
                int i = Add(a, n, col), j = Add(b, n, col), k = Add(c, n, col), l = Add(d, n, col);
                Tri(i, j, k, n); Tri(i, k, l, n);
            }
        }
        static Color Linear(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        static Mesh Build(Shape shape)
        {
            var b = new Builder();
            // Vertex alpha marks emission: tile 0 (fully lit), symbol face 1 (self-lit white), symbol walls partly lit.
            Color tile = Linear(shape.Background); tile.a = 0;
            Color face = new Color(1, 1, 1, 1), wall = new Color(.92f, .96f, 1, .45f);
            float h = Size / 2, d = Depth / 2;
            // Bevelled tile from four rectangular rings: front inset, front edge, back edge, back inset.
            float[] ringZ = { -d, -d + Bevel, d - Bevel, d }, ringHalf = { h - Bevel, h, h, h - Bevel };
            Vector3 R(int ring, int corner)
            {
                float s = ringHalf[ring]; int k = corner % 4;
                return new Vector3(k == 0 || k == 3 ? -s : s, k < 2 ? -s : s, ringZ[ring]);
            }
            b.Quad(R(0, 0), R(0, 1), R(0, 2), R(0, 3), Vector3.back, tile);
            b.Quad(R(3, 0), R(3, 1), R(3, 2), R(3, 3), Vector3.forward, tile);
            for (int ring = 0; ring < 3; ring++)
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p0 = R(ring, k), p1 = R(ring, k + 1), p2 = R(ring + 1, k + 1), p3 = R(ring + 1, k);
                    var n = Vector3.Cross(p1 - p0, p3 - p0).normalized; var mid = (p0 + p1 + p2 + p3) / 4;
                    if (Vector3.Dot(n, mid) < 0) n = -n;
                    // Bevels darken slightly so the tile edge reads as a machined chamfer.
                    var col = ring == 1 ? tile : tile * .82f; col.a = 0;
                    b.Quad(p0, p1, p2, p3, n, col);
                }
            Glyph(b, shape, false, face, wall);
            Glyph(b, shape, true, face, wall);
            var mesh = new Mesh { indexFormat = b.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(b.V); mesh.SetNormals(b.N); mesh.SetColors(b.C); mesh.SetTriangles(b.T, 0); mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }
        static void Glyph(Builder b, Shape shape, bool back, Color face, Color wall)
        {
            float d = Depth / 2, top = back ? d + Relief : -d - Relief, bottom = back ? d : -d;
            float mirror = back ? -1 : 1; var outward = back ? Vector3.forward : Vector3.back;
            Vector3 P(Vector2 p, float z) => new Vector3(p.x * Size * mirror, p.y * Size, z);
            int start = b.V.Count;
            for (int i = 0; i < shape.PointCount; i++) b.Add(P(shape.Point(i), top), outward, face);
            for (int i = 0; i < shape.Triangles.Length; i += 3)
                b.Tri(start + shape.Triangles[i], start + shape.Triangles[i + 1], start + shape.Triangles[i + 2], outward);
            // Side walls: material is on the left of each loop's walking direction, so the outward normal is to the right.
            for (int l = 0; l < shape.Loops.Length; l += 3)
            {
                int first = shape.Loops[l], count = shape.Loops[l + 1], dir = shape.Loops[l + 2];
                var pts = new Vector2[count];
                for (int i = 0; i < count; i++) pts[i] = shape.Point(first + (dir > 0 ? i : count - 1 - i));
                var edge = new Vector2[count];
                for (int i = 0; i < count; i++) { var e = pts[(i + 1) % count] - pts[i]; edge[i] = new Vector2(e.y, -e.x).normalized; }
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;
                    Vector2 n0 = Smooth(edge[(i - 1 + count) % count], edge[i], edge[i]), n1 = Smooth(edge[i], edge[j], edge[i]);
                    Vector3 N(Vector2 n) => new Vector3(n.x * mirror, n.y, 0);
                    Vector3 a = P(pts[i], bottom), c = P(pts[j], top);
                    int ia = b.Add(a, N(n0), wall), ib = b.Add(P(pts[j], bottom), N(n1), wall), ic = b.Add(c, N(n1), wall), id = b.Add(P(pts[i], top), N(n0), wall);
                    var faceNormal = N(edge[i]);
                    b.Tri(ia, ib, ic, faceNormal); b.Tri(ia, ic, id, faceNormal);
                }
            }
        }
        // Curves shade smoothly; corners sharper than ~40° keep a crisp edge.
        static Vector2 Smooth(Vector2 a, Vector2 b, Vector2 own) => Vector2.Dot(a, b) > .76f ? (a + b).normalized : own;
    }

    // A solid service emblem that floats inside the projection, turns to face the viewer and sways slightly
    // so the relief catches the light. Reduced motion removes the sway. It has no collider (the node owns interaction).
    public sealed class AwsServiceEmblem : MonoBehaviour
    {
        public ServiceKind Kind { get; private set; }
        float yaw, phase; bool placed;
        public static AwsServiceEmblem Create(Transform parent, ServiceKind kind, float scale = 1)
        {
            var go = new GameObject("AWS emblem · " + kind, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false); go.transform.localScale = Vector3.one * scale;
            go.GetComponent<MeshFilter>().sharedMesh = AwsIconGeometry.Mesh(kind);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = LabVisuals.SpecialMaterial("LabEmblem", Color.white);
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            var emblem = go.AddComponent<AwsServiceEmblem>(); emblem.Kind = kind; emblem.phase = (int)kind * 1.7f;
            return emblem;
        }
        void LateUpdate()
        {
            var camera = Camera.main; if (!camera) return;
            var toward = camera.transform.position - transform.position; toward.y = 0;
            if (toward.sqrMagnitude < .0001f) return;
            // The symbol's front is -Z, so +Z points away from the viewer.
            float target = Mathf.Atan2(-toward.x, -toward.z) * Mathf.Rad2Deg;
            yaw = placed ? Mathf.LerpAngle(yaw, target, 1 - Mathf.Exp(-5 * Time.unscaledDeltaTime)) : target; placed = true;
            bool reduced = LabFeedback.Current && LabFeedback.Current.ReducedMotion;
            float sway = reduced ? 0 : Mathf.Sin(LabFeedback.Clock * .55f + phase) * 13;
            transform.rotation = Quaternion.Euler(0, yaw + sway, 0);
        }
    }
}
