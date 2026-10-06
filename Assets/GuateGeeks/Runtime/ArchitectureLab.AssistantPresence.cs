using System;
using System.Linq;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    public sealed partial class ArchitectureLab
    {
        RectTransform assistantPresence;
        TMPro.TMP_Text presenceState,presenceTarget,presenceCaption,presenceResult;
        LabTarget presenceOff,presenceStop,presenceUndo,presenceCaptions,presenceApply;
        TMPro.TMP_Text presenceCost;
        float voicePreviewUntil;bool voicePreviewApproved;
        GameObject voicePreviewGeometry;
        Vector3? voicePreviewDestination;
        public const float VoicePreviewSeconds=3.5f;
        public void ApplyVoicePreviewNow(){if(voicePreviewActive)voicePreviewApproved=true;}
        public void CancelPendingVoiceAction(){assistantVoice?.Interrupt();CancelVoicePreview();voiceResult="Acción cancelada · diseño sin cambios.";voiceResultUntil=Time.unscaledTime+5;}
        LineRenderer presenceOrb;
        Transform presenceSpin,presenceCounter;
        UnityEngine.UI.Image[] presenceBars;
        bool assistantWasEnabled;
        Color AssistantStateColor(string state)=>state=="VISTA PREVIA" || state=="REVISAR"?Orange:state=="HECHO"?Green:state=="EN PAUSA" || state=="REVISAR MICRÓFONO" || state=="APAGADO"?Muted:Cyan;
        // Runs every frame: the room reactor and the compact orb breathe with the microphone and ATLAS speech.
        void AnimateAssistantCore()
        {
            if(AssistantEnabled && !assistantWasEnabled)Feedback?.Play(LabFeedback.Wake);
            assistantWasEnabled=AssistantEnabled;
            string state=AssistantEnabled?AssistantPresenceState:"APAGADO";
            float level=assistantVoice?Mathf.Clamp01(Mathf.Sqrt(assistantVoice.MicrophoneLevel)*5):0;
            bool speaking=assistantVoice && assistantVoice.Speaking;
            if(speaking)level=Mathf.Max(level,.35f+.45f*Mathf.PerlinNoise(Time.unscaledTime*7,.3f));
            if(state=="PENSANDO")level=Mathf.Max(level,.25f);
            Color color=AssistantStateColor(state);
            // The central reactor is shared decor. Private conversation animates only the personal orb.
            HoloEnvironment.Current?.SetAssistant(NetworkRoom == null && AssistantEnabled,color,NetworkRoom == null ? level : 0);
            if(!presenceSpin || !assistantPresence.gameObject.activeInHierarchy)return;
            bool reduced=LabFeedback.Current && LabFeedback.Current.ReducedMotion;
            float clock=LabFeedback.Clock*(1+level*3);
            presenceSpin.localRotation=Quaternion.Euler(0,0,clock*40);
            presenceCounter.localRotation=Quaternion.Euler(0,0,-clock*65);
            for(int i=0;i<presenceBars.Length;i++){
                float shape=1-Mathf.Abs(i-3)/4f;
                float wave=reduced?.5f:.5f+.5f*Mathf.Sin(Time.unscaledTime*(9+i*1.7f)+i*1.3f);
                float h=4+34*level*shape*(.45f+.55f*wave);
                presenceBars[i].rectTransform.sizeDelta=new Vector2(3.5f,h);
                presenceBars[i].color=color;
            }
        }
        string assistantCaption="Habla con naturalidad. Puedes señalar objetos mientras hablas.",voiceResult="",voicePreviewLabel="";
        float voiceResultUntil,nextPresenceRefresh;
        bool voicePreviewActive,voiceResultSucceeded=true;
        string[] previewNodes=Array.Empty<string>();
        public bool NaturalConversation=>PlayerPrefs.GetInt("GuateGeeks.AtlasNaturalConversation",1)==1;
        bool AssistantCaptions=>PlayerPrefs.GetInt("GuateGeeks.AtlasCaptions",1)==1;
        public bool VoicePreviewActive=>voicePreviewActive;
        public string AssistantPresenceState {
            get {
                if(!AssistantEnabled)return "APAGADO";
                if(ConfiguringConnection || credentialBusy || inspectionSuspended || assistantVoice?.CanResume==false)return "EN PAUSA";
                if(voicePreviewActive)return "VISTA PREVIA";
                if(assistantVoice?.UserSpeaking==true)return "ESCUCHANDO";
                if(assistantVoice?.Speaking==true)return "HABLANDO";
                if(Time.unscaledTime<voiceResultUntil)return voiceResultSucceeded?"HECHO":"REVISAR";
                if(assistantVoice?.Responding==true || assistantToolRoutine!=null)return "PENSANDO";
                if(assistantVoice?.Listening==true)return "ESCUCHANDO";
                return assistantVoice?.Connected==true?"REVISAR MICRÓFONO":"CONECTANDO";
            }
        }
        void BuildAssistantPresence()
        {
            BuildVoiceDestinationMarker();
            // Upper-left periphery, tilted toward the user: present like a HUD readout without covering
            // the title, the catalog or the architecture on the table.
            assistantPresence=Panel(PersonalRoot,"ATLAS compact presence",new Vector3(-1.12f,2.5f,1.9f),new Vector2(670,450));
            assistantPresence.localRotation=Quaternion.Euler(-12,-30,0);
            assistantPresence.localScale=Vector3.one*.00125f;
            assistantPresence.GetComponent<HoloPanelGraphic>().color=new Color(.025f,.065f,.085f,1);
            // A miniature reactor: counter-rotating arcs around a live voice waveform, tinted by assistant state.
            var orbCenter=new Vector3(-282,122,-1);
            var orbPoints=new Vector3[40];for(int i=0;i<orbPoints.Length;i++){float a=i*Mathf.PI*2/orbPoints.Length;orbPoints[i]=orbCenter+new Vector3(Mathf.Cos(a)*27,Mathf.Sin(a)*27,0);}
            presenceOrb=Line(assistantPresence,"ATLAS signal",orbPoints,Cyan,.003f,true);
            presenceSpin=new GameObject("ATLAS orbit").transform;presenceSpin.SetParent(assistantPresence,false);presenceSpin.localPosition=orbCenter;
            var arcs=new System.Collections.Generic.List<Vector3>();
            for(int i=0;i<3;i++)HoloGeometry.Circle(arcs,Vector3.zero,38,12,true,i*120,80);
            HoloGeometry.Strokes(presenceSpin,"ATLAS orbit arcs",arcs,Cyan,1.6f);
            presenceCounter=new GameObject("ATLAS counter orbit").transform;presenceCounter.SetParent(assistantPresence,false);presenceCounter.localPosition=orbCenter;
            var counter=new System.Collections.Generic.List<Vector3>();
            for(int i=0;i<6;i++)HoloGeometry.Circle(counter,Vector3.zero,47,6,true,i*60,22);
            HoloGeometry.Strokes(presenceCounter,"ATLAS counter arcs",counter,Orange,1.2f);
            presenceBars=new UnityEngine.UI.Image[7];
            for(int i=0;i<presenceBars.Length;i++)presenceBars[i]=Block(assistantPresence,new Vector2(orbCenter.x+(i-3)*6.5f,orbCenter.y),new Vector2(3.5f,4),Cyan);
            presenceState=Text(assistantPresence,"ATLAS · ESCUCHANDO",new Vector2(48,125),new Vector2(515,38),24,Cyan);
            presenceTarget=Text(assistantPresence,"Apunta a un componente o a un lugar de la mesa.",new Vector2(0,76),new Vector2(620,42),18,Muted);
            presenceCaption=Text(assistantPresence,assistantCaption,new Vector2(0,9),new Vector2(620,81),21,White);
            presenceResult=Text(assistantPresence,"",new Vector2(0,-70),new Vector2(620,57),18,Green);
            presenceCost=Text(assistantPresence,"",new Vector2(0,-209),new Vector2(620,26),16,Muted,TextAnchor.MiddleCenter);
            presenceApply=Button(assistantPresence,"Aplicar ahora",new Vector2(-155,-125),new Vector2(300,42),ApplyVoicePreviewNow,Green);
            presenceApply.gameObject.SetActive(false);
            presenceStop=Button(assistantPresence,"Detener",new Vector2(165,-125),new Vector2(300,42),()=>{assistantVoice?.Interrupt();CancelVoicePreview();voiceResult="Acción / respuesta detenida. Puedes seguir hablando.";voiceResultUntil=Time.unscaledTime+4;});
            Button(assistantPresence,"Panel",new Vector2(-240,-174),new Vector2(145,46),OpenAssistant);
            presenceCaptions=Button(assistantPresence,"Subtítulos: sí",new Vector2(-80,-174),new Vector2(145,46),()=>{if(NetworkRoom != null) {roomTalkLatched=!roomTalkLatched;return;}PlayerPrefs.SetInt("GuateGeeks.AtlasCaptions",AssistantCaptions?0:1);PlayerPrefs.Save();});
            presenceUndo=Button(assistantPresence,"Deshacer",new Vector2(80,-174),new Vector2(145,46),()=>{UndoAssistant();voiceResult="Cambio deshecho.";voiceResultUntil=Time.unscaledTime+4;});
            presenceOff=Button(assistantPresence,"Desactivar",new Vector2(240,-174),new Vector2(145,46),()=>SetAssistantEnabled(false),Orange);
            foreach(var text in new[]{presenceState,presenceTarget,presenceCaption,presenceResult}){text.richText=false;text.enableAutoSizing=true;text.fontSizeMin=16;text.fontSizeMax=text.fontSize;}
            foreach(var target in new[]{presenceCaptions,presenceUndo,presenceOff,presenceStop}){target.Label.enableAutoSizing=true;target.Label.fontSizeMin=15;target.Label.fontSizeMax=20;}
            assistantPresence.gameObject.SetActive(false);
        }
        void SetNaturalConversation(bool natural)
        {
            PlayerPrefs.SetInt("GuateGeeks.AtlasNaturalConversation",natural?1:0);PlayerPrefs.Save();assistantVoice?.ConfigureTurnDetection(natural);
            voiceResult=natural?"Conversación natural: espera a que termines la idea.":"Conversación rápida: responde después de una pausa.";voiceResultUntil=Time.unscaledTime+5;
        }
        void AskAssistantText(string text){FreezeVoicePointing();assistantVoice?.Ask(text);}
        void UpdatePresenceButtons()
        {
            if(!assistantPresence)return;
            presenceUndo.SetAvailable(!voicePreviewActive && AssistantCanEdit && AssistantCanUndo);
            presenceApply.gameObject.SetActive(voicePreviewActive);
            presenceStop.Label.text=voicePreviewActive?"Cancelar acción":"Interrumpir";
            presenceCost.text=assistantVoice?assistantVoice.UsageCost.Summary:"USD ~0.0000 · esta ejecución";
            presenceCaptions.Label.text=NetworkRoom != null ? (roomTalkLatched ? "Silenciar mic" : "Hablar") : AssistantCaptions?"Subtítulos: sí":"Subtítulos: no";
        }
        void TickAssistantPresence()
        {
            if(!assistantPresence || !AssistantEnabled)return;
            if(Time.unscaledTime<nextPresenceRefresh)return;nextPresenceRefresh=Time.unscaledTime+.1f;
            assistantPresence.gameObject.SetActive(true);
            string state=AssistantPresenceState;
            Color color=AssistantStateColor(state);
            presenceState.text="ATLAS · "+state;presenceState.color=color;presenceOrb.sharedMaterial=Material(color);
            float level=assistantVoice?Mathf.Clamp01(Mathf.Sqrt(assistantVoice.MicrophoneLevel)*5):0;
            presenceOrb.widthMultiplier=LabFeedback.Current && LabFeedback.Current.ReducedMotion?.003f:.003f+level*.003f;
            var context=assistantVoice?.Responding==true?spatialVoice.Read(Time.unscaledTime):spatialVoice.Preview(Time.unscaledTime);
            UpdateVoiceDestinationMarker(context);
            var pointing=context.pointers.Select(p=>Graph.Find(p.nodeId)?.name).Where(n=>n!=null).Distinct().ToArray();
            presenceTarget.text=voicePreviewActive?voicePreviewLabel:context.ambiguous?"Varios destinos: apunta con una sola mano.":context.hasLocation?"Destino AQUÍ en la mesa · di «muévelo aquí»":pointing.Length>0?"Señalando: "+string.Join(" · ",pointing):"Apunta a la superficie de la mesa hasta ver AQUÍ.";
            if(NetworkRoom != null && !voicePreviewActive && !roomTalkLatched)presenceTarget.text="Mantén A / Espacio para hablar · con manos usa Hablar / Silenciar mic.";
            presenceCaption.text=AssistantCaptions?Tail(assistantCaption,210):"Subtítulos ocultos · el audio sigue activo";
            presenceResult.text=voicePreviewActive?"Se aplicará en "+Mathf.Max(0,voicePreviewUntil-Time.unscaledTime).ToString("0.0")+" s · habla para cancelar":Time.unscaledTime<voiceResultUntil?Tail(voiceResult,150):NaturalConversation?"Conversación natural · micrófono continuo":"Conversación rápida · micrófono continuo";
            UpdatePresenceButtons();
        }
        static string Tail(string text,int limit)=>string.IsNullOrEmpty(text)?"":text.Length<=limit?text:"…"+text.Substring(text.Length-limit);
        bool PreviewVoiceCall(string tool,string arguments)
        {
            if(tool!="component_action" && tool!="connection_action" && tool!="spatial_action" && tool!="set_component_size" && tool!="ui_action")return false;
            if(string.IsNullOrEmpty(arguments) || arguments.Length>4000)return false;
            VoiceAction request;try{request=JsonUtility.FromJson<VoiceAction>(arguments);}catch(ArgumentException){return false;}
            if(request==null || request.baseRevision!=revision || !AssistantCanEdit)return false;
            bool mutation=tool!="ui_action" || new[]{"apply_proposal","load","undo","arrange","apply_changes","confirm_placement"}.Contains(request.action);
            if(!mutation || request.action=="select")return false;
            previewNodes=(request.nodeIds??new[]{request.nodeId,request.from,request.to}).Where(id=>!string.IsNullOrEmpty(id) && Graph.Find(id)!=null).Distinct().ToArray();
            if(previewNodes.Length==0 && (tool=="set_component_size" || request.action=="arrange"))previewNodes=Graph.nodes.Select(n=>n.id).ToArray();
            string operation=tool=="set_component_size"?"Cambiar tamaño":request.action=="add"?"Agregar "+request.name:request.action=="remove"?"Quitar del diseño":request.action=="connect"?"Conectar":request.action=="disconnect"?"Desconectar":request.action=="move"?"Mover":request.action=="resize"?"Cambiar tamaño":"Actualizar diseño";
            if(request.action=="update" && Graph.Find(request.nodeId)!=null){var node=Graph.Find(request.nodeId);var settings=ServiceCatalog.Get(node.kind).Settings;operation="Actualizar: "+request.name+(request.setting>=0 && request.setting<settings.Length?" / "+settings[request.setting]:"");}
            voicePreviewLabel=operation+(previewNodes.Length==0?"":": "+string.Join(" → ",previewNodes.Select(id=>Graph.Find(id).name)));
            voicePreviewDestination=null;
            if(tool=="spatial_action" && request.action=="move" && assistantPointing?.hasLocation==true && assistantPointing.locationId==request.locationId) {
                voicePreviewDestination=assistantPointing.location;voicePreviewLabel+=" → AQUÍ";
            }
            if(tool=="component_action" && request.action=="add")voicePreviewDestination=NextResourcePosition();
            if(tool=="set_component_size" || request.action=="resize")voicePreviewLabel+=" · "+Mathf.RoundToInt(request.scale*100)+"%";
            voicePreviewUntil=Time.unscaledTime+VoicePreviewSeconds;voicePreviewApproved=false;
            voicePreviewActive=true;foreach(var id in previewNodes)views[id].FlashAssistant(true,VoicePreviewSeconds+.5f);
            BuildVoiceActionGeometry(request);
            if(assistantPresence)assistantPresence.gameObject.SetActive(true);
            nextPresenceRefresh=0;TickAssistantPresence();
            if(previewNodes.Length>0)assistantLastNodes=previewNodes.ToArray();
            return true;
        }
        void CancelVoicePreview()
        {
            voicePreviewActive=false;voicePreviewApproved=false;voicePreviewDestination=null;
            if(voicePreviewGeometry){voicePreviewGeometry.SetActive(false);Destroy(voicePreviewGeometry);voicePreviewGeometry=null;}
            foreach(var id in previewNodes)if(views.TryGetValue(id,out var view))view.FlashAssistant(true,0);
            previewNodes=Array.Empty<string>();
        }
        void BuildVoiceActionGeometry(VoiceAction request)
        {
            if(voicePreviewGeometry)Destroy(voicePreviewGeometry);
            voicePreviewGeometry=new GameObject("ATLAS proposed action");voicePreviewGeometry.transform.SetParent(workspace,false); // design space
            var root=voicePreviewGeometry.transform;float rise=.45f+.128f*(Table.LabelScale-1);
            Vector3 center=previewNodes.Length==0?Vector3.zero:previewNodes.Aggregate(Vector3.zero,(sum,id)=>sum+Graph.Find(id).position)/previewNodes.Length;
            foreach(string id in previewNodes) {
                var node=Graph.Find(id);float scale=EffectiveScale(node);
                Ring(root,node.position+Vector3.down*.25f,.31f*scale,Orange,.006f,32);
                var label=Panel(root,"Action target "+id,node.position+Vector3.up*rise,new Vector2(410,54),movable:false);
                label.localScale=Vector3.one*.0013f*Table.LabelScale;
                Text(label,node.name,Vector2.zero,new Vector2(395,50),23,Orange,TextAnchor.MiddleCenter).richText=false;
                if(Camera.main)label.rotation=Quaternion.LookRotation(label.position-Camera.main.transform.position);
                if(voicePreviewDestination.HasValue) {
                    var dest=node.position+voicePreviewDestination.Value-center;
                    Ring(root,dest,.30f*scale,Cyan,.006f,40);
                    Ring(root,dest+Vector3.up*.20f,.22f*scale,Cyan,.004f,32);
                    Line(root,"Proposed movement",new[]{node.position,dest},Cyan,.006f);
                }
            }
            if(request.action=="add" && voicePreviewDestination.HasValue) {
                Ring(root,voicePreviewDestination.Value,.30f*ComponentScale,Cyan,.006f,40);
                var label=Panel(root,"New component preview",voicePreviewDestination.Value+Vector3.up*rise,new Vector2(420,54),movable:false);
                label.localScale=Vector3.one*.0013f*Table.LabelScale;
                Text(label,"Agregar: "+request.name,Vector2.zero,new Vector2(405,50),23,Cyan,TextAnchor.MiddleCenter).richText=false;
                if(Camera.main)label.rotation=Quaternion.LookRotation(label.position-Camera.main.transform.position);
            }
            if(request.action=="connect" || request.action=="disconnect") {
                var from=Graph.Find(request.from);var to=Graph.Find(request.to);
                if(from!=null && to!=null)Line(root,"Proposed connection",new[]{from.position,to.position},request.action=="connect"?Green:Orange,.009f);
            }
            foreach(var line in root.GetComponentsInChildren<LineRenderer>())line.widthMultiplier*=Table.Stroke;
        }
        void ShowVoiceActionResult(string status,string message,string[] affected)
        {
            voiceResult=message;voiceResultUntil=Time.unscaledTime+6;voiceResultSucceeded=status=="applied" || status=="completed" || status=="selected" || status=="review_opened" || status=="started";
            if(presenceResult)presenceResult.color=voiceResultSucceeded?Green:Orange;
            if(status!="applied" && status!="completed" && status!="selected")return;
            foreach(string id in affected)if(views.TryGetValue(id,out var view))view.FlashAssistant(false,2);
            foreach(var link in linkViews)if(affected.Contains(link.FromId) || affected.Contains(link.ToId))link.FlashAssistant();
        }
    }
}
