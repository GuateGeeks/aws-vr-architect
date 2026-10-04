using System;
using System.Linq;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    public sealed partial class ArchitectureLab
    {
        public const string ComponentScalePreference="GuateGeeks.ComponentScale";
        public float ComponentScale {get;private set;}=1;
        TMPro.TMP_Text componentSizeText;
        [Serializable] sealed class VoiceAction {
            public string action,nodeId,name,from,to,locationId;
            public string[] nodeIds;
            public int baseRevision=-1,kind=-1,setting=-1;
            public float scale;
            public bool arrange;
        }
        [Serializable] sealed class VoiceActionResult {
            public string status,message,nodeId;public int revision;public float componentScale;
        }
        string ActionResult(string status,string message,string nodeId="")=>JsonUtility.ToJson(new VoiceActionResult {
            status=status,message=message,nodeId=nodeId,revision=revision,componentScale=ComponentScale
        });
        string[] AvailableVoiceActions()
        {
            if(Busy || codeBusy || ConfiguringConnection || credentialBusy || views.Values.Any(v=>v.Grabbed))return Array.Empty<string>();
            if(EditingText)return new[]{"cancel_interaction"};
            if(Placing)return new[]{"confirm_placement","cancel_interaction"};
            if(HasPendingDefinition)return new[]{"apply_changes","cancel_edit","cancel_interaction"};
            var actions=new System.Collections.Generic.List<string>{"open_catalog","review_design","preview_flow","arrange","save","load","open_library","open_settings","close_settings","open_slots","cancel_interaction"};
            if(history.Count>0)actions.Add("undo");
            if(selected)actions.AddRange(new[]{"relations","help_selected","inspect_selected"});
            if(AssistantHasProposal)actions.AddRange(new[]{"apply_proposal","discard_proposal"});
            else actions.Add("open_deployment_review");
            if(Deployed)actions.Add("send_event");
            return actions.ToArray();
        }
        string ExecuteVoiceActionCore(string tool,string arguments)
        {
            VoiceAction request;
            try {
                if(string.IsNullOrEmpty(arguments) || arguments.Length>4000)return ActionResult("invalid","Argumentos vacíos o demasiado grandes.");
                request=JsonUtility.FromJson<VoiceAction>(arguments);
            }catch(ArgumentException){return ActionResult("invalid","Argumentos inválidos.");}
            if(request==null)return ActionResult("invalid","Falta la acción.");
            if(request.baseRevision!=revision)return ActionResult("stale","El diseño cambió. Obtén el contexto actual antes de continuar.");
            if(tool=="ui_action")return ExecuteVoiceUi(request.action);
            if(!AssistantCanEdit)return ActionResult("blocked","Termina la edición, colocación u operación actual.");
            if(tool=="spatial_action")return ExecuteSpatialAction(request);
            if(tool=="set_component_size") {
                if(!SetComponentScale(request.scale))return ActionResult("invalid","Escala permitida: 0.5 a 1.25.");
                if(request.arrange)ArrangeDesign();
                return ActionResult("applied","Tamaño visual ajustado. La definición y el despliegue AWS no cambiaron.");
            }
            if(tool=="connection_action") {
                if(request.action=="connect") {
                    if(!Graph.CanConnect(request.from,request.to,out string reason))return ActionResult("invalid",reason);
                    Remember();Graph.Connect(request.from,request.to,out _);
                }else if(request.action=="disconnect") {
                    var edge=Graph.links.Find(l=>l.from==request.from && l.to==request.to);
                    if(edge==null)return ActionResult("not_found","La conexión no existe.");
                    Remember();Graph.links.Remove(edge);
                }else return ActionResult("unsupported","Acción de conexión no disponible.");
                ConnectingMode=false;connectionSource=null;RebuildLinks();Changed();RefreshSelection();ShowInspector();
                return ActionResult("applied","Conexión actualizada en el diseño. Puedes deshacer; AWS no cambió.");
            }
            if(tool!="component_action")return ActionResult("unsupported","Herramienta no disponible.");
            var node=Graph.Find(request.nodeId);
            if(request.action=="add" || request.action=="update") {
                if(request.action=="update" && node==null)return ActionResult("not_found","Recurso no encontrado.");
                int kind=request.action=="add"?request.kind:(int)node.kind;
                if(kind<0 || kind>6 || request.setting<0 || request.setting>=DesignSemantics.OptionCount((ServiceKind)kind))return ActionResult("invalid","Tipo u opción no compatible. Consulta el catálogo actual.");
                if(string.IsNullOrWhiteSpace(request.name) || request.name.Trim().Length>80)return ActionResult("invalid","Nombre de 1 a 80 caracteres requerido.");
                if(request.action=="add") {
                    if(Graph.nodes.Count>=Architecture.MaxNodes)return ActionResult("blocked","Máximo 12 componentes por arquitectura.");
                    AddResource((ServiceKind)kind);node=selected.Model;
                    node.name=request.name.Trim();node.setting=request.setting;draftId=null;ShowInspector();
                }else {
                    if(node.kind==ServiceKind.SQS && request.setting==1 && Graph.links.Any(l=>l.to==node.id && Graph.Find(l.from).kind==ServiceKind.S3))return ActionResult("invalid","Desconecta S3 antes de elegir FIFO.");
                    ConnectingMode=false;Select(views[node.id]);draftName=request.name.Trim();draftSetting=request.setting;ApplyDefinition();
                }
                return ActionResult("applied","Componente actualizado en el diseño. Puedes deshacer; AWS no cambió.",node.id);
            }
            if(node==null)return ActionResult("not_found","Recurso no encontrado en el contexto actual.");
            if(request.action=="select") {ConnectingMode=false;connectionSource=null;Select(views[node.id]);return ActionResult("selected",node.name,node.id);}
            if(request.action=="remove") {Remember();var next=Graph.Copy();next.Remove(node.id);SetGraph(next);return ActionResult("applied","Componente y conexiones quitados del diseño. Puedes deshacer; AWS no cambió.",node.id);}
            return ActionResult("unsupported","Acción de componente no disponible.");
        }
        string ExecuteVoiceUi(string action)
        {
            if(!AvailableVoiceActions().Contains(action))return ActionResult("blocked","Acción no disponible en el estado actual. Consulta availableActions. Las confirmaciones de AWS son manuales.");
            switch(action) {
                case "open_catalog":catalogPanel.gameObject.SetActive(true);break;
                case "review_design":ShowReview();break;
                case "preview_flow":
                    bool wasPreviewing=FlowPreviewActive;PreviewFlow();
                    if(!wasPreviewing && !FlowPreviewActive)return ActionResult("blocked",statusText.text);
                    break;
                case "arrange":ArrangeDesign();break;
                case "undo":Undo();break;
                case "save":Save();return ActionResult("ui_result",statusText.text);
                case "load":
                    int previousRevision=revision;Load();
                    return ActionResult(revision!=previousRevision?"completed":"unavailable",statusText.text);
                case "open_library":ShowLibrary(0);break;
                case "open_settings":SetSettingsVisible(true);ShowSettingsPage(1);break;
                case "close_settings":SetSettingsVisible(false);break;
                case "relations":ShowRelations(0);break;
                case "help_selected":ShowDefinitionHelp();break;
                case "inspect_selected":InspectSelectedCloudObject();break;
                case "open_slots":OpenSlots();break;
                case "apply_proposal":
                    if(assistantBaseRevision!=revision || assistantBaseSnapshot!=JsonUtility.ToJson(Graph)){DiscardAssistantProposal();return ActionResult("stale","La propuesta venció; pide otra con el contexto actual.");}
                    ApplyAssistantProposal();break;
                case "discard_proposal":DiscardAssistantProposal();break;
                case "apply_changes":ApplyDefinition();if(HasPendingDefinition)return ActionResult("invalid","La definición no pudo aplicarse. Revisa la compatibilidad del recurso.");break;
                case "cancel_edit":draftId=null;ShowInspector();break;
                case "confirm_placement":ConfirmPlacement();if(Placing)return ActionResult("blocked","El objeto está demasiado cerca de otro. Elige un espacio libre.");break;
                case "cancel_interaction":CancelInteraction();break;
                case "open_deployment_review":
                    confirming=false;RequestDeployment();
                    return ActionResult(confirming?"review_opened":"blocked",confirming?"Confirma manualmente en la revisión para desplegar. No se modificó AWS.":statusText.text);
                case "send_event":TestFlow();return ActionResult("started","Envío iniciado; consulta el resultado. Aún no se confirma procesamiento.");
            }
            return ActionResult("completed","Acción de interfaz ejecutada: "+action);
        }
        public bool SetComponentScale(float scale)
        {
            if(float.IsNaN(scale) || float.IsInfinity(scale) || scale<.5f || scale>1.25f || views.Values.Any(v=>v.Grabbed))return false;
            Remember();ComponentScale=scale;
            foreach(var node in Graph.nodes)node.viewScale=0;
            foreach(var view in views.Values)view.transform.localScale=Vector3.one*scale;
            if(placementGhost)placementGhost.localScale=Vector3.one*.38f*scale;
            PlayerPrefs.SetFloat(ComponentScalePreference,scale);PlayerPrefs.Save();
            RefreshComponentSize();SetStatus("Tamaño de componentes: "+Mathf.RoundToInt(scale*100)+"% · usa Ordenar para distribuirlos.");return true;
        }
        void RefreshComponentSize(){if(componentSizeText)componentSizeText.text="TAMAÑO DE COMPONENTES · "+Mathf.RoundToInt(ComponentScale*100)+"%";}
        Vector3 TablePosition(int index)=>new Vector3((index%4-1.5f)*.86f*ComponentScale,1.5f,2.55f+(index/4-1)*.82f*ComponentScale);
    }
}
