using System.Collections.Generic;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    public sealed class HoloEnvironment : MonoBehaviour
    {
        Transform scanner;
        Transform reactor, counterRing, ceiling;
        readonly List<Transform> energy = new List<Transform>();
        public static void Build(Transform world)
        {
            RenderSettings.skybox=null; RenderSettings.fog=false;
            var floor=Shape(world,"Procedural architectural grid",PrimitiveType.Cube,new Vector3(0,-.08f,2),new Vector3(30,.12f,30),Hex("#030911"));
            floor.GetComponent<Renderer>().sharedMaterial=SpecialMaterial("LabGrid",Hex("#030911"));
            Shape(world,"Projection console",PrimitiveType.Cylinder,new Vector3(0,.67f,2.65f),new Vector3(3.85f,.045f,3.85f),Hex("#091823"));
            Shape(world,"Console inset",PrimitiveType.Cylinder,new Vector3(0,.72f,2.65f),new Vector3(3.62f,.008f,3.62f),Hex("#07131D"));
            var rings=new List<Vector3>(); var accent=new List<Vector3>();
            var center=new Vector3(0,.74f,2.65f);
            HoloGeometry.Circle(rings,center,1.81f,96);
            HoloGeometry.Circle(rings,center,1.70f,96);
            HoloGeometry.Circle(rings,center,1.20f,72);
            HoloGeometry.Circle(rings,center,.48f,48);
            HoloGeometry.Circle(rings,center,.42f,48);
            for(int i=0;i<72;i++)
            {
                float a=i*Mathf.PI/36; var direction=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                HoloGeometry.Path(i%6==0?accent:rings,center+direction*(i%6==0?1.55f:1.62f),center+direction*1.67f);
            }
            for(int i=0;i<8;i++)
            {
                float a=i*Mathf.PI/4; var d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                var side=new Vector3(-d.z,0,d.x);
                HoloGeometry.Path(rings,center+d*.55f,center+d*.9f,center+d*1.1f+side*.16f,center+d*1.4f+side*.16f);
            }
            HoloGeometry.Strokes(world,"Console etched circuit mesh",rings,Hex("#1D5767"),.002f);
            HoloGeometry.Strokes(world,"Console divisions",accent,Cyan,.0035f);
            var scan=new List<Vector3>();
            for(int i=0;i<3;i++) HoloGeometry.Circle(scan,Vector3.zero,1.76f,30,false,i*120,85);
            var orbit=HoloGeometry.Strokes(world,"Rotating perimeter scanner",scan,Hex("#3699AD"),.004f);
            orbit.transform.position=center;
            var lab = world.gameObject.AddComponent<HoloEnvironment>(); lab.scanner=orbit.transform;
            lab.BuildArchitecture(world);

            var backdrop=new List<Vector3>(); var focus=new Vector3(0,1.82f,5.7f);
            HoloGeometry.Circle(backdrop,focus,2.12f,100,true);
            for(int i=0;i<4;i++) HoloGeometry.Circle(backdrop,focus,2.22f,24,true,12+i*90,56);
            for(int i=0;i<48;i++)
            {
                float a=i*Mathf.PI/24; var d=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);
                HoloGeometry.Path(backdrop,focus+d*2.30f,focus+d*(i%4==0?2.40f:2.34f));
            }
            // The far architectural ring remains subdued behind readable foreground UI.
            HoloGeometry.Strokes(world,"Far holographic aperture",backdrop,Hex("#143746"),.006f);
            var room=new List<Vector3>();
            HoloGeometry.Circle(room,new Vector3(0,.005f,2.65f),4.8f,128);
            HoloGeometry.Circle(room,new Vector3(0,4.1f,2.65f),4.8f,96);
            for(int i=0;i<12;i++)
            {
                float a=i*Mathf.PI/6; var d=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*4.8f;
                HoloGeometry.Path(room,d+new Vector3(0,0,2.65f),d+new Vector3(0,.4f,2.65f));
                HoloGeometry.Path(room,d+new Vector3(0,3.7f,2.65f),d+new Vector3(0,4.1f,2.65f));
            }
            HoloGeometry.Strokes(world,"Architectural light rails",room,Hex("#1C4C5C"),.007f);
            var brand=Panel(world,"Projection table marking",new Vector3(0,.76f,1.73f),new Vector2(850,70),background:false,movable:false);
            brand.localRotation=Quaternion.Euler(90,0,0);
            Text(brand,"G U A T E G E E K S  /  A W S  D A Y",Vector2.zero,new Vector2(850,70),21,Muted,TextAnchor.MiddleCenter);
        }
        void BuildArchitecture(Transform world)
        {
            var steel = Hex("#233B4B"); var dark = Hex("#0B1521");
            // Solid architecture establishes depth; foreground interaction space stays open.
            Metal(world,"Raised console pedestal",PrimitiveType.Cylinder,new Vector3(0,.3f,2.65f),new Vector3(2.7f,.29f,2.7f),dark);
            Metal(world,"Console beveled chassis",PrimitiveType.Cylinder,new Vector3(0,.59f,2.65f),new Vector3(3.95f,.055f,3.95f),steel);
            for(int i=0;i<3;i++) Ring(world,new Vector3(0,.12f+i*.14f,2.65f),1.36f,Hex("#26778D"),.012f);
            var structure = new List<Vector3>(); var lights = new List<Vector3>(); var warm = new List<Vector3>();
            for(int i=0;i<11;i++)
            {
                float angle=(-110+i*22)*Mathf.Deg2Rad;
                Vector3 radial=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
                var p=radial*6.8f+new Vector3(0,0,2.65f);
                var column=Metal(world,"Titanium structural rib "+i,PrimitiveType.Cube,p+Vector3.up*2.45f,new Vector3(.24f,4.9f,.38f),steel);
                column.transform.localRotation=Quaternion.Euler(0,angle*Mathf.Rad2Deg,0);
                var foot=Metal(world,"Rib foundation",PrimitiveType.Cube,p+Vector3.up*.18f,new Vector3(.65f,.36f,.8f),dark);
                foot.transform.localRotation=column.transform.localRotation;
                HoloGeometry.Path(lights,p-radial*.22f+Vector3.up*.55f,p-radial*.22f+Vector3.up*4.35f);
                HoloGeometry.Path(structure,p+Vector3.up*4.85f,radial*4.6f+new Vector3(0,5.65f,2.65f),radial*2.3f+new Vector3(0,5.65f,2.65f));
                HoloGeometry.Path(warm,p-radial*.24f+Vector3.up*.45f,p-radial*.24f+Vector3.up*.8f);
            }
            HoloGeometry.Strokes(world,"Overhead structural trusses",structure,steel,.095f);
            HoloGeometry.Strokes(world,"Architectural cyan luminaires",lights,Hex("#31849A"),.024f);
            HoloGeometry.Strokes(world,"Amber foundation markers",warm,Orange,.028f);
            var roof=new List<Vector3>();
            for(int i=0;i<8;i++) HoloGeometry.Circle(roof,Vector3.zero,2.3f,12,false,i*45,32);
            ceiling=HoloGeometry.Strokes(world,"Suspended ceiling halo",roof,Hex("#286B81"),.022f).transform;
            ceiling.localPosition=new Vector3(0,5.65f,2.65f);
            var back=Metal(world,"Reactor containment wall",PrimitiveType.Cube,new Vector3(0,2.5f,9.25f),new Vector3(14,5.5f,.3f),dark);
            var center=new Vector3(0,2.75f,8.95f);
            var housing=Metal(world,"Reactor armored housing",PrimitiveType.Cylinder,center,new Vector3(3.9f,.12f,3.9f),steel);
            housing.transform.localRotation=Quaternion.Euler(90,0,0);
            var inset=Metal(world,"Reactor recess",PrimitiveType.Cylinder,center+Vector3.back*.14f,new Vector3(3.5f,.05f,3.5f),dark);
            inset.transform.localRotation=Quaternion.Euler(90,0,0);
            var arcs=new List<Vector3>(); var inner=new List<Vector3>();
            for(int i=0;i<12;i++) {
                HoloGeometry.Circle(arcs,Vector3.zero,1.65f,10,true,i*30,20);
                HoloGeometry.Circle(inner,Vector3.zero,1.22f,8,true,i*30+5,18);
            }
            reactor=HoloGeometry.Strokes(world,"Reactor outer turbine",arcs,Cyan,.025f).transform; reactor.localPosition=center+Vector3.back*.23f;
            counterRing=HoloGeometry.Strokes(world,"Reactor inner turbine",inner,Orange,.014f).transform; counterRing.localPosition=center+Vector3.back*.25f;
            var lens=Shape(world,"Reactor energy lens",PrimitiveType.Sphere,center+Vector3.back*.25f,new Vector3(1.7f,1.7f,.35f),Cyan);
            lens.GetComponent<Renderer>().sharedMaterial=SpecialMaterial("LabHologram",Cyan);
            var hub=Metal(world,"Reactor central hub",PrimitiveType.Sphere,center+Vector3.back*.3f,new Vector3(.5f,.5f,.23f),Hex("#C5EAF3"));
            for(int side=-1;side<=1;side+=2)
            for(int rack=0;rack<3;rack++)
            {
                var p=new Vector3(side*(3.0f+rack*1.25f),1.55f,8.85f);
                Metal(world,"Compute bay",PrimitiveType.Cube,p,new Vector3(.94f,2.6f,.6f),steel);
                for(int shelf=0;shelf<7;shelf++)
                {
                    Metal(world,"Compute cartridge",PrimitiveType.Cube,p+new Vector3(0,-1.02f+shelf*.34f,-.34f),new Vector3(.8f,.23f,.14f),dark);
                    HoloGeometry.Path(lights,p+new Vector3(-.31f,-1.02f+shelf*.34f,-.43f),p+new Vector3(.18f,-1.02f+shelf*.34f,-.43f));
                }
                var display=Panel(world,"Bay designation",p+new Vector3(0,1.58f,-.4f),new Vector2(460,90),background:false,movable:false);
                Text(display,"C O M P U T E  /  0"+(rack+1),Vector2.zero,new Vector2(460,90),20,Muted,TextAnchor.MiddleCenter);
            }
            HoloGeometry.Strokes(world,"Compute bay illumination",lights,Hex("#245668"),.01f);
            // Deliberately decorative, never presented as measured AWS activity.
            for(int i=0;i<16;i++) energy.Add(Shape(world,"Ambient energy marker",PrimitiveType.Sphere,Vector3.zero,Vector3.one*.045f,i%4==0?Orange:Cyan).transform);
            var title=Panel(world,"Lab sector designation",new Vector3(0,5.05f,8.8f),new Vector2(1800,120),background:false,movable:false);
            Text(title,"A R C H I T E C T U R E   /   R E S E A R C H   D I V I S I O N",Vector2.zero,new Vector2(1800,120),27,Muted,TextAnchor.MiddleCenter);
            HoloGeometry.CombineMetal(world);
        }
        void Update()
        {
            float t=LabFeedback.Clock;
            if(scanner) scanner.localRotation=Quaternion.Euler(0,t*3,0);
            if(reactor) reactor.localRotation=Quaternion.Euler(0,0,t*5);
            if(counterRing) counterRing.localRotation=Quaternion.Euler(0,0,-t*8);
            if(ceiling) ceiling.localRotation=Quaternion.Euler(0,t*2,0);
            for(int i=0;i<energy.Count;i++) {
                float a=i*Mathf.PI/8+t*.08f;
                energy[i].localPosition=new Vector3(Mathf.Sin(a)*5.9f,.55f+Mathf.Repeat(i*.31f+t*.12f,3.7f),2.65f+Mathf.Cos(a)*5.9f);
            }
        }
    }
}
