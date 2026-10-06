using System.Collections.Generic;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    // The workshop ceiling. A hexagonal light canopy spans the arches all the way round, and at the crown hangs the
    // holo-projector rig that "feeds" the table: a titanium hub on eight struts, a ring of emitter apertures, a lens
    // with a soft downward glow, three precessing gyroscope rings and a projection shaft that fades out by mid-room.
    // Static metal is batched with the rest of the architecture; all motion is decorative and follows reduced motion.
    public sealed class HoloCeiling : MonoBehaviour
    {
        // Arch profile shared with HoloEnvironment, as (radius from the table axis, height): pillar head, control, crown.
        public static readonly Vector2 ArchStart = new Vector2(6.8f, 4.9f), ArchControl = new Vector2(5.1f, 6.35f), ArchEnd = new Vector2(2.3f, 5.65f);
        public const float HubRadius = 1.05f;
        public const string CanopyName = "Holographic ceiling canopy", HubName = "Projector hub housing";
        sealed class Gyro { public Transform pivot; public Vector3 tilt; public float speed, precession; }
        readonly List<Gyro> gyros = new List<Gyro>();
        readonly List<Object> owned = new List<Object>();
        Transform lens; Vector3 lensScale;

        public static HoloCeiling Build(Transform world, Vector3 crownCenter)
        {
            var root = new GameObject("Workshop ceiling").transform; root.SetParent(world, false);
            var ceiling = root.gameObject.AddComponent<HoloCeiling>();
            ceiling.BuildCanopy(root, crownCenter);
            ceiling.BuildHub(root, crownCenter);
            return ceiling;
        }
        public static Vector2 Arch(float t) { float u = 1 - t; return u * u * ArchStart + 2 * u * t * ArchControl + t * t * ArchEnd; }

        // Surface of revolution following the arches (just above them) and then inward over the struts to the hub.
        void BuildCanopy(Transform root, Vector3 c)
        {
            const int around = 128, overArch = 16, inward = 6, along = overArch + inward; const float lift = .14f;
            var profile = new Vector2[along + 1];
            for (int k = 0; k <= overArch; k++) profile[k] = Arch(k / (float)overArch) + Vector2.up * lift;
            for (int k = 1; k <= inward; k++) profile[overArch + k] = Vector2.Lerp(ArchEnd + Vector2.up * lift, new Vector2(HubRadius + .04f, c.y + .2f), k / (float)inward);
            var vertices = new Vector3[(along + 1) * (around + 1)]; var uvs = new Vector2[vertices.Length]; var triangles = new int[along * around * 6];
            for (int k = 0; k <= along; k++)
                for (int i = 0; i <= around; i++)
                {
                    float a = i * Mathf.PI * 2 / around; int v = k * (around + 1) + i;
                    vertices[v] = new Vector3(Mathf.Sin(a) * profile[k].x + c.x, profile[k].y, Mathf.Cos(a) * profile[k].x + c.z);
                    uvs[v] = new Vector2(i / (float)around, k / (float)along);
                }
            int n = 0;
            for (int k = 0; k < along; k++)
                for (int i = 0; i < around; i++)
                {
                    int v = k * (around + 1) + i, w = v + around + 1;
                    triangles[n++] = v; triangles[n++] = w; triangles[n++] = v + 1; triangles[n++] = v + 1; triangles[n++] = w; triangles[n++] = w + 1;
                }
            var mesh = new Mesh { name = CanopyName, vertices = vertices, uv = uvs, triangles = triangles }; mesh.RecalculateBounds(); owned.Add(mesh);
            var go = new GameObject(CanopyName, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var material = new Material(Resources.Load<Shader>("LabCanopy")) { name = CanopyName, color = Color.Lerp(EventBranding.Quetzal, Cyan, .35f) };
            material.SetVector("_Cells", new Vector4(72, 13, 0, 0)); owned.Add(material);
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        void BuildHub(Transform root, Vector3 c)
        {
            Color steel = Hex("#1B2B36"), dark = Hex("#0A131B"), graphite = Hex("#141F28");
            Metal(root, HubName, PrimitiveType.Cylinder, c + new Vector3(0, .22f, 0), new Vector3(HubRadius * 2, .09f, HubRadius * 2), graphite);
            Metal(root, "Projector hub cap", PrimitiveType.Cylinder, c + new Vector3(0, .34f, 0), new Vector3(1.3f, .04f, 1.3f), steel);
            Metal(root, "Projector collar", PrimitiveType.Cylinder, c + new Vector3(0, .09f, 0), new Vector3(1.5f, .05f, 1.5f), steel);
            Metal(root, "Emitter barrel", PrimitiveType.Cylinder, c + new Vector3(0, -.02f, 0), new Vector3(.66f, .07f, .66f), dark);
            // Eight struts tie the hub to the crown ring; energy runs along their undersides into the hub.
            var seams = new List<Vector3>();
            for (int i = 0; i < 8; i++)
            {
                float a = (i * 45 + 22.5f) * Mathf.Deg2Rad; var radial = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                Vector3 inner = c + radial * (HubRadius - .05f) + Vector3.up * .2f, outer = c + radial * ArchEnd.x;
                var strut = Metal(root, "Projector strut", PrimitiveType.Cube, (inner + outer) / 2, new Vector3(.08f, .07f, (outer - inner).magnitude), steel);
                strut.transform.localRotation = Quaternion.LookRotation(outer - inner, Vector3.up);
                HoloGeometry.Path(seams, outer + Vector3.down * .045f, inner + Vector3.down * .045f);
                var feed = Line(root, "Projector strut feed", new[] { outer + Vector3.down * .05f, inner + Vector3.down * .05f }, Cyan, .014f);
                feed.sharedMaterial = Beam(i % 2 == 0 ? Cyan : EventBranding.Quetzal); feed.textureMode = LineTextureMode.Stretch; feed.numCapVertices = 0;
            }
            HoloGeometry.Strokes(root, "Projector strut seams", seams, Hex("#2A6F82"), .006f);
            // Emitter apertures around the barrel, and bezel rings on the hub edge.
            var apertures = new List<Vector3>(); var accents = new List<Vector3>();
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30 * Mathf.Deg2Rad; var p = c + new Vector3(Mathf.Sin(a) * .6f, .03f, Mathf.Cos(a) * .6f);
                HoloGeometry.Circle(i % 3 == 0 ? accents : apertures, p, .045f, 14);
                HoloGeometry.Circle(apertures, p, .022f, 10);
            }
            HoloGeometry.Circle(apertures, c + Vector3.up * .125f, HubRadius + .005f, 96);
            HoloGeometry.Circle(apertures, c + Vector3.up * .03f, .76f, 72);
            HoloGeometry.Strokes(root, "Emitter apertures", apertures, Cyan, .005f);
            HoloGeometry.Strokes(root, "Emitter accents", accents, Orange, .005f);
            // The lens: a bright hologram core with a soft glow facing the table.
            var core = Shape(root, "Projector lens", PrimitiveType.Sphere, c + new Vector3(0, -.1f, 0), new Vector3(.5f, .12f, .5f), Cyan);
            core.GetComponent<Renderer>().sharedMaterial = SpecialMaterial("LabHologram", Cyan);
            lens = core.transform; lensScale = lens.localScale;
            var glow = Shape(root, "Projector lens glow", PrimitiveType.Quad, c + new Vector3(0, -.17f, 0), new Vector3(1.5f, 1.5f, 1), Cyan);
            glow.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var glowColor = Cyan; glowColor.a = .5f; glow.GetComponent<Renderer>().sharedMaterial = SpecialMaterial("LabGlow", glowColor);
            // Gyroscope rings under the hub, each turning and slowly precessing.
            AddGyro(root, c + Vector3.down * .26f, 1.30f, 3, 96, Cyan, .011f, new Vector3(5, 0, 0), 9, 4);
            AddGyro(root, c + Vector3.down * .36f, 1.52f, 6, 34, EventBranding.Quetzal, .009f, new Vector3(0, 0, -6), -6, -3);
            AddGyro(root, c + Vector3.down * .18f, 1.14f, 2, 140, Orange, .006f, new Vector3(-3, 0, 4), 14, 5);
            BuildShaft(root, c + Vector3.down * .12f);
        }
        void AddGyro(Transform root, Vector3 center, float radius, int arcs, float span, Color color, float width, Vector3 tilt, float speed, float precession)
        {
            var pairs = new List<Vector3>();
            for (int i = 0; i < arcs; i++) HoloGeometry.Circle(pairs, Vector3.zero, radius, Mathf.Max(6, (int)(span / 4)), false, i * 360f / arcs, span);
            // Bezel ticks every 15° like an instrument ring.
            for (int i = 0; i < 24; i++)
            {
                float a = i * 15 * Mathf.Deg2Rad; var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                HoloGeometry.Path(pairs, dir * radius, dir * (radius + (i % 6 == 0 ? .09f : .045f)));
            }
            var go = HoloGeometry.Strokes(root, "Projector gyroscope ring", pairs, color, width);
            go.transform.localPosition = center;
            gyros.Add(new Gyro { pivot = go.transform, tilt = tilt, speed = speed, precession = precession });
        }
        // A cone from the lens to the table rim; the shader keeps it visible only near the ceiling.
        void BuildShaft(Transform root, Vector3 top)
        {
            const int segments = 48; const float topRadius = .24f, bottomRadius = 1.75f, bottomY = .76f;
            float height = top.y - bottomY;
            var vertices = new Vector3[(segments + 1) * 2]; var normals = new Vector3[vertices.Length]; var uvs = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments; var radial = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var normal = (radial * height + Vector3.up * (bottomRadius - topRadius)).normalized;
                vertices[i * 2] = top + radial * topRadius; vertices[i * 2 + 1] = new Vector3(top.x, bottomY, top.z) + radial * bottomRadius;
                normals[i * 2] = normals[i * 2 + 1] = normal;
                uvs[i * 2] = new Vector2(i / (float)segments, 0); uvs[i * 2 + 1] = new Vector2(i / (float)segments, 1);
                if (i == segments) break;
                int k = i * 6, v = i * 2;
                triangles[k] = v; triangles[k + 1] = v + 1; triangles[k + 2] = v + 2; triangles[k + 3] = v + 2; triangles[k + 4] = v + 1; triangles[k + 5] = v + 3;
            }
            var mesh = new Mesh { name = "Projection shaft", vertices = vertices, normals = normals, uv = uvs, triangles = triangles }; mesh.RecalculateBounds(); owned.Add(mesh);
            var go = new GameObject("Projection shaft", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var material = new Material(Resources.Load<Shader>("LabShaft")) { name = "Projection shaft", color = new Color(.03f, .22f, .25f) }; owned.Add(material);
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        }
        void Update()
        {
            float t = LabFeedback.Clock;
            foreach (var g in gyros)
                g.pivot.localRotation = Quaternion.Euler(0, t * g.precession, 0) * Quaternion.Euler(g.tilt) * Quaternion.Euler(0, t * g.speed, 0);
            if (lens) lens.localScale = Vector3.Scale(lensScale, new Vector3(1 + .04f * Mathf.Sin(t * 1.3f), 1 + .18f * Mathf.Sin(t * 1.3f), 1 + .04f * Mathf.Sin(t * 1.3f)));
        }
        void OnDestroy() { foreach (var o in owned) if (o) Destroy(o); owned.Clear(); }
    }
}
