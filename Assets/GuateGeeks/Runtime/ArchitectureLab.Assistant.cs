using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    public sealed partial class ArchitectureLab
    {
        RectTransform assistantPanel;
        TMPro.TMP_Text assistantState,assistantUser,assistantSpeech,assistantChanges,assistantPageLabel,assistantMicrophone;
        LabTarget assistantConnect,assistantApply,assistantUndo,assistantDock;
        public bool AssistantEnabled {get;private set;}
        float assistantRetryAt;int assistantFailures;
        RealtimeVoice assistantVoice;
        AwsCloudApi assistantBroker,assistantSource,assistantInspector;
        Coroutine assistantConnecting,assistantToolRoutine;
        readonly Queue<AssistantCall> assistantCalls=new Queue<AssistantCall>();
        Architecture assistantProposalGraph,assistantUndoEntry;
        string assistantBaseSnapshot,assistantDifference,assistantAppliedSnapshot;
        int assistantBaseRevision,assistantAppliedRevision=-1,assistantDiffPage;
        float assistantNextUpdate;
        [Serializable] sealed class AssistantCall {public string id,name,args;}
        [Serializable] sealed class AssistantNodeRequest {public string nodeId;}
        [Serializable] sealed class AssistantResult {public string status,message;}
        [Serializable] sealed class AssistantSetting {public int kind;public string name;public string[] settings;}
        [Serializable] sealed class AssistantContext {
            public int revision;public Architecture architecture;public string selectedNodeId,mode,stackId,lastEventId;
            public SpatialVoiceContext.Snapshot pointing;public string[] lastReferencedNodeIds;
            public bool deployed,pendingProposal,canEdit,diagnosticsActive;public string lambdaCodeSupport;public float componentScale;public string[] availableActions;public string[] validation;public AssistantSetting[] catalog;
        }
        public bool AssistantHasProposal=>assistantProposalGraph!=null;
        public int ArchitectureRevision=>revision;
        bool AssistantCanEdit=>!Busy && !codeBusy && !EditingText && !ConfiguringConnection && !Placing && !HasPendingDefinition && !views.Values.Any(v=>v.Grabbed);
        bool AssistantCanUndo=>assistantAppliedRevision==revision && history.Count>0 && ReferenceEquals(history.Peek(),assistantUndoEntry) && JsonUtility.ToJson(Graph)==assistantAppliedSnapshot;
        static string AssistantReply(string status,string message)=>JsonUtility.ToJson(new AssistantResult{status=status,message=message});
        public string AssistantContextJson()=>JsonUtility.ToJson(new AssistantContext {
            revision=revision,architecture=Graph.Copy(),pointing=ReadVoicePointing(),lastReferencedNodeIds=assistantLastNodes.Where(id=>Graph.Find(id)!=null).ToArray(),canEdit=AssistantCanEdit,componentScale=ComponentScale,availableActions=AvailableVoiceActions(),selectedNodeId=selected?selected.Model.id:"",mode=IsCloud?"AWS":"SIMULATION",
            deployed=Deployed,stackId=Cloud?.StackId??"",lastEventId=Cloud?.LastEventId??"",pendingProposal=AssistantHasProposal,diagnosticsActive=DiagnosticsActive,lambdaCodeSupport="Python 3.13 index.py <=8 KiB. Draft, syntax validation, isolated tests. Publish/restore require a physical UI click and loaded AWS revision.",
            validation=Graph.Validate().ToArray(),catalog=ServiceCatalog.All.Select(d=>new AssistantSetting{kind=(int)d.Kind,name=d.Name,settings=d.Settings.Take(DesignSemantics.OptionCount(d.Kind)).ToArray()}).ToArray()
        });
        public void OpenAssistant()
        {
            if((ConfiguringConnection || credentialBusy) && !AssistantEnabled)return;
            if(!assistantPanel)BuildAssistant();
            assistantPanel.gameObject.SetActive(true);UpdateAssistantControls();
        }
        void BuildAssistant()
        {
            BuildAssistantPresence();
            assistantPanel=Panel(world,"ATLAS assistant",new Vector3(0,2.05f,1.1f),new Vector2(1060,1080));assistantPanel.localScale=Vector3.one*.0013f;
            Text(assistantPanel,"A T L A S  /  ASISTENTE DE ARQUITECTURA",new Vector2(0,475),new Vector2(990,60),29,Cyan);
            assistantState=Text(assistantPanel,"Activa ATLAS una vez. Escuchará hasta que lo desactives.",new Vector2(0,415),new Vector2(980,55),22,Green);
            assistantMicrophone=Text(assistantPanel,"MICRÓFONO APAGADO",new Vector2(0,373),new Vector2(980,28),19,Muted);
            assistantUser=Text(assistantPanel,"Tú: crea una API de pedidos con Lambda y DynamoDB.",new Vector2(0,319),new Vector2(980,72),22,Muted);
            assistantSpeech=Text(assistantPanel,"Se comparte el diseño y el objeto seleccionado. Logs/ítems se envían solo al pedir inspección. Mientras ATLAS esté activo se transmite audio a OpenAI y genera consumo.",new Vector2(0,160),new Vector2(980,218),25,White);
            assistantChanges=Text(assistantPanel,"Las propuestas aparecen aquí antes de aplicarse. Deshacer recupera el diseño anterior.",new Vector2(0,-67),new Vector2(980,190),22,Cyan);
            assistantPageLabel=Text(assistantPanel,"",new Vector2(0,-190),new Vector2(540,36),20,Muted,TextAnchor.MiddleCenter);
            foreach(var text in new[]{assistantState,assistantUser,assistantSpeech,assistantChanges,assistantPageLabel}){text.richText=false;text.enableAutoSizing=true;text.fontSizeMin=18;text.fontSizeMax=text.fontSize;}
            Button(assistantPanel,"‹ Cambios",new Vector2(-380,-190),new Vector2(215,42),()=>ShowAssistantDifference(assistantDiffPage-1));
            Button(assistantPanel,"Cambios ›",new Vector2(380,-190),new Vector2(215,42),()=>ShowAssistantDifference(assistantDiffPage+1));
            assistantConnect=Button(assistantPanel,"Activar ATLAS",new Vector2(-165,-262),new Vector2(640,58),()=>SetAssistantEnabled(!AssistantEnabled),Green);
            Button(assistantPanel,"Escribir",new Vector2(330,-262),new Vector2(310,58),()=>{
                if(assistantVoice?.Connected!=true || !AssistantCanEdit)return;
                OpenDesignKeyboard("PEDIR A ATLAS","",600,text=>AskAssistantText(text),false,"Enviar a ATLAS","Cancelar mensaje");
            });
            assistantApply=Button(assistantPanel,"Aplicar propuesta",new Vector2(-330,-337),new Vector2(310,58),ApplyAssistantProposal,Green);
            Button(assistantPanel,"Descartar",new Vector2(0,-337),new Vector2(310,58),()=>{DiscardAssistantProposal();assistantVoice?.ApplicationNotice("The user discarded the pending proposal. No graph change occurred.");});
            assistantUndo=Button(assistantPanel,"Deshacer IA",new Vector2(330,-337),new Vector2(310,58),UndoAssistant);
            Button(assistantPanel,"Interrumpir respuesta",new Vector2(-250,-412),new Vector2(480,58),()=>{assistantVoice?.Interrupt();assistantState.text="RESPUESTA DETENIDA · puedes seguir hablando";});
            Button(assistantPanel,"Cerrar ATLAS",new Vector2(250,-412),new Vector2(480,58),()=>assistantPanel.gameObject.SetActive(false));
            Button(assistantPanel,"Conversación: natural",new Vector2(-250,-462),new Vector2(480,36),()=>SetNaturalConversation(true));
            Button(assistantPanel,"Conversación: rápida",new Vector2(250,-462),new Vector2(480,36),()=>SetNaturalConversation(false));
            Text(assistantPanel,"Cerrar oculta el panel · Desactivar apaga el micrófono · AWS requiere revisión",new Vector2(0,-508),new Vector2(980,28),17,Muted,TextAnchor.MiddleCenter);
        }
        public void SetAssistantEnabled(bool enabled)
        {
            if(!enabled){StopAssistant();UpdateAssistantControls();return;}
            if(!assistantPanel)BuildAssistant();
            AssistantEnabled=true;assistantFailures=0;assistantRetryAt=0;
            assistantPanel.gameObject.SetActive(false);assistantPresence.gameObject.SetActive(true);
            ConnectAssistant();UpdateAssistantControls();
        }
        void ConnectAssistant()
        {
            if(!AssistantEnabled || inspectionSuspended || ConfiguringConnection || credentialBusy || assistantVoice?.CanResume==false || assistantConnecting!=null || assistantVoice?.Connected==true || assistantVoice?.Connecting==true)return;
            if(!IsCloud || !SessionReady){assistantState.text="Conecta la API en Ajustes → Conexión AWS. No es necesario desplegar recursos.";return;}
            assistantRetryAt=Time.unscaledTime+Mathf.Min(300,10*Mathf.Pow(2,Mathf.Min(assistantFailures++,5)));
            assistantSource=Cloud;assistantBroker=Cloud.CreateInspectionReader();assistantConnecting=StartCoroutine(ConnectAssistantRoutine());
        }
        IEnumerator ConnectAssistantRoutine()
        {
            assistantState.text="PREPARANDO · credencial temporal";
            AwsCloudApi.AssistantSession ticket=null;string error=null;
            yield return assistantBroker.CreateAssistantSession((value,message)=>{ticket=value;error=message;});
            assistantBroker?.Disconnect();assistantBroker=null;assistantConnecting=null;
            if(ticket==null){assistantState.text=error;yield break;}
            if(!AssistantEnabled || assistantSource!=Cloud || !SessionReady){ticket.clientSecret=null;yield break;}
            if(!assistantVoice)assistantVoice=gameObject.AddComponent<RealtimeVoice>();
            assistantVoice.Status=text=>{if(assistantState)assistantState.text=text;UpdateAssistantControls();};
            assistantVoice.UserTranscript=text=>{if(assistantUser)assistantUser.text="Tú: "+(text??"");assistantCaption="Tú: "+(text??"");};
            assistantVoice.AssistantTranscript=text=>{if(assistantSpeech)assistantSpeech.text=text;assistantCaption="ATLAS: "+text;};
            assistantVoice.SpeechStarted=BeginVoicePointing;assistantVoice.InputCommitted=FreezeVoicePointing;assistantVoice.Interrupted=CancelVoicePreview;
            assistantVoice.CaptureStopped=()=>{spatialVoice.Clear();assistantPointing=null;};
            assistantVoice.ToolCall=(id,name,args)=>assistantCalls.Enqueue(new AssistantCall{id=id,name=name,args=args});
            assistantVoice.Begin(ticket,true,NaturalConversation);
            assistantVoice.SetCaptureSuspended(ConfiguringConnection || credentialBusy);
        }
        void TickAssistant()
        {
            if(!assistantPanel)return;
            TickAssistantPresence();
            if(Time.unscaledTime<assistantNextUpdate)return;assistantNextUpdate=Time.unscaledTime+.15f;
            if(assistantSource!=null && (assistantSource!=Cloud || !SessionReady)) {
                bool enabled=AssistantEnabled;StopAssistant();AssistantEnabled=enabled;assistantRetryAt=Time.unscaledTime+2;
            }
            assistantVoice?.SetCaptureSuspended(ConfiguringConnection || credentialBusy);
            if(assistantVoice?.Connected==true){assistantFailures=0;assistantRetryAt=Time.unscaledTime+2;}
            if(AssistantEnabled && Time.unscaledTime>=assistantRetryAt)ConnectAssistant();
            if(assistantProposalGraph!=null && (revision!=assistantBaseRevision || JsonUtility.ToJson(Graph)!=assistantBaseSnapshot)){
                DiscardAssistantProposal();assistantChanges.text="El diseño cambió. Pide una propuesta nueva para evitar sobrescribir tu trabajo.";
            }
            if(assistantToolRoutine==null && assistantCalls.Count>0)assistantToolRoutine=StartCoroutine(ExecuteAssistantTool(assistantCalls.Dequeue()));
            UpdateAssistantControls();
        }
        void UpdateAssistantControls()
        {
            if(!assistantPanel)return;
            bool connected=assistantVoice && assistantVoice.Connected;
            assistantConnect.SetAvailable(true);assistantConnect.Label.text=AssistantEnabled?"Desactivar ATLAS":"Activar ATLAS";
            if(assistantDock){assistantDock.Label.text=AssistantEnabled?"ATLAS · ACTIVO":"ATLAS IA";assistantDock.Surface.Accent=AssistantEnabled?Green:Cyan;}
            if(assistantVoice?.Listening==true) {
                float level=assistantVoice.MicrophoneLevel;int bars=Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(level)*24),0,10);
                assistantMicrophone.text="MIC ["+new string('|',bars)+new string('·',10-bars)+"] · "+(level>.00005f?"SEÑAL DETECTADA":"HABLA PARA COMPROBAR LA SEÑAL")+" · "+assistantVoice.RecordedSeconds.ToString("0.0")+" s";
                assistantMicrophone.color=bars>0?Green:Orange;
            } else {assistantMicrophone.text=assistantVoice?.PreparingMicrophone==true?"PREPARANDO MICRÓFONO…":"MICRÓFONO APAGADO";assistantMicrophone.color=Muted;}
            assistantApply.SetAvailable(AssistantHasProposal && AssistantCanEdit);
            assistantUndo.SetAvailable(AssistantCanUndo && AssistantCanEdit);
            UpdatePresenceButtons();
        }
        public string ProposeAssistantArchitecture(string arguments)
        {
            if(!AssistantCanEdit)return AssistantReply("blocked","Termina la operación o edición actual antes de proponer cambios.");
            try {
                if(string.IsNullOrEmpty(arguments) || arguments.Length>24000)throw new ArgumentException("Propuesta vacía o demasiado grande.");
                var proposal=JsonUtility.FromJson<AssistantProposal>(arguments);
                if(proposal==null)throw new ArgumentException("Propuesta inválida.");
                var graph=proposal.Build(Graph,revision);DiscardAssistantProposal();
                assistantProposalGraph=graph;assistantBaseRevision=revision;assistantBaseSnapshot=JsonUtility.ToJson(Graph);
                assistantDifference=AssistantProposal.Difference(Graph,graph);ShowAssistantDifference(0);
                return AssistantReply("pending_user_review",assistantDifference+"\nEl usuario debe aplicar la propuesta con el botón o pedir explícitamente que se aplique. Aún no se cambió el diseño ni AWS.");
            }catch(ArgumentException error){return AssistantReply("invalid_proposal",error.Message);}
        }
        void ShowAssistantDifference(int page)
        {
            if(!assistantChanges || assistantProposalGraph==null)return;
            var lines=assistantDifference.Split('\n');int pages=Math.Max(1,(lines.Length+4)/5);assistantDiffPage=Mathf.Clamp(page,0,pages-1);
            assistantChanges.text=string.Join("\n",lines.Skip(assistantDiffPage*5).Take(5));assistantPageLabel.text="PROPUESTA · "+(assistantDiffPage+1)+" / "+pages;
        }
        public void ApplyAssistantProposal()
        {
            if(!AssistantCanEdit || assistantProposalGraph==null)return;
            if(revision!=assistantBaseRevision || JsonUtility.ToJson(Graph)!=assistantBaseSnapshot){DiscardAssistantProposal();SetStatus("Propuesta vencida: el diseño cambió.");return;}
            var graph=assistantProposalGraph;bool showReview=workflowReview;Remember();ApplyWorkflowLayout(graph);DiscardAssistantProposal();SetGraph(graph);
            assistantAppliedRevision=revision;
            assistantAppliedSnapshot=JsonUtility.ToJson(Graph);
            assistantUndoEntry=history.Peek();
            if(assistantChanges)assistantChanges.text="Propuesta aplicada al diseño. Puedes deshacer. AWS aún no se modificó.";
            assistantVoice?.ApplicationNotice("User applied the proposal. Graph revision is now "+revision+". No AWS deployment occurred.");
            if(showReview)ShowReview();
        }
        void DiscardAssistantProposal(){assistantProposalGraph=null;assistantBaseSnapshot=null;assistantDifference=null;workflowScale=0;workflowArrange=workflowReview=false;if(assistantChanges)assistantChanges.text="Sin propuesta pendiente.";if(assistantPageLabel)assistantPageLabel.text="";}
        void UndoAssistant()
        {
            if(!AssistantCanEdit || !AssistantCanUndo)return;
            Undo();assistantAppliedRevision=-1;assistantVoice?.ApplicationNotice("User undid the assistant's last graph change. Get fresh context.");
        }
        IEnumerator ExecuteAssistantTool(AssistantCall call)
        {
            // Always yield before completion so coroutine assignment cannot retain a completed handle.
            yield return null;
            if(!assistantVoice || !assistantVoice.ToolPending(call.id)){assistantToolRoutine=null;yield break;}
            string output;
            string snapshot=JsonUtility.ToJson(Graph);float size=ComponentScale;
            if(PreviewVoiceCall(call.name,call.args)) {
                float until=Time.unscaledTime+.7f;
                while(Time.unscaledTime<until && assistantVoice && assistantVoice.ToolPending(call.id) && voicePreviewActive)yield return null;
                bool valid=voicePreviewActive && assistantVoice && assistantVoice.ToolPending(call.id);
                CancelVoicePreview();
                if(!valid){assistantToolRoutine=null;yield break;}
                if(snapshot!=JsonUtility.ToJson(Graph) || size!=ComponentScale){assistantVoice.CompleteTool(call.id,AssistantReply("stale","El diseño cambió durante la previsualización. Obtén contexto de nuevo."));assistantToolRoutine=null;yield break;}
            }
            if(call.name=="get_context")output=AssistantContextJson();
            else if(call.name=="spatial_action" || call.name=="component_action" || call.name=="connection_action" || call.name=="set_component_size" || call.name=="ui_action")output=ExecuteVoiceAction(call.name,call.args);
            else if(call.name=="propose_architecture")output=ProposeAssistantArchitecture(call.args);
            else if(call.name=="propose_workflow")output=ProposeAssistantWorkflow(call.args);
            else if(call.name=="propose_lambda_code")output=ProposeLambdaCode(call.args);
            else if(call.name=="diagnostics_action")output=ExecuteDiagnosticsTool(call.args);
            else if(call.name=="get_lambda_code" || call.name=="lambda_code_action"){
                output=null;codeToolActive=true;try{yield return ExecuteCodeTool(call.name,call.args,result=>output=result);}finally{codeToolActive=false;}
            }
            else if(call.name=="open_deployment_review"){
                if(AssistantCanEdit && !AssistantHasProposal)output=ExecuteVoiceUi("open_deployment_review");
                else output=AssistantReply("blocked","Revisa la propuesta o termina la operación actual primero.");
            }else if(call.name=="highlight_node" || call.name=="inspect_last_event"){
                AssistantNodeRequest request=null;try{request=JsonUtility.FromJson<AssistantNodeRequest>(call.args);}catch(ArgumentException){}
                var node=request==null?null:Graph.Find(request.nodeId);
                if(node==null)output=AssistantReply("not_found","Selecciona un recurso del contexto actual.");
                else if(call.name=="highlight_node"){
                    if(AssistantCanEdit){ConnectingMode=false;Select(views[node.id]);output=AssistantReply("selected",node.id);}else output=AssistantReply("blocked","El laboratorio está ocupado.");
                }else{
                    output=null;yield return InspectForAssistant(node,call.id,result=>output=result);
                }
            }else output=AssistantReply("unsupported","Acción no permitida por esta versión del laboratorio.");
            if(assistantVoice && assistantVoice.ToolPending(call.id))assistantVoice.CompleteTool(call.id,output??AssistantReply("cancelled","Consulta cancelada."));
            assistantToolRoutine=null;
        }
        IEnumerator InspectForAssistant(ResourceNode node,string callId,Action<string> complete)
        {
            if(!IsCloud || !SessionReady || !Deployed || string.IsNullOrEmpty(Cloud.LastEventId) || (node.kind!=ServiceKind.Lambda && node.kind!=ServiceKind.DynamoDB)){
                complete(AssistantReply("unavailable","Requiere Lambda o DynamoDB del diseño desplegado y un evento enviado. No se ha enviado ningún evento nuevo."));yield break;
            }
            string stack=Cloud.StackId,eventId=Cloud.LastEventId;var source=Cloud;int atRevision=revision;
            assistantInspector=source.CreateInspectionReader();AwsCloudApi.InspectionPage page=null;string error=null;
            yield return assistantInspector.Inspect(source.Slot,stack,node.id,node.kind==ServiceKind.Lambda?"logs":"items","",(p,e)=>{page=p;error=e;},eventId:eventId);
            assistantInspector?.Disconnect();assistantInspector=null;
            if(source!=Cloud || revision!=atRevision || stack!=Cloud.StackId || eventId!=Cloud.LastEventId || !assistantVoice.ToolPending(callId)){complete(AssistantReply("stale","La selección o sesión cambió durante la consulta."));yield break;}
            if(page==null){complete(AssistantReply("error",error));yield break;}
            page.entries=page.entries.Take(5).ToArray();foreach(var entry in page.entries){entry.text=RedactAssistantData(entry.text);if(entry.text.Length>1200){entry.text=entry.text.Substring(0,1200);entry.truncated=true;}}
            page.message="Muestra limitada y redactada del evento "+eventId+". Puede haber más páginas o demora de ingestión. Vacío no demuestra un fallo.";page.cursor=page.resume="";
            complete(JsonUtility.ToJson(page));
        }
        public static string RedactAssistantData(string value)
        {
            string text=Regex.Replace(value??"",@"sk-[A-Za-z0-9_-]{12,}","[REDACTED]");
            text=Regex.Replace(text,@"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b","[REDACTED]");
            text=Regex.Replace(text,"(?i)(\"(?:password|token|secret|api_key|authorization)\"\\s*:\\s*\")[^\"]*(\")","$1[REDACTED]$2");
            return Regex.Replace(text,@"(?i)\b(?:Bearer|Basic)\s+[A-Za-z0-9_+/=.-]+","[REDACTED AUTH]");
        }
        void StopAssistant()
        {
            StopDiagnostics();if(codeToolActive)AbortCodeOperation();
            AssistantEnabled=false;assistantRetryAt=float.PositiveInfinity;
            CancelVoicePreview();spatialVoice.Clear();assistantPointing=null;
            if(voiceDestinationMarker)voiceDestinationMarker.SetActive(false);
            if(assistantPresence)assistantPresence.gameObject.SetActive(false);
            if(assistantConnecting!=null)StopCoroutine(assistantConnecting);assistantConnecting=null;
            if(assistantToolRoutine!=null)StopCoroutine(assistantToolRoutine);assistantToolRoutine=null;
            assistantBroker?.Disconnect();assistantBroker=null;assistantInspector?.Disconnect();assistantInspector=null;assistantSource=null;
            assistantCalls.Clear();assistantVoice?.Disconnect();DiscardAssistantProposal();
            if(assistantState)assistantState.text="DESCONECTADO · micrófono apagado";
        }
    }
}
