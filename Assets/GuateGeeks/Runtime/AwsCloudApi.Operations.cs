using System;
using System.Collections;

namespace GuateGeeks.AwsVr
{
    public sealed partial class AwsCloudApi
    {
        [Serializable] public class InspectionEntry {
            public string title, text, id, level, eventId, nodeId, targetNodeId, stage, requestId;
            public long timestamp;
            public bool truncated;
        }
        [Serializable] public class InspectionPage
        {
            public string stackId, resourceId, cursor, resume, message;
            public bool incremental;
            public int inspectionVersion;
            public InspectionEntry[] entries;
        }
        static bool ValidSlot(string slot) => slot == "1" || slot == "2" || slot == "3";
        static string Enc(string value) => Uri.EscapeDataString(value ?? "");
        public IEnumerator ReadSlot(string slot, Action<DeploymentStatus, string> completed)
        {
            if (!Connected || !ValidSlot(slot)) { completed(null, "Conecta AWS y elige un slot del 1 al 3."); yield break; }
            CloudReply reply = null;
            yield return Request("GET", "/v1/deployments/" + slot, null, true, r => reply = r);
            if (reply.Code == 404) { ForgetDeployment(slot); completed(new DeploymentStatus { deploymentId = slot, status = "ABSENT", finished = true, nodes = new NodeStatus[0] }, null); yield break; }
            var state = Parse<DeploymentStatus>(reply);
            if (!reply.Ok || state == null || state.deploymentId != slot || string.IsNullOrEmpty(state.stackId))
            { completed(null, reply.Ok ? "Estado de slot inválido." : Describe(reply)); yield break; }
            completed(state, null);
        }
        public IEnumerator Inspect(string slot, string stackId, string resourceId, string mode, string cursor, Action<InspectionPage, string> completed, string resume = "", string search = "", string level = "", string eventId = "")
        {
            if (!Connected || !ValidSlot(slot) || string.IsNullOrEmpty(stackId) || string.IsNullOrEmpty(resourceId) || (mode != "logs" && mode != "items"))
            { completed(null, "Actualiza el slot y selecciona una Lambda o tabla."); yield break; }
            CloudReply reply = null;
            yield return Request("GET", "/v1/deployments/" + slot + "/" + mode + "?stackId=" + Enc(stackId) + "&resourceId=" + Enc(resourceId) + "&cursor=" + Enc(cursor) + "&resume=" + Enc(resume) + "&q=" + Enc(search) + "&level=" + Enc(level) + "&eventId=" + Enc(eventId), null, true, r => reply = r);
            var page = Parse<InspectionPage>(reply);
            if (!reply.Ok || page == null || page.stackId != stackId || page.resourceId != resourceId || page.entries == null)
            { completed(null, reply.Ok ? "La respuesta no corresponde al recurso seleccionado." : Describe(reply)); yield break; }
            completed(page, null);
        }
        // Called only after explicit UI confirmation. Reads are retried; destructive requests are not.
        public IEnumerator CleanSlot(string slot, string confirmedStackId, Action<string> progress, Action<bool, string> completed)
        {
            if (string.IsNullOrEmpty(confirmedStackId)) { completed(false, "Actualiza y confirma el despliegue antes de limpiar."); yield break; }
            DeploymentStatus state = null; string error = null;
            yield return ReadSlot(slot, (s, e) => { state = s; error = e; });
            if (state == null) { completed(false, error); yield break; }
            if (state.status == "ABSENT") { ForgetDeployment(slot); completed(true, "Slot " + slot + " vacío."); yield break; }
            if (state.stackId != confirmedStackId) { completed(false, "El slot cambió. Actualiza y confirma el nuevo despliegue."); yield break; }
            bool sendDelete = state.status != "DELETE_IN_PROGRESS";
            for (int poll = 0; poll < 400; poll++)
            {
                if (sendDelete)
                {
                    CloudReply reply = null;
                    yield return Request("DELETE", "/v1/deployments/" + slot + "?purge=true&stackId=" + Enc(confirmedStackId), null, false, r => reply = r);
                    var result = Parse<DeploymentStatus>(reply);
                    if (!reply.Ok && !reply.Transient) { completed(false, Describe(reply)); yield break; }
                    if (reply.Ok && result?.status == "DELETE_COMPLETE") { ForgetDeployment(slot); completed(true, "Slot " + slot + " limpio."); yield break; }
                    sendDelete = reply.Ok && result != null && result.retryDelete && result.status == "PURGING";
                    progress(sendDelete ? "Vaciando datos S3…" : "Consultando eliminación en AWS…");
                    if (sendDelete) { yield return delay(3); continue; }
                }
                yield return delay(3);
                yield return ReadSlot(slot, (s, e) => { state = s; error = e; });
                if (state == null) { completed(false, error); yield break; }
                if (state.status == "ABSENT") { ForgetDeployment(slot); completed(true, "Slot " + slot + " limpio. AWS confirmó la eliminación."); yield break; }
                if (state.stackId != confirmedStackId) { completed(false, "El slot fue reemplazado. No se borrará el nuevo despliegue."); yield break; }
                progress(state.status + " · slot " + slot);
                if (state.status != "DELETE_IN_PROGRESS") { completed(false, "AWS: " + state.status + ". Actualiza el slot; si corresponde, vuelve a confirmar la limpieza."); yield break; }
            }
            completed(false, "Terminó la espera local. Actualiza el slot para consultar AWS.");
        }
        void ForgetDeployment(string slot)
        {
            if (slot != Slot) return;
            StackId = null; expectedHash = null; deployedGraph = null; EventResourceId = null;
        }
    }
}
