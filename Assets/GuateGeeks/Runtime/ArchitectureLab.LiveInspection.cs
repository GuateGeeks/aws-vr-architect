using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    public sealed partial class ArchitectureLab
    {
        Coroutine liveInspection;
        AwsCloudApi inspectionReader;
        AwsCloudApi.InspectionPage readerData;
        TMPro.TMP_Text liveStatus;
        int readerIndex, readerPart;
        bool drawingLiveReader, readerPaused, readerPinned, inspectionSuspended;
        string liveMessage = "CONECTANDO · lectura automática de AWS…";
        string readerSearch = "", readerLevel = "", readerEventId = "";
        bool readerJson = true;
        AwsCloudApi.NodeStatus readerNode;
        string readerMode;
        AwsCloudApi.InspectionEntry[] readerAllEntries = new AwsCloudApi.InspectionEntry[0];
        readonly HashSet<string> observedRecords = new HashSet<string>();
        string observedEvent;
        public bool LiveInspectionActive => inspectionReader != null;

        void StopLiveInspection()
        {
            if (inspectionReader != null && liveStatus) { liveStatus.text = "LECTURA DETENIDA · datos de la última consulta"; liveStatus.color = LabVisuals.Muted; }
            if (liveInspection != null) StopCoroutine(liveInspection);
            liveInspection = null; inspectionReader?.Disconnect(); inspectionReader = null;
            readerData = null; liveStatus = null; readerAllEntries = new AwsCloudApi.InspectionEntry[0];
        }
        void OnApplicationPause(bool paused) { inspectionSuspended = paused;if(paused)StopDiagnostics("Monitor detenido: aplicación en pausa."); }
        void OnApplicationFocus(bool focused) { if(!focused)StopDiagnostics("Monitor detenido: aplicación sin foco."); }
        void OnDisable() { StopAssistant(); StopLiveInspection();AbortCodeOperation(); }
        void StartLiveInspection(AwsCloudApi.NodeStatus node, string mode)
        {
            StopLiveInspection();
            if (!IsCloud || !SessionReady || inspectedState == null) return;
            string slot = inspectedSlot, stack = inspectedState.stackId;
            readerNode = node; readerMode = mode;
            readerPaused = readerPinned = false; readerIndex = readerPart = 0;
            liveMessage = "CONECTANDO · consulta automática cada 3 s · solo lectura";
            ShowInspection(node, mode, new AwsCloudApi.InspectionPage { entries = new AwsCloudApi.InspectionEntry[0] }, 0, 0);
            inspectionReader = Cloud.CreateInspectionReader();
            liveInspection = StartCoroutine(LiveInspectionRoutine(node, mode, slot, stack, Cloud));
        }
        bool ReaderWaiting => readerPaused || inspectionSuspended || !workspaceInspector.gameObject.activeInHierarchy || Busy || EditingText;
        void ReaderStatus(string message, bool error = false)
        {
            liveMessage = message;
            if (liveStatus) { liveStatus.text = message; liveStatus.color = error ? LabVisuals.Orange : LabVisuals.Green; }
        }
        IEnumerator LiveInspectionRoutine(AwsCloudApi.NodeStatus node, string mode, string slot, string stack, AwsCloudApi source)
        {
            var sweep = new List<AwsCloudApi.InspectionEntry>();
            string cursor = "", resume = "", lastSuccess = "sin respuesta";
            float reconciliation = Time.unscaledTime;
            int failures = 0, pages = 0;
            while (inspectionReader != null && Cloud == source && source.Connected)
            {
                while (ReaderWaiting) {
                    if (readerPaused) ReaderStatus("PAUSADO · última lectura " + lastSuccess + " · los datos permanecen visibles");
                    yield return null;
                }
                AwsCloudApi.InspectionPage page = null; string error = null;
                ReaderStatus("LEYENDO AWS · última respuesta " + lastSuccess + (pages > 0 ? " · recorriendo páginas" : ""));
                yield return inspectionReader.Inspect(slot, stack, node.resourceId, mode, cursor, (p, e) => { page = p; error = e; }, cursor.Length == 0 ? resume : "", readerSearch, readerLevel, readerEventId);
                while (ReaderWaiting) yield return null;
                if (page == null)
                {
                    if (!string.IsNullOrEmpty(resume) && error != null && error.StartsWith("HTTP 400")) { resume = cursor = ""; pages = 0; sweep.Clear(); yield return new WaitForSecondsRealtime(3); continue; }
                    failures++; cursor = ""; pages = 0; sweep.Clear();
                    // Identity/permission failures require a fresh explicit resource selection.
                    bool terminal = !inspectionReader.Connected || (error != null && (error.StartsWith("HTTP 400") || error.StartsWith("HTTP 403") || error.StartsWith("HTTP 404") || error.StartsWith("HTTP 409")));
                    ReaderStatus((terminal ? "LECTURA DETENIDA · vuelve a seleccionar el recurso" : "REINTENTANDO") + "\n" + error + " · última lectura: " + lastSuccess, true);
                    if (terminal) break;
                    yield return new WaitForSecondsRealtime(Mathf.Min(30, 3 * Mathf.Pow(2, failures)) + UnityEngine.Random.value);
                    continue;
                }
                failures = 0; pages++;
                sweep.AddRange(page.entries);
                // A bounded local window, with no disk persistence and no duplicate suppression:
                // identical log messages can be separate legitimate AWS events.
                if (sweep.Count > 100) sweep.RemoveRange(0, sweep.Count - 100);
                lastSuccess = DateTime.Now.ToString("HH:mm:ss");
                bool complete = string.IsNullOrEmpty(page.cursor);
                var entries = mode == "logs" ? sweep.AsEnumerable().Reverse().ToArray() : sweep.OrderBy(e => e.text, StringComparer.Ordinal).ToArray();
                liveMessage = "EN VIVO · respuesta " + lastSuccess + " · consulta cada 3 s" + (complete ? "" : "\nRecorriendo páginas AWS · muestra parcial");
                if (complete || readerData == null || readerData.entries.Length == 0) {
                    readerAllEntries = page.incremental ? InspectionTools.Merge(readerAllEntries, entries) : entries;
                    PresentLiveEntries(node, mode, readerAllEntries.Where(e=>InspectionTools.Matches(e, readerSearch, readerLevel, readerEventId)).ToArray());
                }
                ObserveInspection(node, page, stack);
                GuideObserved(mode,page,stack);
                ReaderStatus(liveMessage);
                cursor = page.cursor;
                if (complete) {
                    cursor = ""; resume = page.resume ?? ""; sweep.Clear(); pages = 0;
                    if (Time.unscaledTime - reconciliation > 120) { resume = ""; reconciliation = Time.unscaledTime; }
                }
                yield return new WaitForSecondsRealtime(3);
            }
            inspectionReader?.Disconnect(); inspectionReader = null; liveInspection = null;
        }
        void PresentLiveEntries(AwsCloudApi.NodeStatus node, string mode, AwsCloudApi.InspectionEntry[] entries)
        {
            int index = 0, part = 0;
            if (readerPinned && readerData?.entries.Length > 0)
            {
                var selectedEntry = readerData.entries[Mathf.Clamp(readerIndex, 0, readerData.entries.Length - 1)];
                index = Array.FindIndex(entries, e => InspectionTools.SameIdentity(e, selectedEntry));
                if (index < 0) {
                    var retained = entries.Take(99).ToList(); retained.Add(selectedEntry);
                    entries = retained.ToArray(); index = entries.Length - 1;
                }
                part = readerPart;
            }
            bool changed = readerData == null || readerData.entries.Length != entries.Length ||
                entries.Where((e, i) => !SameInspectionEntry(e, readerData.entries[i])).Any();
            if (changed) ShowInspection(node, mode, new AwsCloudApi.InspectionPage { entries = entries }, index, part);
        }
        static bool SameInspectionEntry(AwsCloudApi.InspectionEntry a, AwsCloudApi.InspectionEntry b) => a.id == b.id && a.title == b.title && a.text == b.text && a.truncated == b.truncated && a.eventId == b.eventId && a.stage == b.stage && a.level == b.level && a.requestId == b.requestId;
        void RestartReader() => StartLiveInspection(readerNode, readerMode);
        void SearchReader()
        {
            OpenDesignKeyboard("BUSCAR EN LOGS / ÍTEMS", readerSearch, 80, text=> { readerSearch=text; RestartReader(); }, true, "Aplicar filtro", "Cancelar búsqueda");
        }
        void ObserveInspection(AwsCloudApi.NodeStatus node, AwsCloudApi.InspectionPage page, string stack)
        {
            if (!IsCloud || !Deployed || stack != Cloud.StackId || string.IsNullOrEmpty(Cloud.LastEventId)) return;
            if (observedEvent != Cloud.LastEventId) { observedEvent=Cloud.LastEventId; observedRecords.Clear(); }
            foreach(var entry in page.entries) {
                if (entry.eventId != observedEvent || entry.stage != "delivered" || entry.nodeId != node.resourceId || string.IsNullOrEmpty(entry.id)) continue;
                if (observedRecords.Count >= 500 || !observedRecords.Add(entry.id)) continue;
                foreach(var link in linkViews) if(!link.IsObservation && link.FromId==entry.nodeId && link.ToId==entry.targetNodeId) link.ConfirmObserved(observedEvent);
            }
        }
    }
}
