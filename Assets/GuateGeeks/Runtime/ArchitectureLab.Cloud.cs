using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Text = TMPro.TMP_Text;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    public sealed partial class ArchitectureLab
    {
        public bool IsCloud => api is AwsCloudApi;
        AwsCloudApi Cloud => api as AwsCloudApi;
        Text modeText, apiText, cloudDetails;
        LabTarget stopCloudButton;
        readonly List<LabTarget> cloudButtons = new List<LabTarget>();
        [Serializable] sealed class CloudCheckpoint
        {
            public string endpoint, slot, stackId;
            public Architecture architecture;
        }
        CloudCheckpoint checkpoint;
        string CheckpointPath => Path.Combine(Application.persistentDataPath, "aws-cloud-checkpoint.json");
        void BuildCloudPanel()
        {
            var panel = Panel(world, "05 · Cloud connection", new Vector3(-2.12f, 3.32f, 2.5f), new Vector2(660, 500), -29);
            Text(panel, "05 / CONEXIÓN AWS", new Vector2(0, 153), new Vector2(600, 40), 24, Cyan);
            cloudDetails = Text(panel, "", new Vector2(0, 78), new Vector2(600, 95), 20, White);
            cloudButtons.Add(Button(panel, "Conectar AWS", new Vector2(-155, -12), new Vector2(290, 54), ConnectSaved, Orange));
            cloudButtons.Add(Button(panel, "Volver a demo", new Vector2(155, -12), new Vector2(290, 54), UseMock));
            cloudButtons.Add(Button(panel, "Recuperar diseño AWS", new Vector2(-155, -81), new Vector2(290, 54), RestoreCheckpoint));
            stopCloudButton = Button(panel, "Dejar de observar AWS", new Vector2(155, -81), new Vector2(290, 54), CancelDeployment);
            cloudButtons.Add(Button(panel, "Configurar conexión", new Vector2(-155, -146), new Vector2(290, 48), OpenConnectionForm));
            Button(panel, "Desconectar", new Vector2(155, -146), new Vector2(290, 48), DisconnectCloud);
            Button(panel, "Olvidar credencial", new Vector2(0, -212), new Vector2(600, 48), ForgetCredential);
            BuildConnectionForm(); RefreshCloudLabels();
        }
        // Settings originate in the VR form, secure store, or Editor test window.
        public void ConfigureCloud(CloudConnection settings, ICloudTransport transport = null, Func<float, object> delay = null)
        {
            if (Busy) return;
            var next = new AwsCloudApi(settings, transport, delay);
            StopLiveInspection();
            settings.password = null;
            api.Disconnect(); api = next; SessionReady = false; SimulateFailure = false;
            Changed(); RefreshCloudLabels(); operation = StartCoroutine(ConnectSession());
        }
        void UseMock()
        {
            if (Busy) return;
            DisconnectCloud(); api = new MockCloudApi(); SessionReady = false;
            if (Graph.region != "us-east-1" && Graph.region != "us-west-2") Graph.region = "us-east-1";
            Changed(); RefreshCloudLabels(); operation = StartCoroutine(ConnectSession());
        }
        void RefreshCloudLabels()
        {
            RefreshGlobalSettings();
            if (modeText) modeText.text = IsCloud ? "AWS REAL · RECURSOS CON COSTO" : "100% LOCAL · SIN COSTOS AWS";
            if (apiText) apiText.text = IsCloud ? "AWS REAL / SLOT " + Cloud.Slot + " / " + Cloud.Host : "MOCK API / Ningún recurso se crea en AWS";
            if (deployButton) deployButton.Label.text = IsCloud ? "Desplegar / retomar AWS →" : "Desplegar demo  →";
            if (cloudDetails) cloudDetails.text = IsCloud ? (SessionReady ? "Conectado" : "Sin sesión") + " · Slot " + Cloud.Slot + "\n" + Cloud.Host : "Modo demo · sin conexión AWS\nConfigura la conexión desde este visor.";
            if (Graph != null) UpdateCounts();
        }
        void SyncCloudSession()
        {
            if (!IsCloud) return;
            SessionReady = Cloud.Connected;
            sessionText.text = SessionReady ? "● AWS · SLOT " + Cloud.Slot : "SIN SESIÓN AWS";
            RefreshCloudLabels();
        }
        IEnumerator ValidateCloud()
        {
            Busy = true; UpdateButtons();
            yield return Cloud.Validate(Graph.Copy(), (ok, message) => { Feedback.Play(ok ? 1 : 2); SetStatus(message); });
            Busy = false; operation = null; SyncCloudSession(); UpdateButtons();
        }
        bool SaveCloudCheckpoint(bool starting)
        {
            if (!IsCloud) return true;
            try
            {
                if (starting) checkpoint = new CloudCheckpoint { endpoint = Cloud.Endpoint, slot = Cloud.Slot, architecture = Graph.Copy() };
                if (checkpoint == null) return true;
                checkpoint.stackId = Cloud.StackId;
                File.WriteAllText(CheckpointPath, JsonUtility.ToJson(checkpoint, true));
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            { SetStatus("No se pudo guardar el punto de recuperación AWS en este dispositivo."); return false; }
        }
        void RestoreCheckpoint()
        {
            if (Busy) return;
            if (!IsCloud || !SessionReady) { SetStatus("Conecta primero la API y el slot originales."); return; }
            try
            {
                var saved = JsonUtility.FromJson<CloudCheckpoint>(File.ReadAllText(CheckpointPath));
                if (saved == null || saved.endpoint != Cloud.Endpoint || saved.slot != Cloud.Slot || saved.architecture == null ||
                    AwsCloudApi.ValidateLocally(saved.architecture, Cloud.Region).Count != 0) throw new ArgumentException();
                Remember(); SetGraph(saved.architecture); checkpoint = saved;
                SetStatus("Diseño AWS recuperado. «Desplegar / retomar AWS» consulta o crea el mismo diseño en el mismo slot.");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { SetStatus("No hay un punto de recuperación válido para esta API y slot."); }
        }
    }
}
