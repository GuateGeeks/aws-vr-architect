using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace GuateGeeks.AwsVr
{
    // One dynamic solid mesh per hand. Joint poses remain in XR tracking-origin space.
    // Two-tone armour via vertex colours (one draw call): graphite under-suit, titanium plates on every phalanx,
    // a quetzal-teal wrist seal and luminous knuckle nodes. Vertex alpha marks self-lit parts (LabEmblem shader).
    public sealed class ArmoredHandVisual : MonoBehaviour
    {
        const int Bones = 19, Knuckles = 4, Sides = 10, Rings = 7, Parts = Bones * 2 + 3 + Knuckles;
        readonly Vector3[] vertices = new Vector3[Parts * Sides * Rings];
        readonly Vector3[] normals = new Vector3[Parts * Sides * Rings];
        readonly Color[] colors = new Color[Parts * Sides * Rings];
        static readonly XRHandJointID[] knuckleJoints = { XRHandJointID.IndexProximal, XRHandJointID.MiddleProximal, XRHandJointID.RingProximal, XRHandJointID.LittleProximal };
        readonly Vector3[] joints = new Vector3[26];
        readonly bool[] valid = new bool[26];
        Mesh mesh;
        MeshRenderer surface;
        Transform beacon;
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
        public void Initialize(bool left)
        {
            mesh = new Mesh { name = "Articulated armored glove" }; mesh.MarkDynamic();
            var triangles = new List<int>();
            for(int p=0;p<Parts;p++) for(int r=0;r<Rings-1;r++) for(int s=0;s<Sides;s++) {
                int a=p*Sides*Rings+r*Sides+s, b=p*Sides*Rings+r*Sides+(s+1)%Sides;
                triangles.Add(a); triangles.Add(b); triangles.Add(a+Sides);
                triangles.Add(b); triangles.Add(b+Sides); triangles.Add(a+Sides);
            }
            // Part order: bone capsules, phalanx plates, palm, dorsal armour, wrist seal, knuckle nodes.
            Color suit=Tone("#1E2830",0), plate=Tone("#5C7383",0), palm=Tone("#26333D",0), dorsal=Tone("#6E8798",0), seal=Tone("#0E8A8C",.35f), node=Tone("#7FEFFF",1);
            for(int p=0;p<Parts;p++) {
                Color c=p<Bones?suit:p<Bones*2?plate:p==Bones*2?palm:p==Bones*2+1?dorsal:p==Bones*2+2?seal:node;
                for(int v=0;v<Sides*Rings;v++) colors[p*Sides*Rings+v]=c;
            }
            mesh.vertices=vertices; mesh.normals=normals; mesh.colors=colors; mesh.SetTriangles(triangles,0);
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            surface=gameObject.AddComponent<MeshRenderer>(); surface.shadowCastingMode=ShadowCastingMode.Off; surface.receiveShadows=false;
            // Dark titanium with luminous pinch emitters: the fingertips that touch holograms carry the light.
            surface.sharedMaterial=LabVisuals.SpecialMaterial("LabEmblem",new Color(.85f,.9f,.95f,1));
            for(int f=0;f<tips.Length;f++) {
                tips[f]=LabVisuals.Shape(transform,tipNames[f],PrimitiveType.Sphere,Vector3.zero,Vector3.one*TipSize(f),LabVisuals.Ice).transform;
                tips[f].gameObject.SetActive(false);
            }
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
        static float TipSize(int finger) => finger==0 || finger==1 ? .011f : finger==4 ? .0085f : .0095f;
        // Proximity feedback: a fingertip near a touchable surface swells and brightens; contact turns it green.
        public void SetTouch(int finger, float proximity, bool pressing)
        {
            if(finger<0 || finger>=tips.Length || !tips[finger]) return;
            proximity=Mathf.Clamp01(proximity);
            if(Mathf.Approximately(tipGlow[finger],proximity) && tipPressing[finger]==pressing) return;
            tipGlow[finger]=proximity; tipPressing[finger]=pressing;
            tips[finger].localScale=Vector3.one*TipSize(finger)*(1+.55f*proximity);
            tips[finger].GetComponent<Renderer>().sharedMaterial=LabVisuals.Material(pressing?LabVisuals.Green:proximity>0?LabVisuals.White:LabVisuals.Ice);
        }
        // Also used by deterministic visual/geometry validation without a headset.
        public void ApplyPoses(Vector3[] positions, bool[] tracked, Quaternion rotation)
        {
            int wrist=XRHandJointID.Wrist.ToIndex(), middle=XRHandJointID.MiddleProximal.ToIndex();
            bool visible=tracked[wrist] && tracked[middle] && tracked[XRHandJointID.Palm.ToIndex()];
            surface.enabled=visible; beacon.gameObject.SetActive(visible);
            for(int f=0;f<tips.Length;f++) {
                int tip=tipJoints[f].ToIndex();
                tips[f].gameObject.SetActive(visible && tracked[tip]);
                if(visible && tracked[tip]) tips[f].localPosition=positions[tip];
            }
            if(!visible) return;
            int part=0;
            Vector3 dorsalUp=rotation*Vector3.up;
            for(int f=0;f<digits.Length;f++) for(int j=1;j<digits[f].Length;j++) {
                int a=digits[f][j-1].ToIndex(), b=digits[f][j].ToIndex();
                float radius=(f==0?.0105f:f==4?.0075f:.009f)*(1-j*.08f);
                Vector3 from=positions[a], to=positions[b];
                bool ok=tracked[a] && tracked[b];
                if(!ok) { from=to=positions[wrist]; radius=0; }
                Capsule(part,from,to,radius*.86f);
                // Articulated plate riding on the back of each phalanx, with a small gap at every joint.
                Vector3 axis=to-from; float span=axis.magnitude;
                if(ok && span>.002f) {
                    axis/=span; var up=dorsalUp-axis*Vector3.Dot(dorsalUp,axis);
                    if(up.sqrMagnitude<1e-6f) up=Vector3.Cross(axis,Vector3.right);
                    up.Normalize();
                    Ellipsoid(Bones+part,(from+to)*.5f+up*radius*.62f,Quaternion.LookRotation(axis,up),new Vector3(radius*1.12f,radius*.5f,span*.44f));
                } else Ellipsoid(Bones+part,positions[wrist],Quaternion.identity,Vector3.one*1e-5f);
                part++;
            }
            part=Bones*2;
            Vector3 center=Vector3.Lerp(positions[wrist],positions[middle],.5f);
            float length=Mathf.Clamp(Vector3.Distance(positions[wrist],positions[middle]),.05f,.12f);
            float width=tracked[XRHandJointID.IndexProximal.ToIndex()] && tracked[XRHandJointID.LittleProximal.ToIndex()]
                ? Mathf.Clamp(Vector3.Distance(positions[XRHandJointID.IndexProximal.ToIndex()],positions[XRHandJointID.LittleProximal.ToIndex()]),.04f,.09f):.062f;
            Ellipsoid(part++,center,rotation,new Vector3(width*.59f,.018f,length*.59f));
            // Raised dorsal armor and a substantial wrist seal define the silhouette.
            Ellipsoid(part++,center+rotation*new Vector3(0,.013f,0),rotation,new Vector3(width*.44f,.010f,length*.40f));
            Ellipsoid(part++,positions[wrist],rotation,new Vector3(width*.46f,.019f,.018f));
            for(int k=0;k<Knuckles;k++) {
                int joint=knuckleJoints[k].ToIndex();
                if(tracked[joint]) Ellipsoid(part++,positions[joint]+dorsalUp*.0105f,rotation,new Vector3(.0042f,.0026f,.0042f));
                else Ellipsoid(part++,positions[wrist],rotation,Vector3.one*1e-5f);
            }
            beacon.SetLocalPositionAndRotation(center+rotation*new Vector3(0,.024f,0),rotation);
            mesh.vertices=vertices; mesh.normals=normals; mesh.RecalculateBounds();
        }
        static Color Tone(string hex,float glow) { var c=LabVisuals.Hex(hex); if(QualitySettings.activeColorSpace==ColorSpace.Linear) c=c.linear; c.a=glow; return c; }
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
