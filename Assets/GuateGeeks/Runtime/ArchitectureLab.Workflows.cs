using System;
using System.Linq;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    public sealed partial class ArchitectureLab
    {
        [Serializable] sealed class WorkflowRequest {public int baseRevision;public string summary;public ResourceNode[] nodes;public ResourceLink[] links;public float componentScale;public bool arrange,openReview;}
        float workflowScale;bool workflowArrange,workflowReview;
        public string ProposeAssistantWorkflow(string arguments)
        {
            WorkflowRequest request;
            try {if(arguments==null || arguments.Length>24000)throw new ArgumentException();request=JsonUtility.FromJson<WorkflowRequest>(arguments);}
            catch(ArgumentException){return AssistantReply("invalid","Flujo inválido.");}
            if(request==null || float.IsNaN(request.componentScale) || request.componentScale<.5f || request.componentScale>1.25f)return AssistantReply("invalid","Tamaño visual entre 50% y 125% requerido.");
            var result=ProposeAssistantArchitecture(JsonUtility.ToJson(new AssistantProposal{baseRevision=request.baseRevision,summary=request.summary,nodes=request.nodes,links=request.links}));
            if(JsonUtility.FromJson<AssistantResult>(result)?.status!="pending_user_review")return result;
            workflowScale=request.componentScale;workflowArrange=request.arrange;workflowReview=request.openReview;
            assistantDifference="PLAN · aplicar diseño completo\n"+assistantDifference+"\nTamaño: "+Mathf.RoundToInt(workflowScale*100)+"%"+(workflowArrange?"\nDistribuir en la mesa":"")+(workflowReview?"\nAbrir revisión del diseño":"")+"\nUn solo paso para deshacer. AWS requiere confirmación aparte.";
            ShowAssistantDifference(0);return AssistantReply("pending_user_review",assistantDifference);
        }
        void ApplyWorkflowLayout(Architecture graph)
        {
            if(workflowScale<=0)return;
            RestoreViewScale(workflowScale);
            foreach(var node in graph.nodes)node.viewScale=0;
            if(workflowArrange)for(int i=0;i<graph.nodes.Count;i++)graph.nodes[i].position=TablePosition(i);
        }
    }
}
