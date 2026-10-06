using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GuateGeeks.AwsVr
{
    public sealed class HoloGeometry : MonoBehaviour
    {
        Mesh ownedMesh;
        // All strokes share one generated mesh and material; no edge-per-object hierarchy.
        public static GameObject Strokes(Transform parent, string name, List<Vector3> pairs, Color color, float radius = .004f)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int i = 0; i + 1 < pairs.Count; i += 2)
            {
                Vector3 a = pairs[i], b = pairs[i + 1], axis = (b - a).normalized;
                Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > .9f ? Vector3.right : Vector3.up).normalized;
                Vector3 other = Vector3.Cross(axis, side); int start = vertices.Count;
                for (int j = 0; j < 6; j++)
                {
                    float angle = j * Mathf.PI / 3;
                    Vector3 offset = radius * (side * Mathf.Cos(angle) + other * Mathf.Sin(angle));
                    vertices.Add(a + offset); vertices.Add(b + offset);
                }
                for (int j = 0; j < 6; j++)
                {
                    int n = (j + 1) % 6;
                    triangles.Add(start + j * 2); triangles.Add(start + n * 2); triangles.Add(start + j * 2 + 1);
                    triangles.Add(start + j * 2 + 1); triangles.Add(start + n * 2); triangles.Add(start + n * 2 + 1);
                }
            }
            var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer), typeof(HoloGeometry));
            go.transform.SetParent(parent, false); go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = LabVisuals.Material(color);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            go.GetComponent<HoloGeometry>().ownedMesh = mesh; return go;
        }
        public static void Path(List<Vector3> pairs, params Vector3[] path)
        { for (int i = 1; i < path.Length; i++) { pairs.Add(path[i - 1]); pairs.Add(path[i]); } }
        public static void CombineMetal(Transform root, params Transform[] exclude)
        {
            var groups = new Dictionary<Material, List<CombineInstance>>();
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>()) {
                bool excluded=false; foreach(var branch in exclude) if(branch && filter.transform.IsChildOf(branch)) excluded=true;
                if(excluded) continue;
                var renderer=filter.GetComponent<MeshRenderer>();
                if(!renderer || !renderer.enabled || renderer.sharedMaterial.shader.name!="GuateGeeks/LabMetal") continue;
                if(!groups.TryGetValue(renderer.sharedMaterial,out var parts)) groups[renderer.sharedMaterial]=parts=new List<CombineInstance>();
                parts.Add(new CombineInstance { mesh=filter.sharedMesh, transform=root.worldToLocalMatrix*filter.transform.localToWorldMatrix });
                renderer.enabled=false;
            }
            foreach(var group in groups) {
                var mesh=new Mesh { name="Batched titanium architecture", indexFormat=IndexFormat.UInt32 };
                mesh.CombineMeshes(group.Value.ToArray(),true,true);
                var go=new GameObject(mesh.name,typeof(MeshFilter),typeof(MeshRenderer),typeof(HoloGeometry)); go.transform.SetParent(root,false);
                go.GetComponent<MeshFilter>().sharedMesh=mesh; go.GetComponent<MeshRenderer>().sharedMaterial=group.Key;
                go.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off; go.GetComponent<MeshRenderer>().receiveShadows=false;
                go.GetComponent<HoloGeometry>().ownedMesh=mesh;
            }
        }
        public static void Circle(List<Vector3> pairs, Vector3 center, float radius, int segments = 48, bool vertical = false, float start = 0, float span = 360)
        {
            var path = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float a = (start + span * i / segments) * Mathf.Deg2Rad;
                path[i] = center + (vertical ? new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) : new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))) * radius;
            }
            Path(pairs, path);
        }
        public static GameObject BoxFrame(Transform parent, Vector3 size, Color color)
        {
            var p = new List<Vector3>(); Vector3 h = size / 2;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2) Path(p, new Vector3(x*h.x,y*h.y,-h.z), new Vector3(x*h.x,y*h.y,h.z));
            for (int z = -1; z <= 1; z += 2)
                Path(p, new Vector3(-h.x,-h.y,z*h.z), new Vector3(h.x,-h.y,z*h.z), new Vector3(h.x,h.y,z*h.z), new Vector3(-h.x,h.y,z*h.z), new Vector3(-h.x,-h.y,z*h.z));
            return Strokes(parent,"Luminous edges",p,color);
        }
        public static GameObject Glyph(Transform parent, ServiceKind kind, Color color)
        {
            var p=new List<Vector3>();
            Vector3 V(float x,float y)=>new Vector3(x,y,-.19f);
            void P(params Vector3[] path)=>Path(p,path);
            switch(kind)
            {
                case ServiceKind.Lambda:
                    P(V(-.08f,.13f),V(-.035f,.13f),V(.06f,-.12f),V(.105f,-.12f)); P(V(-.015f,.055f),V(-.11f,-.12f)); break;
                case ServiceKind.ApiGateway:
                    P(V(-.08f,.12f),V(-.14f,.12f),V(-.14f,-.12f),V(-.08f,-.12f));
                    P(V(.08f,.12f),V(.14f,.12f),V(.14f,-.12f),V(.08f,-.12f));
                    P(V(-.09f,0),V(.09f,0)); P(V(.04f,.05f),V(.09f,0),V(.04f,-.05f)); break;
                case ServiceKind.S3:
                    P(V(-.13f,.11f),V(-.09f,-.12f),V(.09f,-.12f),V(.13f,.11f),V(-.13f,.11f));
                    P(V(-.11f,.04f),V(.11f,.04f)); P(V(0,.005f),V(.04f,-.04f),V(0,-.085f),V(-.04f,-.04f),V(0,.005f)); break;
                case ServiceKind.DynamoDB:
                    for(int i=-1;i<=1;i++) P(V(-.105f,i*.07f+.016f),V(.105f,i*.07f+.016f));
                    P(V(-.105f,-.105f),V(-.105f,.105f)); P(V(.105f,-.105f),V(.105f,.105f)); break;
                case ServiceKind.SQS:
                    for(int i=-1;i<=1;i++) P(V(i*.095f-.033f,-.08f),V(i*.095f+.033f,-.08f),V(i*.095f+.033f,.08f),V(i*.095f-.033f,.08f),V(i*.095f-.033f,-.08f)); break;
                case ServiceKind.EventBridge:
                    P(V(0,-.13f),V(0,-.02f),V(-.12f,.075f)); P(V(0,-.02f),V(.12f,.075f)); P(V(0,-.02f),V(0,.13f));
                    Circle(p,V(-.12f,.085f),.023f,12,true); Circle(p,V(.12f,.085f),.023f,12,true); Circle(p,V(0,.13f),.023f,12,true); break;
                case ServiceKind.CloudWatch:
                    P(V(-.13f,-.11f),V(-.13f,.1f)); P(V(-.13f,-.11f),V(.13f,-.11f));
                    P(V(-.1f,-.025f),V(-.05f,.035f),V(.005f,.005f),V(.055f,.09f),V(.13f,.12f)); break;
            }
            return Strokes(parent,"Crafted service symbol",p,color,.009f);
        }
        void OnDestroy() { if(ownedMesh) Destroy(ownedMesh); }
    }
}
