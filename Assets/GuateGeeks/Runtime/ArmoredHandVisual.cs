using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace GuateGeeks.AwsVr
{
    // One dynamic solid mesh per hand. Joint poses remain in XR tracking-origin space.
    public sealed class ArmoredHandVisual : MonoBehaviour
    {
        const int Sides = 10, Rings = 7, Parts = 22;
        readonly Vector3[] vertices = new Vector3[Parts * Sides * Rings];
        readonly Vector3[] normals = new Vector3[Parts * Sides * Rings];
        readonly Vector3[] joints = new Vector3[26];
        readonly bool[] valid = new bool[26];
        Mesh mesh;
        MeshRenderer surface;
        Transform beacon;
        static readonly XRHandJointID[][] digits = {
            new[] { XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip },
            new[] { XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip },
            new[] { XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip },
            new[] { XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip },
            new[] { XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip }
        };
        public void Initialize(bool left)
        {
            mesh = new Mesh { name = "Articulated armored glove" }; mesh.MarkDynamic();
            var triangles = new List<int>();
            for(int p=0;p<Parts;p++) for(int r=0;r<Rings-1;r++) for(int s=0;s<Sides;s++) {
                int a=p*Sides*Rings+r*Sides+s, b=p*Sides*Rings+r*Sides+(s+1)%Sides;
                triangles.Add(a); triangles.Add(b); triangles.Add(a+Sides);
                triangles.Add(b); triangles.Add(b+Sides); triangles.Add(a+Sides);
            }
            mesh.vertices=vertices; mesh.normals=normals; mesh.SetTriangles(triangles,0);
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            surface=gameObject.AddComponent<MeshRenderer>(); surface.shadowCastingMode=ShadowCastingMode.Off; surface.receiveShadows=false;
            surface.sharedMaterial=LabVisuals.SpecialMaterial("LabMetal",LabVisuals.Hex("#7894A3"));
            beacon=LabVisuals.Shape(transform,"Glove dorsal emitter",PrimitiveType.Sphere,Vector3.zero,new Vector3(.021f,.006f,.021f),left?LabVisuals.Cyan:LabVisuals.Orange).transform;
            surface.enabled=false; beacon.gameObject.SetActive(false);
        }
        public void UpdateHand(XRHand hand)
        {
            for(int i=0;i<26;i++) { valid[i]=hand.isTracked && hand.GetJoint(XRHandJointIDUtility.FromIndex(i)).TryGetPose(out _); }
            for(int i=0;i<26;i++) if(valid[i]) { hand.GetJoint(XRHandJointIDUtility.FromIndex(i)).TryGetPose(out var p); joints[i]=p.position; }
            bool palmValid=hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm);
            ApplyPoses(joints,valid,palmValid?palm.rotation:Quaternion.identity);
        }
        // Also used by deterministic visual/geometry validation without a headset.
        public void ApplyPoses(Vector3[] positions, bool[] tracked, Quaternion rotation)
        {
            int wrist=XRHandJointID.Wrist.ToIndex(), middle=XRHandJointID.MiddleProximal.ToIndex();
            bool visible=tracked[wrist] && tracked[middle] && tracked[XRHandJointID.Palm.ToIndex()];
            surface.enabled=visible; beacon.gameObject.SetActive(visible); if(!visible) return;
            int part=0;
            for(int f=0;f<digits.Length;f++) for(int j=1;j<digits[f].Length;j++) {
                int a=digits[f][j-1].ToIndex(), b=digits[f][j].ToIndex();
                float radius=(f==0?.0105f:f==4?.0075f:.009f)*(1-j*.08f);
                Vector3 from=positions[a], to=positions[b];
                if(!tracked[a] || !tracked[b]) { from=to=positions[wrist]; radius=0; }
                Capsule(part++,from,to,radius);
            }
            Vector3 center=Vector3.Lerp(positions[wrist],positions[middle],.5f);
            float length=Mathf.Clamp(Vector3.Distance(positions[wrist],positions[middle]),.05f,.12f);
            float width=tracked[XRHandJointID.IndexProximal.ToIndex()] && tracked[XRHandJointID.LittleProximal.ToIndex()]
                ? Mathf.Clamp(Vector3.Distance(positions[XRHandJointID.IndexProximal.ToIndex()],positions[XRHandJointID.LittleProximal.ToIndex()]),.04f,.09f):.062f;
            Ellipsoid(part++,center,rotation,new Vector3(width*.59f,.018f,length*.59f));
            // Raised dorsal armor and a substantial wrist seal define the silhouette.
            Ellipsoid(part++,center+rotation*new Vector3(0,.013f,0),rotation,new Vector3(width*.44f,.010f,length*.40f));
            Ellipsoid(part++,positions[wrist],rotation,new Vector3(width*.46f,.019f,.018f));
            beacon.SetLocalPositionAndRotation(center+rotation*new Vector3(0,.024f,0),rotation);
            mesh.vertices=vertices; mesh.normals=normals; mesh.RecalculateBounds();
        }
        void Capsule(int part, Vector3 a, Vector3 b, float radius)
        {
            Vector3 axis=b-a; float length=axis.magnitude;
            var rotation=length>.0001f?Quaternion.FromToRotation(Vector3.up,axis/length):Quaternion.identity;
            for(int r=0;r<Rings;r++) {
                float phi=Mathf.PI*r/(Rings-1), y=Mathf.Cos(phi), radial=Mathf.Sin(phi);
                for(int s=0;s<Sides;s++) {
                    float theta=2*Mathf.PI*s/Sides; var n=new Vector3(radial*Mathf.Cos(theta),y,radial*Mathf.Sin(theta));
                    int i=part*Sides*Rings+r*Sides+s;
                    vertices[i]=(a+b)*.5f+rotation*(n*radius+Vector3.up*(y>=0?1:-1)*length*.5f);
                    normals[i]=rotation*n;
                }
            }
        }
        void Ellipsoid(int part, Vector3 center, Quaternion rotation, Vector3 size)
        {
            for(int r=0;r<Rings;r++) for(int s=0;s<Sides;s++) {
                float phi=Mathf.PI*r/(Rings-1), theta=2*Mathf.PI*s/Sides;
                var n=new Vector3(Mathf.Sin(phi)*Mathf.Cos(theta),Mathf.Cos(phi),Mathf.Sin(phi)*Mathf.Sin(theta));
                int i=part*Sides*Rings+r*Sides+s;
                vertices[i]=center+rotation*Vector3.Scale(n,size);
                normals[i]=rotation*new Vector3(n.x/size.x,n.y/size.y,n.z/size.z).normalized;
            }
        }
        void OnDestroy() { if(mesh) Destroy(mesh); }
    }
}
