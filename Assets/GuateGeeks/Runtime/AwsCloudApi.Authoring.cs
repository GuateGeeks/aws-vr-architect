using System;
using System.Collections;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    public sealed partial class AwsCloudApi
    {
        [Serializable] public sealed class CodeVersion {public string version,description;}
        [Serializable] public sealed class CodeResult {
            public string source,revisionId,codeSha256,sourceHash,version,updateStatus,rollbackVersion,message,output,logs;
            public bool valid,passed,truncated;public CodeVersion[] versions;
        }
        [Serializable] public sealed class CodeRequest {
            public string source,eventJson,stackId,resourceId,revisionId,version;public bool confirmed;
        }
        public IEnumerator ReadLambdaCode(string stack,string node,Action<CodeResult,string> completed)
        {
            CloudReply reply=null;
            yield return Request("GET","/v1/deployments/"+Slot+"/code?stackId="+Uri.EscapeDataString(stack)+"&resourceId="+Uri.EscapeDataString(node),null,true,r=>reply=r);
            completed(reply.Ok?Parse<CodeResult>(reply):null,reply.Ok?null:Describe(reply));
        }
        public IEnumerator CodeOperation(string operation,CodeRequest request,Action<CodeResult,string> completed)
        {
            if(operation!="validate" && operation!="test" && operation!="publish" && operation!="rollback"){completed(null,"Operación de código inválida.");yield break;}
            string path=operation=="validate" || operation=="test"?"/v1/code/"+operation:"/v1/deployments/"+Slot+"/code/"+operation;
            CloudReply reply=null;yield return Request("POST",path,JsonUtility.ToJson(request),false,r=>reply=r);
            completed(reply.Ok?Parse<CodeResult>(reply):null,reply.Ok?null:Describe(reply));
        }
    }
}
