using System.Collections.Generic;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    public sealed class HoloEnvironment : MonoBehaviour
    {
        Transform scanner;
        Transform reactor, counterRing, ceiling;
        // The reactor on the far wall doubles as the assistant's presence: it tints, spins up and breathes with voice.
        public static HoloEnvironment Current { get; private set; }
        Renderer reactorRenderer, counterRenderer, lensRenderer;
        Transform lens;
        Vector3 lensScale;
        float reactorAngle, counterAngle, level, levelGoal, lastClock;
        bool assistantActive;
        Color appliedColor = Color.clear;
        public bool AssistantActive => assistantActive;
        public void SetAssistant(bool active, Color color, float signal)
        {
            assistantActive = active; levelGoal = active ? Mathf.Clamp01(signal) : 0;
            var target = active ? color : Cyan;
            if (target == appliedColor || !reactorRenderer) return;
            appliedColor = target;
            reactorRenderer.sharedMaterial = Material(target);
            lensRenderer.sharedMaterial = SpecialMaterial("LabHologram", target);
            counterRenderer.sharedMaterial = Material(active ? Color.Lerp(target, White, .35f) : Orange);
        }
        void OnDestroy() { if (Current == this) Current = null; if (horizonMesh) Destroy(horizonMesh); foreach (var m in ownedMaterials) if (m) Destroy(m); ownedMaterials.Clear(); }
        readonly List<Transform> energy = new List<Transform>();
        public static void Build(Transform world)
        {
            RenderSettings.skybox=null; RenderSettings.fog=false;
            var floor=Shape(world,"Procedural architectural grid",PrimitiveType.Cube,new Vector3(0,-.08f,2),new Vector3(30,.12f,30),Hex("#030911"));
            floor.GetComponent<Renderer>().sharedMaterial=SpecialMaterial("LabGrid",Hex("#030911"));
            // Projection table: bevelled dark-metal top, recessed glass field and a glowing quetzal-teal edge.
            Metal(world,"Projection console",PrimitiveType.Cylinder,new Vector3(0,.67f,2.65f),new Vector3(3.85f,.045f,3.85f),Hex("#15222C"));
            Metal(world,"Console bevel",PrimitiveType.Cylinder,new Vector3(0,.705f,2.65f),new Vector3(3.74f,.012f,3.74f),Hex("#2C3E4B"));
            Shape(world,"Console inset",PrimitiveType.Cylinder,new Vector3(0,.72f,2.65f),new Vector3(3.62f,.008f,3.62f),Hex("#07131D"));
            TableLight(world,new Vector3(0,.672f,2.65f),1.94f,EventBranding.Quetzal,.05f);
            TableLight(world,new Vector3(0,.735f,2.65f),1.82f,Cyan,.014f);
            TableLight(world,new Vector3(0,.585f,2.65f),1.99f,EventBranding.Leaf,.02f);
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
            var lab = world.gameObject.AddComponent<HoloEnvironment>(); lab.scanner=orbit.transform; Current = lab;
            lab.BuildArchitecture(world);
            lab.BuildHorizon(world);
            lab.BuildFloorLight(world, center);

            var backdrop=new List<Vector3>(); var focus=new Vector3(0,1.82f,5.7f);
            HoloGeometry.Circle(backdrop,focus,2.12f,100,true);
            for(int i=0;i<4;i++) HoloGeometry.Circle(backdrop,focus,2.22f,24,true,12+i*90,56);
            for(int i=0;i<48;i++)
            {
                float a=i*Mathf.PI/24; var d=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);
                HoloGeometry.Path(backdrop,focus+d*2.30f,focus+d*(i%4==0?2.40f:2.34f));
            }
            // The far architectural ring remains subdued behind readable foreground UI.
            lab.aperture=HoloGeometry.Strokes(world,"Far holographic aperture",backdrop,Hex("#1B4A5C"),.006f).transform;
            lab.aperture.localPosition=focus; ShiftToPivot(lab.aperture,focus);
            lab.quetzal=DigitalQuetzal.Create(world);
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
            // Etched into the table glass, facing the user: the event and the organising community.
            var brand=Panel(world,"Projection table marking",new Vector3(0,.746f,1.76f),new Vector2(900,96),background:false,movable:false);
            brand.localRotation=Quaternion.Euler(90,0,0);
            var etch=Color.Lerp(EventBranding.Quetzal,Cyan,.45f);
            Text(brand,"A W S   C O M M U N I T Y   D A Y   ·   G U A T E M A L A",new Vector2(0,14),new Vector2(900,46),21,etch,TextAnchor.MiddleCenter);
            var gg=LabVisuals.Rect(brand,"GuateGeeks etched logo",new Vector2(0,-62),new Vector2(150,98)).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            gg.texture=EventBranding.GuateGeeksFull; gg.color=new Color(1,1,1,.85f); gg.raycastTarget=false;
        }
        // A light run: the pairs list from HoloGeometry.Path is collapsed back into a polyline drawn as a moving beam.
        void MovingLight(Transform world,string name,List<Vector3> pairs,Color color,float width,float speed)
        {
            var points=new List<Vector3>{pairs[0]}; for(int i=1;i<pairs.Count;i+=2) points.Add(pairs[i]);
            var line=Line(world,name,points.ToArray(),color,width); line.textureMode=LineTextureMode.Stretch; line.numCapVertices=0;
            var material=new Material(Beam(color)); material.SetFloat("_Speed",speed); material.SetFloat("_Dashes",points.Count>2?3:2); line.sharedMaterial=material;
            ownedMaterials.Add(material);
        }
        readonly List<Material> ownedMaterials=new List<Material>();
        static LineRenderer TableLight(Transform world, Vector3 center, float radius, Color color, float width)
        {
            var ring=Ring(world,center,radius,color,width,128); ring.name="Console light · "+radius.ToString("0.00");
            ring.sharedMaterial=Beam(color,false); ring.textureMode=LineTextureMode.Stretch; ring.alignment=LineAlignment.TransformZ;
            ring.transform.localRotation=Quaternion.Euler(90,0,0);
            var pts=new Vector3[ring.positionCount]; ring.GetPositions(pts);
            for(int i=0;i<pts.Length;i++) pts[i]=new Vector3(pts[i].x,pts[i].z,-pts[i].y);  // flat on the table plane
            ring.SetPositions(pts); return ring;
        }
        void BuildArchitecture(Transform world)
        {
            var steel = Hex("#1B2B36"); var dark = Hex("#0A131B"); var graphite = Hex("#141F28");
            // Solid architecture establishes depth; foreground interaction space stays open.
            Metal(world,"Raised console pedestal",PrimitiveType.Cylinder,new Vector3(0,.3f,2.65f),new Vector3(2.7f,.29f,2.7f),dark);
            Metal(world,"Console beveled chassis",PrimitiveType.Cylinder,new Vector3(0,.59f,2.65f),new Vector3(3.95f,.055f,3.95f),Hex("#1B2A35"));
            for(int i=0;i<3;i++) Ring(world,new Vector3(0,.12f+i*.14f,2.65f),1.36f,Hex("#26778D"),.012f);
            // Slim graphite pillars with lit seams, event-colour accents at their feet and lit arches meeting in a crown ring.
            var arches = new List<Vector3>(); var archLights = new List<Vector3>(); var seams = new List<Vector3>();
            var tealAccent = new List<Vector3>(); var leafAccent = new List<Vector3>();
            var crownCenter = new Vector3(0,5.65f,2.65f);
            for(int i=0;i<11;i++)
            {
                float angle=(-110+i*22)*Mathf.Deg2Rad;
                Vector3 radial=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle)), tangent=new Vector3(Mathf.Cos(angle),0,-Mathf.Sin(angle));
                var p=radial*6.8f+new Vector3(0,0,2.65f);
                var rotation=Quaternion.Euler(0,angle*Mathf.Rad2Deg,0);
                Metal(world,"Titanium structural rib "+i,PrimitiveType.Cube,p+Vector3.up*2.45f,new Vector3(.16f,4.9f,.26f),graphite).transform.localRotation=rotation;
                Metal(world,"Rib spine",PrimitiveType.Cube,p+radial*.11f+Vector3.up*2.45f,new Vector3(.07f,4.9f,.1f),steel).transform.localRotation=rotation;
                Metal(world,"Rib foundation",PrimitiveType.Cube,p+Vector3.up*.14f,new Vector3(.42f,.28f,.5f),dark).transform.localRotation=rotation;
                for(int side=-1;side<=1;side+=2)
                    HoloGeometry.Path(seams,p-radial*.135f+tangent*side*.062f+Vector3.up*.38f,p-radial*.135f+tangent*side*.062f+Vector3.up*4.62f);
                var accent=i%2==0?tealAccent:leafAccent; float y=.29f;
                HoloGeometry.Path(accent,p+(tangent*.22f-radial*.26f)+Vector3.up*y,p+(tangent*.22f+radial*.26f)+Vector3.up*y,
                    p+(-tangent*.22f+radial*.26f)+Vector3.up*y,p+(-tangent*.22f-radial*.26f)+Vector3.up*y,p+(tangent*.22f-radial*.26f)+Vector3.up*y);
                // Lit arch: a quadratic curve from the pillar head up and over to the crown ring.
                Vector3 a0=p+Vector3.up*4.9f, a2=radial*2.3f+crownCenter, a1=radial*5.1f+new Vector3(0,6.35f,2.65f);
                var arc=new Vector3[11]; var under=new Vector3[11];
                for(int k=0;k<=10;k++){ float t=k/10f, u=1-t; arc[k]=u*u*a0+2*u*t*a1+t*t*a2; under[k]=arc[k]+Vector3.down*.05f; }
                HoloGeometry.Path(arches,arc); HoloGeometry.Path(archLights,under);
            }
            HoloGeometry.Circle(arches,crownCenter,2.3f,64);
            var crown=Ring(world,crownCenter+Vector3.down*.06f,2.22f,EventBranding.Quetzal,.02f,96); crown.name="Crown light run";
            crown.sharedMaterial=Beam(EventBranding.Quetzal); crown.textureMode=LineTextureMode.Stretch;
            HoloGeometry.Strokes(world,"Lit roof arches",arches,graphite,.035f);
            // Light runs along the arches and pillar seams instead of sitting still (decorative motion, reduced-motion aware).
            for(int i=0;i+1<archLights.Count;i+=20) MovingLight(world,"Arch light run",archLights.GetRange(i,20),i/20%2==0?Hex("#3FB8CC"):EventBranding.Quetzal,.014f,.9f);
            for(int i=0;i+1<seams.Count;i+=2) MovingLight(world,"Pillar seam run",seams.GetRange(i,2),i/2%2==0?Hex("#3FB8CC"):EventBranding.Leaf,.016f,.35f+.1f*(i%3));
            HoloGeometry.Strokes(world,"Pillar teal accents",tealAccent,EventBranding.Quetzal,.012f);
            HoloGeometry.Strokes(world,"Pillar leaf accents",leafAccent,EventBranding.Leaf,.012f);
            var roof=new List<Vector3>();
            for(int i=0;i<8;i++) HoloGeometry.Circle(roof,Vector3.zero,2.3f,12,false,i*45,32);
            ceiling=HoloGeometry.Strokes(world,"Suspended ceiling halo",roof,Hex("#286B81"),.022f).transform;
            ceiling.localPosition=new Vector3(0,5.5f,2.65f);
            // Reactor bulkhead: narrower than the old wall so the volcano horizon shows on both sides, with lit panel seams.
            // No back wall or screen: the reactor (ATLAS) floats free in front of the open volcano horizon.
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
            this.lens=lens.transform; lensScale=lens.transform.localScale; lensRenderer=lens.GetComponent<Renderer>();
            reactorRenderer=reactor.GetComponent<Renderer>(); counterRenderer=counterRing.GetComponent<Renderer>();
            // The GuateGeeks eyes sit in the reactor core: ATLAS looks at whoever is in the lab.
            var coreEyes=GeekEyes.Create(world,1.25f,"Reactor GuateGeeks eyes"); coreEyes.transform.localPosition=center+Vector3.back*.42f;
            // GuateGeeks signs float on both sides of the reactor, facing the lab, where the old wall used to be.
            for(int side=-1;side<=1;side+=2) {
                // High enough to clear the catalog and inspector panels from the standing position.
                var at=new Vector3(side*3.9f,3.75f,7.6f);
                EventBranding.GuateGeeksSign(world,side<0?"GuateGeeks sign left":"GuateGeeks sign right",at,2.6f,at-new Vector3(0,1.6f,0));
            }
            // Deliberately decorative, never presented as measured AWS activity.
            // Fine motes in the event palette (quetzal teal, leaf green, amber) drift through the room.
            Color[] motes={EventBranding.Quetzal,Cyan,EventBranding.Leaf,Cyan,Orange};
            for(int i=0;i<36;i++) energy.Add(Shape(world,"Ambient energy marker",PrimitiveType.Sphere,Vector3.zero,Vector3.one*(i%5==4?.03f:.022f),motes[i%motes.Length]).transform);
            // The event logo in the title window is the lab identity; the wall no longer carries a competing caption.
            HoloGeometry.CombineMetal(world);
        }
        Transform[] ribbons;
        Transform aperture;
        DigitalQuetzal quetzal;
        // Strokes are built in world-local space; move the mesh so the transform pivots at its centre for rotation.
        static void ShiftToPivot(Transform t,Vector3 pivot)
        {
            var filter=t.GetComponent<MeshFilter>(); var mesh=filter.sharedMesh; var v=mesh.vertices;
            for(int i=0;i<v.Length;i++) v[i]-=pivot; mesh.vertices=v; mesh.RecalculateBounds();
        }
        Mesh horizonMesh;
        // A 360° horizon band outside the titanium ribs: night sky with teal haze and aurora ribbons in the logo colours,
        // above a holographic range of Guatemalan volcanoes. One inward-facing cylinder, one procedural shader.
        void BuildHorizon(Transform world)
        {
            const int segments = 96; const float radius = 8.4f, bottom = -.2f, top = 11f;
            var vertices = new Vector3[(segments + 1) * 2]; var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * radius;
                vertices[i * 2] = d + Vector3.up * bottom; vertices[i * 2 + 1] = d + Vector3.up * top;
                if (i == segments) break;
                int k = i * 6, v = i * 2;
                triangles[k] = v; triangles[k + 1] = v + 1; triangles[k + 2] = v + 2; triangles[k + 3] = v + 2; triangles[k + 4] = v + 1; triangles[k + 5] = v + 3;
            }
            horizonMesh = new Mesh { name = "Volcano horizon", vertices = vertices, triangles = triangles };
            horizonMesh.bounds = new Bounds(Vector3.up * 5, new Vector3(radius * 2, 12, radius * 2));
            var go = new GameObject("Guatemala volcano horizon", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(world, false); go.transform.localPosition = new Vector3(0, 0, 2.65f);
            go.GetComponent<MeshFilter>().sharedMesh = horizonMesh;
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = SpecialMaterial("LabPanorama", Color.white);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        }
        // Light on the floor: a teal pool under the console and two flowing light ribbons shaped like the logo swooshes.
        void BuildFloorLight(Transform world, Vector3 center)
        {
            var pool = Shape(world, "Console light pool", PrimitiveType.Quad, new Vector3(0, .012f, center.z), new Vector3(9.5f, 9.5f, 1), EventBranding.Quetzal);
            pool.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var glow = EventBranding.Quetzal; glow.a = .32f; pool.GetComponent<Renderer>().sharedMaterial = SpecialMaterial("LabGlow", glow);
            ribbons = new[] { Swoosh(world, "Teal floor ribbon", center, 3.55f, .32f, -140, 118, 0, EventBranding.Quetzal, .34f),
                              Swoosh(world, "Leaf floor ribbon", center, 4.15f, .26f, -118, 146, 1.9f, EventBranding.Leaf, .26f) };
        }
        static Transform Swoosh(Transform world, string name, Vector3 center, float radius, float wave, float from, float to, float phase, Color color, float width)
        {
            var pivot = new GameObject(name).transform; pivot.SetParent(world, false); pivot.localPosition = new Vector3(0, .02f, center.z);
            var points = new Vector3[72];
            for (int i = 0; i < points.Length; i++)
            {
                float u = (float)i / (points.Length - 1), a = Mathf.Lerp(from, to, u) * Mathf.Deg2Rad;
                float r = radius + wave * Mathf.Sin(a * 2 + phase);
                points[i] = new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r);
            }
            var line = Line(pivot, name + " beam", points, color, width);
            line.sharedMaterial = Beam(color); line.textureMode = LineTextureMode.Stretch; line.numCapVertices = 0;
            line.alignment = LineAlignment.TransformZ; line.transform.localRotation = Quaternion.Euler(90, 0, 0);
            // LineRenderer positions are local to the rotated child: map the flat path back into its frame.
            for (int i = 0; i < points.Length; i++) points[i] = new Vector3(points[i].x, points[i].z, 0);
            line.SetPositions(points);
            // Tapered like the logo swooshes: a hairline at both ends, full width through the middle.
            line.widthCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(.35f, 1), new Keyframe(.8f, .85f), new Keyframe(1, 0));
            return pivot;
        }
        void Update()
        {
            float t=LabFeedback.Clock;
            if(scanner) scanner.localRotation=Quaternion.Euler(0,t*3,0);
            float delta=Mathf.Max(0,t-lastClock); lastClock=t;
            level=Mathf.Lerp(level,levelGoal,1-Mathf.Exp(-10*Time.unscaledDeltaTime));
            float boost=assistantActive?1.6f+level*9:1;
            reactorAngle+=delta*5*boost; counterAngle-=delta*8*boost;
            if(reactor) reactor.localRotation=Quaternion.Euler(0,0,reactorAngle);
            if(counterRing) counterRing.localRotation=Quaternion.Euler(0,0,counterAngle);
            if(lens) lens.localScale=delta>0?Vector3.Scale(lensScale,new Vector3(1+level*.09f,1+level*.09f,1+level*.5f)):lensScale;
            if(ceiling) ceiling.localRotation=Quaternion.Euler(0,t*2,0);
            for(int i=0;i<energy.Count;i++) {
                float a=i*Mathf.PI*2/energy.Count+t*(.03f+(i%3)*.012f), r=4.6f+(i%4)*.55f;
                energy[i].localPosition=new Vector3(Mathf.Sin(a)*r,.35f+Mathf.Repeat(i*.37f+t*(.05f+(i%5)*.012f),4.2f),2.65f+Mathf.Cos(a)*r);
            }
            if(ribbons!=null) for(int i=0;i<ribbons.Length;i++) ribbons[i].localRotation=Quaternion.Euler(0,t*(i==0?1.6f:-1.1f),0);
            if(aperture) aperture.localRotation=Quaternion.Euler(0,0,t*2.2f);
        }
    }
}
