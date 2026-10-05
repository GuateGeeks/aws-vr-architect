using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GuateGeeks.AwsVr
{
    [Serializable]
    public sealed class CloudReply
    {
        public long Code;
        public string Json;
        public bool Ok => Code >= 200 && Code < 300;
        public bool Transient => Code == 0 || Code == 429 || Code >= 500;
    }
    public interface ICloudTransport
    {
        IEnumerator Send(string method, string url, string authorization, string json, Action<CloudReply> completed);
        void Abort();
    }
    public sealed class UnityCloudTransport : ICloudTransport
    {
        UnityWebRequest active;
        public IEnumerator Send(string method, string url, string authorization, string json, Action<CloudReply> completed)
        {
            using (var request = new UnityWebRequest(url, method))
            {
                active = request;
                request.downloadHandler = new DownloadHandlerBuffer();
                if (json != null) request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Authorization", authorization);
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");
                request.timeout = 40;
                // Keep Basic credentials on the configured origin; use platform TLS validation.
                request.redirectLimit = 0;
                yield return request.SendWebRequest();
                completed(new CloudReply { Code = request.responseCode, Json = request.downloadHandler.text });
                if (active == request) active = null;
            }
        }
        public void Abort() { if (active != null) { active.Abort(); active.Dispose(); active = null; } }
    }
    public sealed partial class AwsCloudApi : ICloudApi
    {
        [Serializable] class Session { public bool connected; public string region; public int schemaVersion; }
        [Serializable] class Catalog { public int maxNodes; public string[] deploymentSlots; public Service[] services; }
        [Serializable] class Service { public int kind; }
        [Serializable] class Validation { public bool valid; public string graphHash; }
        [Serializable] class Create { public string deploymentId; public Architecture architecture; }
        [Serializable] class EventRequest { public string resourceId,stackId,eventJson; }
        [Serializable] class EventResponse { public bool accepted; public string eventId, message; }
        [Serializable] class Error { public string message, requestId; }
        [Serializable] public class NodeStatus { public string resourceId, status, message, name, physicalId; public int kind; }
        [Serializable] public class DeploymentStatus
        {
            public string deploymentId, stackId, graphHash, status, message;
            public bool finished, success, retryDelete;
            public NodeStatus[] nodes;
        }
        readonly ICloudTransport transport;
        readonly Func<float, object> delay;
        readonly string endpoint;
        string authorization, expectedHash;
        Architecture deployedGraph;
        public string Region { get; private set; }
        public string Slot { get; private set; }
        public string StackId { get; private set; }
        public string EventResourceId { get; set; }
        public string LastEventId { get; private set; }
        public bool Connected { get; private set; }
        public string Host => new Uri(endpoint).Host;
        public string Endpoint => endpoint;
        public AwsCloudApi(CloudConnection settings, ICloudTransport transport = null, Func<float, object> delay = null)
        {
            settings.Validate(); endpoint = settings.endpoint.TrimEnd('/'); Slot = settings.deploymentId;
            authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.username + ":" + settings.password));
            this.transport = transport ?? new UnityCloudTransport(); this.delay = delay ?? (s => new WaitForSecondsRealtime(s));
        }
        // Independent request ownership: closing the reader must never abort a deploy/event.
        public AwsCloudApi CreateInspectionReader() => new AwsCloudApi(this);
        AwsCloudApi(AwsCloudApi source)
        {
            RoomToken = source.RoomToken;
            endpoint = source.endpoint; Slot = source.Slot; authorization = source.authorization;
            Region = source.Region; Connected = source.Connected; delay = source.delay;
            transport = source.transport is UnityCloudTransport ? new UnityCloudTransport() : source.transport;
        }
        static T Parse<T>(CloudReply reply) where T : class
        {
            try { return JsonUtility.FromJson<T>(reply.Json ?? ""); } catch (ArgumentException) { return null; }
        }
        string Describe(CloudReply reply)
        {
            if (reply.Code == 401) { Connected = false; return "HTTP 401 · Vuelve a cargar las credenciales de servicio."; }
            if (reply.Code == 403) return "HTTP 403 · Conecta Quest a la red/IP permitida por la API.";
            if (reply.Code == 0) return "Sin respuesta de la API. Revisa la red; AWS puede seguir trabajando.";
            var error = Parse<Error>(reply);
            return "HTTP " + reply.Code + " · " + (error?.message ?? "Respuesta no válida de la API.") +
                (string.IsNullOrEmpty(error?.requestId) ? "" : " · requestId: " + error.requestId);
        }
        IEnumerator Request(string method, string path, string json, bool retry, Action<CloudReply> completed)
        {
            CloudReply reply = null;
            for (int attempt = 0; attempt < (retry ? 5 : 1); attempt++)
            {
                string auth = !string.IsNullOrEmpty(RoomToken) && !path.StartsWith("/v1/collab/") ? "Bearer " + RoomToken : authorization;
                yield return transport.Send(method, endpoint + path, auth, json, value => reply = value);
                reply = reply ?? new CloudReply();
                if (!reply.Transient || !retry || attempt == 4) break;
                yield return delay(Mathf.Min(30, 3 * Mathf.Pow(2, attempt)) + UnityEngine.Random.value);
            }
            completed(reply);
        }
        public IEnumerator Connect(Action<bool, string> completed)
        {
            Connected = false; CloudReply reply = null;
            yield return Request("GET", "/v1/session", null, true, r => reply = r);
            var session = Parse<Session>(reply);
            if (!reply.Ok || session == null || !session.connected || session.schemaVersion != 1 || string.IsNullOrEmpty(session.region))
            { completed(false, reply.Ok ? "Versión de API no compatible." : Describe(reply)); yield break; }
            Region = session.region;
            yield return Request("GET", "/v1/catalog", null, true, r => reply = r);
            var catalog = Parse<Catalog>(reply);
            if (!reply.Ok || catalog?.deploymentSlots == null || !catalog.deploymentSlots.Contains(Slot) ||
                catalog.maxNodes != Architecture.MaxNodes || catalog.services == null ||
                !Enumerable.Range(0, 7).All(k => catalog.services.Any(s => s.kind == k)))
            { completed(false, reply.Ok ? "Catálogo de API no compatible." : Describe(reply)); yield break; }
            Connected = true; completed(true, "AWS conectado · " + Region + " · slot " + Slot + " · recursos con costo real");
        }
        public static List<string> ValidateLocally(Architecture graph, string region)
        {
            var issues = graph.Validate();
            if (issues.Count > 0) return issues;
            if (graph.region != region) issues.Add("La región debe coincidir con la API: " + region);
            if (graph.links.Count > 36) issues.Add("AWS admite hasta 36 enlaces.");
            foreach (var n in graph.nodes)
            {
                var targets = graph.links.Where(l => l.from == n.id).Select(l => graph.Find(l.to)).ToArray();
                if (n.kind == ServiceKind.ApiGateway && (n.setting != 0 || !targets.Any(t => t.kind == ServiceKind.Lambda)))
                    issues.Add("API Gateway requiere HTTP API y una Lambda conectada.");
                if ((n.kind == ServiceKind.ApiGateway || n.kind == ServiceKind.SQS) && targets.Count(t => t.kind == ServiceKind.Lambda) > 1)
                    issues.Add("Cada API/cola admite una sola Lambda consumidora.");
                if (n.kind == ServiceKind.S3 && targets.Count(t => t.kind == ServiceKind.Lambda || t.kind == ServiceKind.SQS) > 1)
                    issues.Add("S3 admite un único destino de notificación.");
                if (n.kind == ServiceKind.S3 && targets.Any(t => t.kind == ServiceKind.SQS && t.setting == 1))
                    issues.Add("S3 no entrega directamente a SQS FIFO.");
            }
            return issues;
        }
        public IEnumerator Validate(Architecture graph, Action<bool, string> completed)
        {
            if (!Connected) { completed(false, "Conecta la sesión AWS."); yield break; }
            var issues = ValidateLocally(graph, Region);
            if (issues.Count > 0) { completed(false, issues[0]); yield break; }
            CloudReply reply = null;
            yield return Request("POST", "/v1/architectures/validate", JsonUtility.ToJson(graph), true, r => reply = r);
            var result = Parse<Validation>(reply);
            bool valid = reply.Ok && result != null && result.valid && !string.IsNullOrEmpty(result.graphHash);
            if (valid) expectedHash = result.graphHash;
            completed(valid, valid ? "Diseño validado por AWS. Todavía no se crearon recursos." : Describe(reply));
        }
        public IEnumerator Deploy(Architecture snapshot, bool simulateFailure, Action<DeploymentEvent> progress)
        {
            if (simulateFailure) { Fail(progress, "Desactiva el fallo simulado en modo AWS."); yield break; }
            bool valid = false; string message = null;
            yield return Validate(snapshot, (ok, text) => { valid = ok; message = text; });
            if (!valid) { Fail(progress, message); yield break; }
            deployedGraph = snapshot.Copy(); EventResourceId = null;
            foreach (var node in snapshot.nodes) progress(new DeploymentEvent { ResourceId = node.id, State = ResourceState.Provisioning, Message = "Solicitando slot " + Slot + "…" });
            CloudReply reply = null;
            yield return Request("POST", "/v1/deployments", JsonUtility.ToJson(new Create { deploymentId = Slot, architecture = snapshot }), false, r => reply = r);
            if (reply.Transient)
            {
                progress(new DeploymentEvent { Message = "Respuesta incierta. Consultando el MISMO slot; no se repite la creación." });
                yield return Request("GET", "/v1/deployments/" + Slot, null, true, r => reply = r);
            }
            if (!reply.Ok) { Fail(progress, Describe(reply)); yield break; }
            // Polling is bounded locally. Stopping it never cancels CloudFormation.
            for (int poll = 0; poll < 400; poll++)
            {
                var state = Parse<DeploymentStatus>(reply);
                if (state == null || state.deploymentId != Slot || string.IsNullOrEmpty(state.stackId) ||
                    state.graphHash != expectedHash || (!string.IsNullOrEmpty(StackId) && state.stackId != StackId))
                { Fail(progress, "El slot no coincide con este diseño. Consulta al operador y vuelve a conectar la sesión."); yield break; }
                StackId = state.stackId;
                bool success = state.finished && state.success && state.status == "CREATE_COMPLETE";
                foreach (var node in snapshot.nodes)
                {
                    var actual = state.nodes?.FirstOrDefault(n => n.resourceId == node.id);
                    var visual = actual == null ? ResourceState.Provisioning : MapState(actual.status);
                    progress(new DeploymentEvent { ResourceId = node.id, State = visual, Message = node.name + ": " + (actual?.status ?? "pendiente") });
                }
                progress(new DeploymentEvent { Message = state.status + " · slot " + Slot + (string.IsNullOrEmpty(state.message) ? "" : " · " + state.message),
                    Finished = state.finished, Success = success, Progress = success ? 1 : 0 });
                if (state.finished) yield break;
                yield return delay(3);
                yield return Request("GET", "/v1/deployments/" + Slot, null, true, r => reply = r);
                if (!reply.Ok) { Fail(progress, Describe(reply)); yield break; }
            }
            Fail(progress, "Terminó la espera local. AWS continúa; usa el mismo diseño y slot para retomar.");
        }
        public static ResourceState MapState(string state) => state == "CREATE_COMPLETE" ? ResourceState.Ready :
            state != null && (state.Contains("FAILED") || state.Contains("ROLLBACK") || state.StartsWith("DELETE_", StringComparison.Ordinal)) ? ResourceState.Failed : ResourceState.Provisioning;
        static void Fail(Action<DeploymentEvent> progress, string message) => progress(new DeploymentEvent { Finished = true, Success = false, Message = message });
        public IEnumerator Invoke(Action<bool, string> completed)
        { return InvokeEvent(EventResourceId,"",completed); }
        public static ResourceNode DefaultEventSource(Architecture graph)=>graph?.nodes.FirstOrDefault(n=>SupportedSource(n.kind) && !graph.links.Any(l=>l.to==n.id && graph.Find(l.from)?.kind!=ServiceKind.CloudWatch));
        public IEnumerator InvokeEvent(string nodeId,string eventJson,Action<bool,string> completed,Func<bool> stillAuthorized=null)
        {
            LastEventId = null;
            if (!Connected || deployedGraph == null || string.IsNullOrEmpty(StackId)) { completed(false, "Despliega o retoma tu diseño antes de enviar eventos."); yield break; }
            if(System.Text.Encoding.UTF8.GetByteCount(eventJson??"")>4096){completed(false,"Evento JSON de hasta 4 KiB requerido.");yield break;}
            var source = string.IsNullOrEmpty(nodeId) ? DefaultEventSource(deployedGraph) : deployedGraph.Find(nodeId);
            if (source == null || !SupportedSource(source.kind)) { completed(false, "Selecciona API Gateway, Lambda, S3, SQS o EventBridge como origen."); yield break; }
            // Check identity again: slots are shared with other frontends/operators.
            CloudReply reply = null;
            yield return Request("GET", "/v1/deployments/" + Slot, null, true, r => reply = r);
            var state = Parse<DeploymentStatus>(reply);
            if (!reply.Ok || state == null || state.stackId != StackId || state.graphHash != expectedHash || !state.success || state.status != "CREATE_COMPLETE")
            { completed(false, reply.Ok ? "El despliegue cambió o no está listo. Retoma el mismo diseño antes de enviar eventos." : Describe(reply)); yield break; }
            if(stillAuthorized!=null && !stillAuthorized()){completed(false,"Envío cancelado antes de modificar AWS.");yield break;}
            yield return Request("POST", "/v1/deployments/" + Slot + "/events", JsonUtility.ToJson(new EventRequest { resourceId = source.id,stackId=StackId,eventJson=eventJson??"" }), false, r => reply = r);
            var result = Parse<EventResponse>(reply);
            bool accepted = reply.Ok && result != null && result.accepted;
            if (accepted) LastEventId = result.eventId;
            completed(accepted, accepted ? "HTTP " + reply.Code + " · " + (reply.Code == 202 ? "Evento aceptado; procesamiento aún no confirmado." : "API respondió.") + " · " + result.eventId : Describe(reply));
        }
        static bool SupportedSource(ServiceKind kind) => kind != ServiceKind.DynamoDB && kind != ServiceKind.CloudWatch;
        public void StopWatching() => transport.Abort();
        public void Disconnect() { transport.Abort(); Connected = false; authorization = null; }
    }
}
