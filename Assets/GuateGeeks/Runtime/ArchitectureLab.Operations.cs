using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    public sealed partial class ArchitectureLab
    {
        string inspectedSlot = "1";
        AwsCloudApi.DeploymentStatus inspectedState;
        bool slotOperation;

        public void OpenSlots()
        {
            if (Busy || EditingText || Placing || ConfiguringConnection) return;
            ConnectingMode = false; connectionSource = null; RefreshSelection();
            inspectedState = null; DrawSlots();
            SetStatus("Selecciona un slot para consultar sus recursos reales.");
        }
        void InspectSelectedCloudObject()
        {
            if (Busy || !selected) return;
            if (!IsCloud || !SessionReady || !Deployed || string.IsNullOrEmpty(Cloud.StackId))
            { OpenSlots(); SetStatus("Retoma o despliega esta arquitectura para inspeccionar su objeto en AWS."); return; }
            string resourceId = selected.Model.id, stackId = Cloud.StackId;
            inspectedSlot = Cloud.Slot; inspectedState = null;
            BeginSlotOperation("CONSULTANDO OBJETO", ReadSelectedObjectRoutine(inspectedSlot, stackId, resourceId));
        }
        IEnumerator ReadSelectedObjectRoutine(string slot, string stackId, string resourceId)
        {
            string error = null;
            yield return Cloud.ReadSlot(slot, (state, message) => { inspectedState = state; error = message; });
            EndSlotOperation();
            var node = inspectedState?.stackId == stackId
                ? inspectedState.nodes?.FirstOrDefault(n => n.resourceId == resourceId) : null;
            if (node != null) ShowCloudResource(node);
            else { DrawSlots(); SetStatus(error ?? "El objeto no pertenece al despliegue actual. Revisa los recursos del slot."); }
        }
        void OperationsHeader(string title)
        {
            ClearInspector();
            FitText(title, new Vector2(0, 350), new Vector2(506, 60), 27, Cyan);
        }
        void DrawSlots(int resourcePage = 0)
        {
            OperationsHeader("SLOTS / INSPECCIÓN AWS");
            for (int i = 1; i <= 3; i++)
            {
                string slot = i.ToString();
                EditButton(inspector, "Slot " + slot, new Vector2((i - 2) * 170, 274), new Vector2(160, 58), () => LoadSlot(slot), inspectedSlot == slot ? Cyan : Muted);
            }
            if (!IsCloud || !SessionReady)
            {
                FitText("Conecta AWS desde Ajustes.\n\nEsta vista consulta recursos reales. Tu diseño local permanece en la mesa.", new Vector2(0, 65), new Vector2(506, 290), 25, White);
                EditButton(inspector, "Configurar conexión AWS", new Vector2(0, -180), new Vector2(506, 58), OpenConnectionForm);
            }
            else if (inspectedState == null)
                FitText("Elige Slot 1, 2 o 3 para actualizar.\n\nInspecciona recursos y datos, o limpia un despliegue completo.", new Vector2(0, 50), new Vector2(506, 260), 25, White);
            else
            {
                var state = inspectedState;
                FitText("SLOT " + inspectedSlot + " · " + state.status + "\n" + (state.nodes?.Length ?? 0) + " objetos de arquitectura", new Vector2(0, 196), new Vector2(506, 75), 21, state.status == "ABSENT" ? Green : White);
                var nodes = state.nodes ?? new AwsCloudApi.NodeStatus[0];
                int pages = Mathf.Max(1, (nodes.Length + 3) / 4); resourcePage = Mathf.Clamp(resourcePage, 0, pages - 1);
                for (int i = 0; i < Math.Min(4, nodes.Length - resourcePage * 4); i++)
                {
                    var node = nodes[resourcePage * 4 + i];
                    var b = EditButton(inspector, ((ServiceKind)node.kind) + " · " + (node.name ?? node.resourceId), new Vector2(0, 112 - i * 64), new Vector2(506, 54), () => ShowCloudResource(node));
                    b.Label.richText = false; b.Label.enableAutoSizing = true; b.Label.fontSizeMax = 22; b.Label.fontSizeMin = 17;
                }
                if (pages > 1)
                {
                    int page = resourcePage;
                    EditButton(inspector, "‹ Anteriores", new Vector2(-130, -150), new Vector2(245, 48), () => DrawSlots(Math.Max(0, page - 1)));
                    EditButton(inspector, "Más recursos ›", new Vector2(130, -150), new Vector2(245, 48), () => DrawSlots(Math.Min(pages - 1, page + 1)));
                }
                EditButton(inspector, "Actualizar slot " + inspectedSlot, new Vector2(0, -216), new Vector2(506, 54), () => LoadSlot(inspectedSlot));
                if (state.status != "ABSENT")
                    EditButton(inspector, "Limpiar slot " + inspectedSlot + "…", new Vector2(0, -282), new Vector2(506, 54), ConfirmSlotCleanup, Orange);
            }
            EditButton(inspector, "Volver al diseño", new Vector2(0, -354), new Vector2(506, 50), CloseWorkspace);
            UpdateButtons();
        }
        void LoadSlot(string slot)
        {
            if (Busy) return;
            if (!IsCloud || !SessionReady) { SetStatus("Conecta AWS desde Ajustes."); return; }
            inspectedSlot = slot; inspectedState = null;
            BeginSlotOperation("Consultando slot " + slot, ReadSlotRoutine(slot));
        }
        void BeginSlotOperation(string title, IEnumerator routine)
        {
            slotOperation = true; Busy = true; OperationsHeader(title);
            FitText("Esperando respuesta de AWS…\n\nPuedes dejar de observar. Las operaciones iniciadas en AWS continúan.", new Vector2(0, 90), new Vector2(506, 220), 24, White);
            Button(inspector, "Dejar de observar", new Vector2(0, -260), new Vector2(506, 60), StopSlotOperation);
            UpdateButtons(); operation = StartCoroutine(routine);
        }
        void EndSlotOperation()
        { slotOperation = false; Busy = false; operation = null; SyncCloudSession(); UpdateButtons(); }
        void StopSlotOperation()
        {
            if (!slotOperation) return;
            if (operation != null) StopCoroutine(operation);
            Cloud?.StopWatching(); EndSlotOperation(); inspectedState = null; DrawSlots();
            SetStatus("Observación detenida. Actualiza el slot para consultar el estado real en AWS.");
        }
        IEnumerator ReadSlotRoutine(string slot)
        {
            string error = null;
            yield return Cloud.ReadSlot(slot, (state, message) => { inspectedState = state; error = message; });
            if (slot == Cloud.Slot && inspectedState != null && (inspectedState.status == "ABSENT" || inspectedState.stackId != Cloud.StackId))
            { Deployed = false; EventCount = 0; Graph.ResetStates(); StopFlowPreview(); }
            if(guideStep==5 && slot==Cloud.Slot && inspectedState?.status=="ABSENT") guideCleaned=true;
            EndSlotOperation(); DrawSlots(); SetStatus(error ?? ("Slot " + slot + " · " + inspectedState.status));
        }
        void ConfirmSlotCleanup()
        {
            if (Busy || inspectedState == null || string.IsNullOrEmpty(inspectedState.stackId)) return;
            var state = inspectedState; string slot = inspectedSlot;
            var source=Cloud;
            OperationsHeader("ELIMINAR SLOT " + slot + " EN AWS");
            FitText(Cloud.Endpoint + "\n" + Cloud.Region, new Vector2(0, 240), new Vector2(506, 125), 19, Muted);
            FitText("Se eliminarán todos los recursos del slot, incluidos tablas e ítems, colas, logs y versiones de objetos S3.\n\nEsta acción no se puede deshacer. Tu diseño local se conserva.", new Vector2(0, 65), new Vector2(506, 225), 24, White);
            FitText(state.stackId, new Vector2(0, -130), new Vector2(506, 145), 18, Muted);
            EditButton(inspector, "Cancelar", new Vector2(0, -257), new Vector2(506, 58), () => DrawSlots());
            EditButton(inspector, "Sí, eliminar slot " + slot, new Vector2(0, -336), new Vector2(506, 64), () =>
            {
                if(source!=Cloud || !SessionReady || Busy){SetStatus("La sesión cambió. Actualiza y confirma el slot de nuevo.");return;}
                StopFlowPreview();
                if (slot == Cloud.Slot) { Deployed = false; EventCount = 0; Graph.ResetStates(); RefreshSelection(); }
                BeginSlotOperation("LIMPIANDO SLOT " + slot, CleanSlotRoutine(slot, state.stackId));
            }, Orange);
        }
        IEnumerator CleanSlotRoutine(string slot, string stackId)
        {
            bool success = false; string result = null;
            yield return Cloud.CleanSlot(slot, stackId, SetStatus, (ok, text) => { success = ok; result = text; });
            if(guideStep==5 && success && slot==Cloud.Slot) guideCleaned=true;
            EndSlotOperation(); inspectedState = null; DrawSlots(); SetStatus(result); Feedback.Play(success ? 1 : 2);
        }
        void ShowCloudResource(AwsCloudApi.NodeStatus node)
        {
            OperationsHeader("RECURSO / SLOT " + inspectedSlot);
            FitText((node.name ?? node.resourceId) + "\n" + (ServiceKind)node.kind + "\n" + node.status, new Vector2(0, 220), new Vector2(506, 180), 24, White);
            FitText("Identificador físico AWS\n" + node.physicalId, new Vector2(0, 45), new Vector2(506, 145), 20, Muted);
            string mode = node.kind == 1 ? "logs" : node.kind == 2 ? "items" : null;
            if (mode != null && inspectedState?.status == "CREATE_COMPLETE" && SessionReady)
                EditButton(inspector, mode == "logs" ? "Inspeccionar logs" : "Inspeccionar ítems", new Vector2(0, -140), new Vector2(506, 64), () => LoadInspection(node, mode, ""), Green);
            else FitText("Logs disponibles en nodos Lambda.\nÍtems disponibles en tablas DynamoDB.", new Vector2(0, -140), new Vector2(506, 110), 22, Muted);
            EditButton(inspector, "Volver a slots", new Vector2(0, -335), new Vector2(506, 58), () => DrawSlots());
        }
        void LoadInspection(AwsCloudApi.NodeStatus node, string mode, string cursor)
        {
            if (Busy) return;
            readerSearch = readerLevel = readerEventId = "";
            StartLiveInspection(node, mode);
        }
        // Preserve all returned text while keeping each VR page readable, including multiline JSON.
        public static List<string> InspectionTextPages(string text)
        {
            var pages = new List<string>(); var lines = new List<string>();
            foreach (var line in (text ?? "").Replace("\r", "").Replace("\t", "  ").Split('\n'))
            {
                if (line.Length == 0) lines.Add("");
                else for (int i = 0; i < line.Length; i += 38) lines.Add(line.Substring(i, Math.Min(38, line.Length - i)));
            }
            for (int i = 0; i < lines.Count; i += 12) pages.Add(string.Join("\n", lines.Skip(i).Take(12)));
            return pages;
        }
        void ReaderText(string text, Vector2 pos, Vector2 size, int fontSize, Color color)
        {
            var label = Text(workspaceInspector, text, pos, size, fontSize, color);
            label.richText = false; label.fontSizeMax = fontSize; label.fontSizeMin = 20; label.enableAutoSizing = true;
        }
        void ShowInspection(AwsCloudApi.NodeStatus node, string mode, AwsCloudApi.InspectionPage data, int index, int part)
        {
            drawingLiveReader = true;
            try { ClearInspector(); } finally { drawingLiveReader = false; }
            SetReaderSize(new Vector2(1160, 1160));
            index = Mathf.Clamp(index, 0, Mathf.Max(0, data.entries.Length - 1));
            readerIndex = index; readerPart = part; readerData = data;
            ReaderText((mode == "logs" ? "LOGS / LAMBDA" : "ÍTEMS / DYNAMODB") + "   ·   SLOT " + inspectedSlot, new Vector2(0, 525), new Vector2(1100, 50), 30, Cyan);
            ReaderText((node.name ?? node.resourceId) + "\n" + node.physicalId, new Vector2(0, 465), new Vector2(1100, 64), 23, White);
            liveStatus = Text(workspaceInspector, liveMessage, new Vector2(0, 398), new Vector2(1100, 66), 20, Green);
            liveStatus.richText = false;
            var searchButton=EditButton(workspaceInspector, "Buscar: " + (readerSearch.Length==0 ? "todos" : readerSearch), new Vector2(-275,325),new Vector2(535,52),SearchReader);
            searchButton.Label.richText=false;
            EditButton(workspaceInspector, mode=="logs" ? "Nivel: " + (readerLevel.Length==0?"TODOS":readerLevel) : "Todos los ítems",new Vector2(135,325),new Vector2(250,52),()=> { readerLevel=mode!="logs"?"":readerLevel==""?"ERROR":readerLevel=="ERROR"?"WARN":readerLevel=="WARN"?"INFO":""; RestartReader(); });
            EditButton(workspaceInspector, readerJson?"Vista: JSON":"Vista: original",new Vector2(415,325),new Vector2(260,52),()=> {readerJson=!readerJson;ShowInspection(node,mode,data,index,0);});
            var correlate=EditButton(workspaceInspector, readerEventId.Length==0?"Seguir último evento enviado":"Evento: "+readerEventId,new Vector2(-135,265),new Vector2(820,50),()=> {readerEventId=readerEventId.Length==0?Cloud?.LastEventId ?? "":""; RestartReader();},Orange);
            correlate.Label.richText=false; correlate.Label.enableAutoSizing=true; correlate.Label.fontSizeMax=22; correlate.Label.fontSizeMin=20; correlate.SetAvailable(readerEventId.Length>0 || !string.IsNullOrEmpty(Cloud?.LastEventId));
            EditButton(workspaceInspector,"Quitar filtros",new Vector2(420,265),new Vector2(250,50),()=> {readerSearch=readerLevel=readerEventId="";RestartReader();});
            Block(workspaceInspector, new Vector2(-205, -18), new Vector2(2, 500), LineColor);
            var entries = data.entries;
            var entry = entries.Length == 0 ? null : entries[index];
            int listStart = index / 10 * 10;
            ReaderText("REGISTROS · " + entries.Length + " en memoria", new Vector2(-390, 213), new Vector2(330, 40), 20, Cyan);
            for (int i = listStart; i < Math.Min(entries.Length, listStart + 10); i++)
            {
                int selectedEntry = i;
                var button = EditButton(workspaceInspector, (i + 1) + " · " + entries[i].title, new Vector2(-390, 159 - (i-listStart) * 44), new Vector2(330, 39), () => { readerPinned = true; ShowInspection(node, mode, data, selectedEntry, 0); }, i == index ? Green : Muted);
                button.Label.richText = false; button.Label.fontSize = 20;
            }
            var pages = InspectionTextPages(entry == null ? "Sin resultados para estos filtros. El lector consulta AWS automáticamente." : readerJson && !entry.truncated ? InspectionTools.Json(entry.text) : entry.text);
            part = Mathf.Clamp(part, 0, pages.Count - 1); readerPart = part;
            ReaderText(entry == null ? "SIN RESULTADOS" : "REGISTRO " + (index + 1) + " / " + entries.Length + " · " + entry.title, new Vector2(178, 211), new Vector2(700, 42), 22, Cyan);
            ReaderText(pages[part], new Vector2(178, -7), new Vector2(700, 390), 27, White);
            ReaderText("Texto " + (part + 1) + " / " + pages.Count, new Vector2(178, -231), new Vector2(700, 36), 21, Muted);
            var previous = EditButton(workspaceInspector, "‹ Texto anterior", new Vector2(-5, -285), new Vector2(335, 55), () => { if (part > 0) { readerPinned = true; ShowInspection(node, mode, data, index, part - 1); } });
            previous.SetAvailable(part > 0);
            var next = EditButton(workspaceInspector, "Texto siguiente ›", new Vector2(360, -285), new Vector2(335, 55), () => { if (part + 1 < pages.Count) { readerPinned = true; ShowInspection(node, mode, data, index, part + 1); } });
            next.SetAvailable(part + 1 < pages.Count);
            EditButton(workspaceInspector, readerPaused ? "Reanudar lectura" : "Pausar lectura", new Vector2(-390, -361), new Vector2(330, 60), () => { readerPaused = !readerPaused; ReaderStatus(readerPaused?"PAUSADO · datos de la última consulta":"REANUDANDO · datos de la última consulta"); ShowInspection(node, mode, data, index, part); });
            EditButton(workspaceInspector, "Seguir recientes", new Vector2(0, -361), new Vector2(390, 60), () => { readerPinned = false; readerPaused = false; ShowInspection(node, mode, data, 0, 0); });
            var older = EditButton(workspaceInspector, "‹", new Vector2(-475, -300), new Vector2(130, 42), () => { readerPinned = true; ShowInspection(node, mode, data, Math.Max(0, listStart - 10), 0); });
            older.SetAvailable(listStart > 0);
            var newer = EditButton(workspaceInspector, "›", new Vector2(-310, -300), new Vector2(130, 42), () => { readerPinned = true; ShowInspection(node, mode, data, listStart + 10, 0); });
            newer.SetAvailable(listStart + 10 < entries.Length);
            EditButton(workspaceInspector, "Volver al recurso", new Vector2(400, -361), new Vector2(330, 60), () => ShowCloudResource(node));
            ReaderText(entry?.truncated == true ? "REGISTRO RECORTADO A 4096 CARACTERES · consulta CloudWatch/DynamoDB para el contenido completo" : readerPinned ? "LECTURA FIJADA · el registro se conserva aunque deje de aparecer en AWS" : mode == "logs" ? "AUTOMÁTICO · últimos 15 min · hasta 100 registros · latencia de CloudWatch" : "AUTOMÁTICO · muestra de hasta 100 ítems · consistencia eventual de DynamoDB", new Vector2(0, -426), new Vector2(1100, 50), 20, entry?.truncated == true ? Orange : Muted);
            ReaderText("Evento: " + (string.IsNullOrEmpty(entry?.eventId)?"sin correlación":entry.eventId) + "\n" + (entry?.stage ?? "") + " · " + (entry?.level ?? "") + " · " + (entry?.requestId ?? ""),new Vector2(0,-505),new Vector2(1100,80),21,Muted);
        }
    }
}
