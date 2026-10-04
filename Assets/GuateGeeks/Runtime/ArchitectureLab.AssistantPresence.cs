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
        LabTarget presenceOff,presenceStop,presenceUndo,presenceCaptions;
        LineRenderer presenceOrb;
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
                if(voicePreviewActive)return "ACTUANDO";
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
            assistantPresence=Panel(world,"ATLAS compact presence",new Vector3(-.2f,2.4f,1.65f),new Vector2(670,360));
            assistantPresence.localScale=Vector3.one*.0011f;
            assistantPresence.GetComponent<HoloPanelGraphic>().color=new Color(.025f,.065f,.085f,1);
            var orbPoints=new Vector3[32];for(int i=0;i<orbPoints.Length;i++){float a=i*Mathf.PI*2/orbPoints.Length;orbPoints[i]=new Vector3(-280+Mathf.Cos(a)*18,125+Mathf.Sin(a)*18,-1);}
            presenceOrb=Line(assistantPresence,"ATLAS signal",orbPoints,Cyan,.003f,true);
            presenceState=Text(assistantPresence,"ATLAS · ESCUCHANDO",new Vector2(30,125),new Vector2(545,38),24,Cyan);
            presenceTarget=Text(assistantPresence,"Apunta a un componente o a un lugar de la mesa.",new Vector2(0,76),new Vector2(620,42),18,Muted);
            presenceCaption=Text(assistantPresence,assistantCaption,new Vector2(0,9),new Vector2(620,81),21,White);
            presenceResult=Text(assistantPresence,"",new Vector2(-80,-70),new Vector2(445,57),18,Green);
            presenceStop=Button(assistantPresence,"Detener",new Vector2(245,-70),new Vector2(140,46),()=>{assistantVoice?.Interrupt();CancelVoicePreview();voiceResult="Acción / respuesta detenida. Puedes seguir hablando.";voiceResultUntil=Time.unscaledTime+4;});
            Button(assistantPresence,"Panel",new Vector2(-240,-137),new Vector2(145,46),OpenAssistant);
            presenceCaptions=Button(assistantPresence,"Subtítulos: sí",new Vector2(-80,-137),new Vector2(145,46),()=>{PlayerPrefs.SetInt("GuateGeeks.AtlasCaptions",AssistantCaptions?0:1);PlayerPrefs.Save();});
            presenceUndo=Button(assistantPresence,"Deshacer",new Vector2(80,-137),new Vector2(145,46),()=>{UndoAssistant();voiceResult="Cambio deshecho.";voiceResultUntil=Time.unscaledTime+4;});
            presenceOff=Button(assistantPresence,"Desactivar",new Vector2(240,-137),new Vector2(145,46),()=>SetAssistantEnabled(false),Orange);
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
            presenceUndo.SetAvailable(AssistantCanEdit && AssistantCanUndo);
            presenceCaptions.Label.text=AssistantCaptions?"Subtítulos: sí":"Subtítulos: no";
        }
        void TickAssistantPresence()
        {
            if(!assistantPresence || !AssistantEnabled)return;
            if(Time.unscaledTime<nextPresenceRefresh)return;nextPresenceRefresh=Time.unscaledTime+.1f;
            assistantPresence.gameObject.SetActive(true);
            string state=AssistantPresenceState;
            Color color=state=="ACTUANDO" || state=="REVISAR"?Orange:state=="HECHO"?Green:state=="EN PAUSA" || state=="REVISAR MICRÓFONO"?Muted:Cyan;
            presenceState.text="ATLAS · "+state;presenceState.color=color;presenceOrb.sharedMaterial=Material(color);
            float level=assistantVoice?Mathf.Clamp01(Mathf.Sqrt(assistantVoice.MicrophoneLevel)*5):0;
            presenceOrb.widthMultiplier=LabFeedback.Current && LabFeedback.Current.ReducedMotion?.003f:.003f+level*.003f;
            var context=assistantVoice?.Responding==true?spatialVoice.Read(Time.unscaledTime):spatialVoice.Preview(Time.unscaledTime);
            UpdateVoiceDestinationMarker(context);
            var pointing=context.pointers.Select(p=>Graph.Find(p.nodeId)?.name).Where(n=>n!=null).Distinct().ToArray();
            presenceTarget.text=voicePreviewActive?voicePreviewLabel:context.ambiguous?"Varios destinos: apunta con una sola mano.":context.hasLocation?"Destino AQUÍ en la mesa · di «muévelo aquí»":pointing.Length>0?"Señalando: "+string.Join(" · ",pointing):"Apunta a la superficie de la mesa hasta ver AQUÍ.";
            presenceCaption.text=AssistantCaptions?Tail(assistantCaption,210):"Subtítulos ocultos · el audio sigue activo";
            presenceResult.text=voicePreviewActive?"Vista previa · habla o pulsa Detener para cancelar":Time.unscaledTime<voiceResultUntil?Tail(voiceResult,150):NaturalConversation?"Conversación natural · micrófono continuo":"Conversación rápida · micrófono continuo";
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
            voicePreviewLabel=operation+(previewNodes.Length==0?"":": "+string.Join(" → ",previewNodes.Select(id=>Graph.Find(id).name)));
            voicePreviewActive=true;foreach(var id in previewNodes)views[id].FlashAssistant(true,1);
            if(previewNodes.Length>0)assistantLastNodes=previewNodes.ToArray();
            return true;
        }
        void CancelVoicePreview()
        {
            voicePreviewActive=false;foreach(var id in previewNodes)if(views.TryGetValue(id,out var view))view.FlashAssistant(true,0);
            previewNodes=Array.Empty<string>();
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
