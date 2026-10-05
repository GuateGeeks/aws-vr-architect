using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    public sealed partial class ArchitectureLab
    {
        readonly SpatialVoiceContext spatialVoice=new SpatialVoiceContext();
        string[] assistantLastNodes=Array.Empty<string>();
        SpatialVoiceContext.Snapshot assistantPointing;
        float assistantPointingAt;
        GameObject voiceDestinationMarker;
        RectTransform voiceDestinationLabel;
        public bool VoiceDestinationVisible=>voiceDestinationMarker && voiceDestinationMarker.activeSelf;
        void BuildVoiceDestinationMarker()
        {
            voiceDestinationMarker=new GameObject("ATLAS tabletop destination");var root=voiceDestinationMarker.transform;root.SetParent(world,false);
            LabVisuals.Ring(root,Vector3.zero,.10f,LabVisuals.Cyan,.006f,32);
            LabVisuals.Ring(root,new Vector3(0,1.5f-VoiceTableHeight,0),.13f,LabVisuals.Cyan,.005f,32);
            LabVisuals.Line(root,"Destination height",new[]{Vector3.zero,new Vector3(0,1.5f-VoiceTableHeight,0)},LabVisuals.Cyan,.003f);
            voiceDestinationLabel=LabVisuals.Panel(root,"Voice destination label",new Vector3(0,2.02f-VoiceTableHeight,0),new Vector2(420,64),background:true,movable:false);
            voiceDestinationLabel.localScale=Vector3.one*.0015f;
            LabVisuals.Text(voiceDestinationLabel,"AQUÍ · DESTINO DE VOZ",Vector2.zero,new Vector2(400,54),28,LabVisuals.Cyan,TextAnchor.MiddleCenter);
            voiceDestinationMarker.SetActive(false);
        }
        void UpdateVoiceDestinationMarker(SpatialVoiceContext.Snapshot context)
        {
            if(!voiceDestinationMarker)return;
            bool show=AssistantEnabled && (voicePreviewDestination.HasValue || context.hasLocation) && !ConfiguringConnection && !EditingText && assistantVoice?.CanResume!=false;
            voiceDestinationMarker.SetActive(show);if(!show)return;
            var location=voicePreviewDestination??context.location;
            voiceDestinationMarker.transform.localPosition=new Vector3(location.x,VoiceTableHeight+.01f,location.z);
            if(Camera.main)voiceDestinationLabel.rotation=Quaternion.LookRotation(voiceDestinationLabel.position-Camera.main.transform.position);
        }
        public const float VoiceTableHeight=.74f;
        public static bool TryVoiceTableDestination(Transform workspace,Ray ray,out Vector3 destination)
        {
            destination=Vector3.zero;
            if(!new Plane(workspace.up,workspace.TransformPoint(new Vector3(0,VoiceTableHeight,0))).Raycast(ray,out float distance) || distance<=0 || distance>=8)return false;
            var point=workspace.InverseTransformPoint(ray.GetPoint(distance));
            if(point.x< -1.6f || point.x>1.6f || point.z<1.65f || point.z>3.65f || new Vector2(point.x,point.z-2.65f).sqrMagnitude>1.81f*1.81f)return false;
            // Aim at the visible tabletop; the hologram floats above that exact x/z.
            destination=new Vector3(point.x,1.5f,point.z);return true;
        }
        public void ObserveVoicePointer(string source,LabTarget target,Ray ray,bool blocked=false)
        {
            if(!AssistantEnabled || ConfiguringConnection || EditingText)return;
            if(blocked){spatialVoice.Observe(source,null,null,Time.unscaledTime);return;}
            Vector3? location=null;
            // Floating buttons keep their normal click target, but must not hide the table
            // from an explicit voice move. Nodes and menu grab handles retain priority.
            if((!target || (!target.Node && !target.Menu)) && TryVoiceTableDestination(world,ray,out var point))location=point;
            spatialVoice.Observe(source,target && target.Node?target.Node.Model.id:null,location,Time.unscaledTime);
        }
        public void BeginVoicePointing(){voiceResultUntil=0;spatialVoice.BeginSpeech(Time.unscaledTime);}
        public void FreezeVoicePointing(){spatialVoice.Freeze(Time.unscaledTime);assistantPointing=spatialVoice.Read(Time.unscaledTime);}
        SpatialVoiceContext.Snapshot ReadVoicePointing()
        {
            var value=spatialVoice.Read(Time.unscaledTime);
            value.pointers=value.pointers.Where(p=>Graph.Find(p.nodeId)!=null).ToArray();
            value.gestureNodeIds=value.gestureNodeIds.Where(id=>Graph.Find(id)!=null).ToArray();
            assistantPointing=value;assistantPointingAt=Time.unscaledTime;return value;
        }
        string ExecuteSpatialAction(VoiceAction request)
        {
            var ids=request.nodeIds?.Distinct().ToArray();
            if(ids==null || ids.Length==0 || ids.Length>12 || ids.Any(id=>Graph.Find(id)==null))return ActionResult("invalid","Usa IDs existentes del contexto. Aclara cuáles son los objetos si hay ambigüedad.");
            if(request.action=="resize") {
                if(float.IsNaN(request.scale) || request.scale<.5f || request.scale>1.25f)return ActionResult("invalid","Tamaño permitido: 0.5 a 1.25.");
                Remember();foreach(string id in ids){Graph.Find(id).viewScale=request.scale;views[id].transform.localScale=Vector3.one*request.scale;}
            }else if(request.action=="move") {
                if(assistantPointing==null || !assistantPointing.hasLocation || assistantPointing.locationId!=request.locationId || Time.unscaledTime-assistantPointingAt>25 || spatialVoice.Read(Time.unscaledTime).ageSeconds>25)
                    return ActionResult("stale","Apunta a un espacio libre de la mesa y repite la ubicación.");
                var center=ids.Aggregate(Vector3.zero,(sum,id)=>sum+Graph.Find(id).position)/ids.Length;
                var destinations=ids.ToDictionary(id=>id,id=>Graph.Find(id).position+assistantPointing.location-center);
                if(destinations.Any(pair=>(ClampWorkspace(pair.Value)-pair.Value).sqrMagnitude>.0001f || Graph.nodes.Any(n=>!ids.Contains(n.id) && Vector3.Distance(n.position,pair.Value)<.28f*(EffectiveScale(n)+EffectiveScale(Graph.Find(pair.Key))))))
                    return ActionResult("blocked","La ubicación queda fuera de la mesa o demasiado cerca de otro componente.");
                Remember();foreach(var pair in destinations){Graph.Find(pair.Key).position=pair.Value;views[pair.Key].transform.localPosition=pair.Value;}
            }else return ActionResult("unsupported","Acción espacial no disponible.");
            assistantLastNodes=ids;return ActionResult("applied","Vista actualizada para "+string.Join(", ",ids.Select(id=>Graph.Find(id).name))+". El despliegue AWS no cambió.");
        }
        float EffectiveScale(ResourceNode node)=>node.viewScale>=.5f && node.viewScale<=1.25f?node.viewScale:ComponentScale;
        readonly Dictionary<Architecture,float> historyScales=new Dictionary<Architecture,float>();
        void RestoreViewScale(float scale)
        {
            ComponentScale=scale;PlayerPrefs.SetFloat(ComponentScalePreference,scale);PlayerPrefs.Save();
            foreach(var view in views.Values)view.transform.localScale=Vector3.one*EffectiveScale(view.Model);
            RefreshComponentSize();
        }
        void RememberSnapshot(Architecture snapshot,float scale)
        {
            history.Push(snapshot);historyScales[snapshot]=scale;
            if(history.Count>30){var latest=history.Take(30).Reverse().ToArray();history.Clear();foreach(var item in latest)history.Push(item);}
            foreach(var old in historyScales.Keys.Where(k=>!history.Contains(k)).ToArray())historyScales.Remove(old);
        }
        public string ExecuteVoiceAction(string tool,string arguments)
        {
            var before=Graph.Copy();var beforeJson=JsonUtility.ToJson(before);float scale=ComponentScale;var previousHistory=history.ToArray();var previousScales=new Dictionary<Architecture,float>(historyScales);
            string result=ExecuteVoiceActionCore(tool,arguments);
            var parsed=JsonUtility.FromJson<VoiceActionResult>(result);
            bool changed=beforeJson!=JsonUtility.ToJson(Graph) || scale!=ComponentScale;
            VoiceAction request=null;try{if(arguments!=null && arguments.Length<=4000)request=JsonUtility.FromJson<VoiceAction>(arguments);}catch(ArgumentException){}
            if(changed && request?.action!="undo") {
                // One user action (including size + arrange) is one undo step.
                history.Clear();foreach(var entry in previousHistory.Reverse())history.Push(entry);
                historyScales.Clear();foreach(var pair in previousScales)historyScales[pair.Key]=pair.Value;
                RememberSnapshot(before,scale);
                assistantAppliedRevision=revision;assistantAppliedSnapshot=JsonUtility.ToJson(Graph);assistantUndoEntry=history.Peek();
            }
            string[] affected=request?.nodeIds??new[]{parsed.nodeId,request?.nodeId,request?.from,request?.to};
            affected=affected.Where(id=>!string.IsNullOrEmpty(id) && Graph.Find(id)!=null).Distinct().ToArray();
            if(affected.Length>0)assistantLastNodes=affected;
            ShowVoiceActionResult(parsed.status,parsed.message,affected);
            return result;
        }
    }
}
