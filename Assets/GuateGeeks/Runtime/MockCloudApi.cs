using System;
using System.Collections;
using System.Collections.Generic;

namespace GuateGeeks.AwsVr
{
    public sealed class DeploymentEvent
    {
        public string ResourceId, Message;
        public ResourceState State;
        public float Progress;
        public bool Finished, Success;
    }
    public interface ICloudApi
    {
        IEnumerator Connect(Action<bool, string> completed);
        IEnumerator Deploy(Architecture snapshot, bool simulateFailure, Action<DeploymentEvent> progress);
        IEnumerator Invoke(Action<bool, string> completed);
        void Disconnect();
    }
    // All delays, identities, endpoints and results are fabricated locally. No HTTP client or AWS SDK.
    public sealed class MockCloudApi : ICloudApi
    {
        public bool Connected { get; private set; }
        readonly Func<float, object> delay;
        public MockCloudApi(Func<float, object> delay = null) { this.delay = delay ?? (seconds => new UnityEngine.WaitForSecondsRealtime(seconds)); }
        public IEnumerator Connect(Action<bool, string> completed)
        {
            yield return delay(.65f); Connected = true;
            completed(true, "Sesión de demostración lista · API interna simulada");
        }
        public void Disconnect() => Connected = false;
        public IEnumerator Deploy(Architecture snapshot, bool simulateFailure, Action<DeploymentEvent> progress)
        {
            if (!Connected || snapshot.Validate().Count > 0)
            {
                progress(new DeploymentEvent { Finished = true, Success = false, Message = "Conecta la sesión y valida la arquitectura antes de desplegar." }); yield break;
            }
            // Stable topological order also works when the user adds resources in reverse order.
            var ordered = new List<ResourceNode>();
            var done = new HashSet<string>();
            while (ordered.Count < snapshot.nodes.Count)
            {
                foreach (var node in snapshot.nodes)
                {
                    if (done.Contains(node.id)) continue;
                    bool ready = true;
                    foreach (var edge in snapshot.links) if (edge.to == node.id && !done.Contains(edge.from)) ready = false;
                    if (ready) { ordered.Add(node); done.Add(node.id); }
                }
            }
            for (int i = 0; i < ordered.Count; i++)
            {
                var node = ordered[i];
                progress(new DeploymentEvent { ResourceId = node.id, State = ResourceState.Provisioning, Progress = (float)i / ordered.Count, Message = "Creando " + node.name + "…" });
                yield return delay(.9f);
                bool failed = simulateFailure && i == Math.Min(1, ordered.Count - 1);
                progress(new DeploymentEvent { ResourceId = node.id, State = failed ? ResourceState.Failed : ResourceState.Ready,
                    Progress = (float)(i + 1) / ordered.Count, Finished = failed, Success = false,
                    Message = failed ? "Fallo simulado. Desactiva «Simular fallo» y vuelve a intentar." : node.name + " listo" });
                if (failed) yield break;
            }
            progress(new DeploymentEvent { Finished = true, Success = true, Progress = 1f, Message = "Despliegue simulado completado. Prueba el flujo de eventos." });
        }
        public IEnumerator Invoke(Action<bool, string> completed)
        {
            yield return delay(.65f);
            completed(Connected, Connected ? "200 OK · evento procesado · 142 ms (simulados)" : "La sesión de demostración está desconectada.");
        }
    }
}
