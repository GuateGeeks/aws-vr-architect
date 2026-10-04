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

        public void Initialize(ResourceNode model, ArchitectureLab lab)
        {
            owner = lab; Model = model; transform.localPosition = model.position;
            var tint = ServiceCatalog.Get(model.kind).Color;
            Metal(transform, "Projector plinth", PrimitiveType.Cylinder, new Vector3(0, -.29f, 0), new Vector3(.49f, .022f, .49f), Hex("#304B5B"));
            Metal(transform,"Projector inset",PrimitiveType.Cylinder,new Vector3(0,-.261f,0),new Vector3(.39f,.008f,.39f),Hex("#08141F"));
            var plate = new List<Vector3>();
            HoloGeometry.Circle(plate, new Vector3(0,-.258f,0), .23f, 48);
            for (int i=0; i<12; i++)
            {
                float a = i * Mathf.PI / 6;
                HoloGeometry.Path(plate, new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*.2f + Vector3.down*.255f,
                    new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*.225f + Vector3.down*.255f);
            }
            HoloGeometry.Strokes(transform, "Projector circuitry", plate, tint, .0025f);
            core = new GameObject("Projected service").transform; core.SetParent(transform, false);
            bool cylinder = model.kind == ServiceKind.DynamoDB || model.kind == ServiceKind.S3;
            var shell = Shape(core,"Scan-line volume", cylinder ? PrimitiveType.Cylinder : model.kind == ServiceKind.EventBridge ? PrimitiveType.Sphere : PrimitiveType.Cube,
                Vector3.zero, cylinder ? new Vector3(.36f,.15f,.36f) : new Vector3(.36f,.32f,.28f), tint);
            shell.GetComponent<Renderer>().sharedMaterial = SpecialMaterial("LabHologram", tint);
            if (model.kind == ServiceKind.DynamoDB)
            {
                var rings = new List<Vector3>();
                for (int i=-1; i<=1; i++) HoloGeometry.Circle(rings,new Vector3(0,i*.15f,0),.18f,32);
                HoloGeometry.Strokes(core,"Database contours",rings,tint,.003f);
            }
            else if (!cylinder && model.kind != ServiceKind.EventBridge) HoloGeometry.BoxFrame(core,new Vector3(.365f,.325f,.285f),Color.Lerp(tint,White,.22f));
            HoloGeometry.Glyph(core,model.kind,tint);
            BuildServiceVolume(model.kind,tint);
            orbit = new GameObject("Orbital scanner").transform; orbit.SetParent(transform,false);
            var arcs = new List<Vector3>();
            for (int i=0;i<3;i++) HoloGeometry.Circle(arcs,new Vector3(0,-.21f,0),.28f,18,false,i*120,72);
            HoloGeometry.Strokes(orbit,"Segmented orbit",arcs,tint,.004f);
            selection = Ring(transform,new Vector3(0,-.24f,0),.32f,White,.008f); selection.enabled=false;
            stateRing = Ring(transform,new Vector3(0,-.25f,0),.255f,Muted,.004f);
            label = Panel(transform,"Resource label",new Vector3(0,.36f,0),new Vector2(390,128),background:false,movable:false);
            nameText = Text(label,model.name,new Vector2(0,23),new Vector2(390,50),27,White,TextAnchor.MiddleCenter);
            stateText = Text(label,"BORRADOR",new Vector2(0,-19),new Vector2(390,42),17,Muted,TextAnchor.MiddleCenter);
            nameText.richText = false; nameText.fontSizeMax = 27; nameText.enableAutoSizing = true; nameText.fontSizeMin = 12;
            CreatePort(false); if (model.kind != ServiceKind.CloudWatch) CreatePort(true);
            var hit = gameObject.AddComponent<BoxCollider>(); hit.size=new Vector3(.55f,.65f,.5f);
            Target=gameObject.AddComponent<LabTarget>(); Target.Node=this; Target.Action=()=>lab.Select(this);
        }
        void BuildServiceVolume(ServiceKind kind, Color tint)
        {
            var metal=Color.Lerp(Hex("#1E3344"),tint,.18f);
            // Individual physical silhouettes behind the front-facing service emblem.
            switch(kind)
            {
                case ServiceKind.DynamoDB:
                    for(int i=-1;i<=1;i++) {
                        Metal(core,"Database storage tier",PrimitiveType.Cylinder,new Vector3(0,i*.095f,0),new Vector3(.29f,.032f,.29f),metal);
                        Ring(core,new Vector3(0,i*.095f+.032f,0),.147f,tint,.004f,32);
                    } break;
                case ServiceKind.S3:
                    Metal(core,"Storage canister",PrimitiveType.Cylinder,Vector3.zero,new Vector3(.25f,.135f,.25f),metal);
                    for(int i=-1;i<=1;i+=2) Ring(core,new Vector3(0,i*.13f,0),.13f,tint,.005f,32);
                    break;
                case ServiceKind.Lambda:
                    Metal(core,"Compute processor",PrimitiveType.Cube,Vector3.zero,new Vector3(.245f,.245f,.16f),metal);
                    for(int i=0;i<5;i++) for(int side=-1;side<=1;side+=2)
                        Metal(core,"Processor pin",PrimitiveType.Cube,new Vector3(side*.143f,(i-2)*.045f,0),new Vector3(.045f,.012f,.09f),tint);
                    break;
                case ServiceKind.SQS:
                    for(int i=-1;i<=1;i++) Metal(core,"Message cartridge",PrimitiveType.Cube,new Vector3(i*.095f,0,0),new Vector3(.068f,.24f,.19f),metal);
                    break;
                case ServiceKind.ApiGateway:
                    for(int side=-1;side<=1;side+=2) Metal(core,"Gateway pillar",PrimitiveType.Cube,new Vector3(side*.13f,0,0),new Vector3(.045f,.28f,.2f),metal);
                    Metal(core,"Gateway lintel",PrimitiveType.Cube,new Vector3(0,.12f,0),new Vector3(.29f,.04f,.2f),metal); break;
                case ServiceKind.EventBridge:
                    Metal(core,"Event routing hub",PrimitiveType.Sphere,Vector3.zero,Vector3.one*.17f,metal);
                    for(int i=0;i<3;i++) {
                        float a=i*Mathf.PI*2/3;
                        Metal(core,"Routing satellite",PrimitiveType.Sphere,new Vector3(Mathf.Sin(a)*.14f,Mathf.Cos(a)*.14f,.025f),Vector3.one*.072f,tint);
                    } break;
                default:
                    for(int i=0;i<4;i++) Metal(core,"Metric pillar",PrimitiveType.Cube,new Vector3((i-1.5f)*.07f,-.10f+i*.025f,.03f),new Vector3(.04f,.07f+i*.05f,.15f),metal);
                    break;
            }
            var projection=new List<Vector3>();
            for(int i=0;i<4;i++) {
                float a=i*Mathf.PI/2+Mathf.PI/4;
                var d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                HoloGeometry.Path(projection,d*.18f+Vector3.down*.25f,d*.12f+Vector3.down*.17f);
            }
            HoloGeometry.Strokes(transform,"Projection supports",projection,Color.Lerp(tint,Hex("#122532"),.65f),.002f);
            HoloGeometry.CombineMetal(core);
        }
        void CreatePort(bool output)
        {
            var port = Shape(transform, output ? "Output port" : "Input port", PrimitiveType.Sphere, new Vector3(output ? .37f : -.37f, -.12f, -.08f), Vector3.one * .10f, output ? Orange : Cyan, true);
            var target = port.AddComponent<LabTarget>(); target.Node = this; target.Port = output ? 2 : 1; target.Action = () => owner.SelectPort(this, output);
            var tag = Panel(transform, "Port label", new Vector3(output ? .37f : -.37f, -.03f, -.08f), new Vector2(160, 40), background: false, movable: false);
            Text(tag, output ? "SALIDA" : "ENTRADA", Vector2.zero, new Vector2(160, 40), 14, output ? Orange : Cyan, TextAnchor.MiddleCenter);
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
            Color color=Model.state==ResourceState.Ready?Green:Model.state==ResourceState.Failed?Hex("#FF8290"):Model.state==ResourceState.Provisioning?Orange:Muted;
            string state=Model.state==ResourceState.Ready?(owner.IsCloud?"ACTIVO · AWS":"ACTIVO · SIMULADO"):Model.state==ResourceState.Failed?(owner.IsCloud?"ERROR · AWS":"ERROR SIMULADO"):Model.state==ResourceState.Provisioning?"CREANDO…":"BORRADOR";
            if(ConnectionHint!=0)
            {
                state=ConnectionHint==1?"ORIGEN":ConnectionHint==2?"+ DESTINO VÁLIDO":"NO COMPATIBLE";
                color=ConnectionHint==1?Orange:ConnectionHint==2?Green:Muted;
            }
            if(Grabbed) {state="MOVIENDO";color=White;}
            stateText.text=state;stateText.color=color; stateRing.sharedMaterial=Material(color);
            bool assistantHighlight=Time.unscaledTime<assistantHighlightUntil;
            if(assistantHighlight){stateText.text=assistantPreview?"ATLAS · OBJETIVO":"ATLAS · ACTUALIZADO";stateText.color=assistantPreview?Orange:Green;}
            selection.enabled=assistantHighlight||selected||hovered||Grabbed||ConnectionHint==2;
            selection.widthMultiplier=assistantHighlight && !(LabFeedback.Current && LabFeedback.Current.ReducedMotion)?.008f+.004f*Mathf.Sin(Time.unscaledTime*7):.008f;
            selection.sharedMaterial=Material(assistantHighlight?(assistantPreview?Orange:Green):ConnectionHint==2?Green:White);
        }
    }
}
