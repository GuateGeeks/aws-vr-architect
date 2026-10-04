using System;
using System.Collections;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    public sealed partial class AwsCloudApi
    {
        [Serializable] public sealed class AssistantSession { public string clientSecret, model; public long expiresAt; public int maxSessionSeconds; }
        public IEnumerator CreateAssistantSession(Action<AssistantSession,string> completed)
        {
            if(!Connected) { completed(null,"Conecta la API AWS para iniciar ATLAS.");yield break; }
            CloudReply reply=null;
            yield return Request("POST","/v1/assistant/session","{}",false,r=>reply=r);
            var session=Parse<AssistantSession>(reply);
            if(!reply.Ok || session==null || string.IsNullOrEmpty(session.clientSecret) || !session.clientSecret.StartsWith("ek_") || session.expiresAt<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
                completed(null,reply.Ok?"Credencial temporal inválida.":Describe(reply));yield break;
            }
            completed(session,null);
        }
    }
}
