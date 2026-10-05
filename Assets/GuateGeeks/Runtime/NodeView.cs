using System.Collections.Generic;
using UnityEngine;
using Text = TMPro.TMP_Text;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    public sealed class NodeView : MonoBehaviour
    {
        public ResourceNode Model { get; private set; }
        public LabTarget Target { get; private set; }
        public AwsServiceEmblem Emblem { get; private set; }
        public int ConnectionHint { get; private set; }
        Transform core, orbit;
        RectTransform label;
        LineRenderer selection, stateRing;
        Text stateText, nameText;
        ArchitectureLab owner;
        bool hovered, selected;
        float assistantHighlightUntil;bool assistantPreview;
        public void FlashAssistant(bool preview,float seconds){assistantPreview=preview;assistantHighlightUntil=Time.unscaledTime+seconds;}
        public bool Grabbed;
        // Another person's editing lock (shared room): their colour rings the plinth and names them.
        public PeerState LockedBy;
        LineRenderer lockRing;
        static readonly Color Failed = Hex("#FF8290");

        public void Initialize(ResourceNode model, ArchitectureLab lab)
        {
            owner = lab; Model = model; transform.localPosition = model.position;
            var tint = ServiceCatalog.Get(model.kind).Color;
            // Stand matching the emblem: dark base, raised titanium rim, recessed glass lit in the service's official colour.
            var service = AwsIconGeometry.Background(model.kind);
            Metal(transform, "Projector plinth", PrimitiveType.Cylinder, new Vector3(0, -.29f, 0), new Vector3(.46f, .02f, .46f), Hex("#141F28"));
            Metal(transform, "Projector rim", PrimitiveType.Cylinder, new Vector3(0, -.27f, 0), new Vector3(.44f, .014f, .44f), Hex("#5C7383"));
            Metal(transform,"Projector inset",PrimitiveType.Cylinder,new Vector3(0,-.266f,0),new Vector3(.385f,.012f,.385f),Hex("#08141F"));
            var pool = Shape(transform, "Service light pool", PrimitiveType.Quad, new Vector3(0, -.258f, 0), new Vector3(.44f, .44f, 1), service);
            pool.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var poolColor = service; poolColor.a = .9f; pool.GetComponent<Renderer>().sharedMaterial = SpecialMaterial("LabGlow", poolColor);
            var serviceRing = Ring(transform, new Vector3(0, -.259f, 0), .178f, service, .012f, 48);
            serviceRing.sharedMaterial = Beam(service, false); serviceRing.textureMode = LineTextureMode.Stretch;
            var plate = new List<Vector3>();
            HoloGeometry.Circle(plate, new Vector3(0,-.258f,0), .23f, 48);
            for (int i=0; i<12; i++)
            {
                float a = i * Mathf.PI / 6;
                HoloGeometry.Path(plate, new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*.2f + Vector3.down*.255f,
                    new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*.225f + Vector3.down*.255f);
            }
            HoloGeometry.Strokes(transform, "Projector circuitry", plate, tint, .0025f);
            ProjectionCone(tint);
            core = new GameObject("Projected service").transform; core.SetParent(transform, false);
            // The official service icon as a solid object: category-colour tile with the white symbol raised on both faces.
            Emblem = AwsServiceEmblem.Create(core, model.kind);
            ProjectionSupports(tint);
            orbit = new GameObject("Orbital scanner").transform; orbit.SetParent(transform,false);
            var arcs = new List<Vector3>();
            for (int i=0;i<3;i++) HoloGeometry.Circle(arcs,new Vector3(0,-.21f,0),.28f,18,false,i*120,72);
            HoloGeometry.Strokes(orbit,"Segmented orbit",arcs,tint,.004f);
            selection = Ring(transform,new Vector3(0,-.24f,0),.32f,White,.022f); selection.sharedMaterial=Beam(White,false); selection.textureMode=LineTextureMode.Stretch; selection.enabled=false;
            stateRing = Ring(transform,new Vector3(0,-.25f,0),.255f,Muted,.004f);
            label = Panel(transform,"Resource label",new Vector3(0,.36f,0),new Vector2(390,128),background:false,movable:false);
            nameText = Text(label,model.name,new Vector2(0,23),new Vector2(390,50),27,White,TextAnchor.MiddleCenter);
            stateText = Text(label,"BORRADOR",new Vector2(0,-19),new Vector2(390,42),17,Muted,TextAnchor.MiddleCenter);
            nameText.richText = false; nameText.fontSizeMax = 27; nameText.enableAutoSizing = true; nameText.fontSizeMin = 12;
            CreatePort(false); if (model.kind != ServiceKind.CloudWatch) CreatePort(true);
            var hit = gameObject.AddComponent<BoxCollider>(); hit.size=new Vector3(.55f,.65f,.5f);
            Target=gameObject.AddComponent<LabTarget>(); Target.Node=this; Target.Action=()=>lab.Select(this);
        }
        void ProjectionSupports(Color tint)
        {
            var projection=new List<Vector3>();
            for(int i=0;i<4;i++) {
                float a=i*Mathf.PI/2+Mathf.PI/4;
                var d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                HoloGeometry.Path(projection,d*.18f+Vector3.down*.25f,d*.12f+Vector3.down*.17f);
            }
            HoloGeometry.Strokes(transform,"Projection supports",projection,Color.Lerp(tint,Hex("#122532"),.65f),.002f);
        }
        // A faint cone of light from the projector plinth up into the object: it reads as projected, not placed.
        Mesh coneMesh;
        void ProjectionCone(Color tint)
        {
            const int sides = 28; float y0 = -.255f, y1 = .14f, r0 = .17f, r1 = .27f;
            var vertices = new Vector3[sides * 2]; var normals = new Vector3[sides * 2]; var triangles = new int[sides * 6];
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                vertices[i] = d * r0 + Vector3.up * y0; vertices[i + sides] = d * r1 + Vector3.up * y1;
                normals[i] = normals[i + sides] = (d * (y1 - y0) - Vector3.up * (r1 - r0)).normalized;
                int n = (i + 1) % sides, t = i * 6;
                triangles[t] = i; triangles[t + 1] = i + sides; triangles[t + 2] = n;
                triangles[t + 3] = n; triangles[t + 4] = i + sides; triangles[t + 5] = n + sides;
            }
            coneMesh = new Mesh { name = "Projection cone", vertices = vertices, normals = normals, triangles = triangles }; coneMesh.RecalculateBounds();
            var go = new GameObject("Projection light", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = coneMesh;
            var renderer = go.GetComponent<MeshRenderer>(); renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            var glow = Color.Lerp(tint, Cyan, .55f); glow.a = .32f; renderer.sharedMaterial = SpecialMaterial("LabHologram", glow);
        }
        void OnDestroy() { if (coneMesh) Destroy(coneMesh); }
        PeerState shownLock;
        void UpdateLock()
        {
            if (ReferenceEquals(shownLock, LockedBy)) { if (lockRing && LockedBy != null) lockRing.transform.localRotation = Quaternion.Euler(0, -LabFeedback.Clock * 40, 0); return; }
            shownLock = LockedBy;
            if (LockedBy == null) { if (lockRing) lockRing.enabled = false; return; }
            if (!lockRing)
            {
                var pivot = new GameObject("Teammate lock").transform; pivot.SetParent(transform, false);
                var arcs = new Vector3[40];
                for (int i = 0; i < arcs.Length; i++) { float a = i * Mathf.PI * 2 / arcs.Length; arcs[i] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .36f + Vector3.down * .235f; }
                lockRing = Line(pivot, "Lock ring", arcs, White, .03f, true); lockRing.textureMode = LineTextureMode.Stretch;
            }
            lockRing.sharedMaterial = Beam(LockedBy.Color); lockRing.enabled = true;
        }
        void CreatePort(bool output)
        {
            var port = Shape(transform, output ? "Output port" : "Input port", PrimitiveType.Sphere, new Vector3(output ? .37f : -.37f, -.12f, -.08f), Vector3.one * .10f, output ? Orange : Cyan, true);
            var target = port.AddComponent<LabTarget>(); target.Node = this; target.Port = output ? 2 : 1; target.Action = () => owner.SelectPort(this, output);
            var tag = Panel(transform, "Port label", new Vector3(output ? .37f : -.37f, -.03f, -.08f), new Vector2(160, 40), background: false, movable: false);
            Text(tag, output ? "SALIDA" : "ENTRADA", Vector2.zero, new Vector2(160, 40), 16, output ? Orange : Cyan, TextAnchor.MiddleCenter);
        }
        public void SetSelected(bool value) { selected=value; }
        public void SetHovered(bool value) { hovered=value; }
        public void SetConnectionHint(int value) { ConnectionHint=value; }
        void Update()
        {
            nameText.text = Model.name;
            float clock=LabFeedback.Clock;
            core.localPosition=Vector3.up*Mathf.Sin(clock*1.4f+transform.position.x)*.013f;
            core.localScale=Vector3.Lerp(core.localScale,Vector3.one*(Grabbed?1.12f:hovered?1.06f:1),Time.unscaledDeltaTime*14);
            orbit.localRotation=Quaternion.Euler(0,clock*16,0);
            var cam=Camera.main;
            if(cam && label) { var dir=label.position-cam.transform.position; dir.y=0; if(dir.sqrMagnitude>.01f) label.rotation=Quaternion.LookRotation(dir); }
            Color color=Model.state==ResourceState.Ready?Green:Model.state==ResourceState.Failed?Failed:Model.state==ResourceState.Provisioning?Orange:Muted;
            string state=Model.state==ResourceState.Ready?(owner.IsCloud?"ACTIVO · AWS":"ACTIVO · SIMULADO"):Model.state==ResourceState.Failed?(owner.IsCloud?"ERROR · AWS":"ERROR SIMULADO"):Model.state==ResourceState.Provisioning?"CREANDO…":"BORRADOR";
            if(ConnectionHint!=0)
            {
                state=ConnectionHint==1?"ORIGEN":ConnectionHint==2?"+ DESTINO VÁLIDO":"NO COMPATIBLE";
                color=ConnectionHint==1?Orange:ConnectionHint==2?Green:Muted;
            }
            if(Grabbed) {state="MOVIENDO";color=White;}
            else if(LockedBy!=null) {state=LockedBy.LockText;color=LockedBy.Color;}
            UpdateLock();
            stateText.text=state;stateText.color=color; stateRing.sharedMaterial=Material(color);
            bool assistantHighlight=Time.unscaledTime<assistantHighlightUntil;
            if(assistantHighlight){stateText.text=assistantPreview?"ATLAS · OBJETIVO":"ATLAS · ACTUALIZADO";stateText.color=assistantPreview?Orange:Green;}
            selection.enabled=assistantHighlight||selected||hovered||Grabbed||ConnectionHint==2;
            selection.widthMultiplier=assistantHighlight && !(LabFeedback.Current && LabFeedback.Current.ReducedMotion)?.024f+.01f*Mathf.Sin(Time.unscaledTime*7):hovered && !selected?.016f:.024f;
            selection.sharedMaterial=Beam(assistantHighlight?(assistantPreview?Orange:Green):ConnectionHint==2?Green:selected?Ice:Cyan,false);
        }
    }
}
