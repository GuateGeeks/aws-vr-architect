using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Text = TMPro.TMP_Text;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    public sealed partial class ArchitectureLab
    {
        RectTransform catalogPanel, designKeyboard;
        Transform placementGhost;
        ServiceKind placementKind;
        string draftId, draftName, keyboardValue;
        int draftSetting, keyboardLimit, libraryPage, relationPage;
        Action<string> acceptKeyboard;
        Text keyboardText;
        bool inspectorPinned = true;
        float flowRemaining;
        string libraryDirectory = null;
        public bool HasPendingDefinition => selected && draftId == selected.Model.id && (draftName != selected.Model.name || draftSetting != selected.Model.setting);
        public bool Placing => placementGhost;
        public bool EditingText => designKeyboard && designKeyboard.gameObject.activeSelf;
        public bool FlowPreviewActive => flowRemaining > 0;
        DesignLibrary Library => new DesignLibrary(libraryDirectory ?? Path.Combine(Application.persistentDataPath, "design-library"));

        void BuildStudioTools(RectTransform dock)
        {
            string[] names = { "Previsualizar flujo", "Biblioteca", "Ordenar", "Slots / inspección", "Ajustes", "ATLAS IA" };
            Action[] actions = { PreviewFlow, () => ShowLibrary(0), ArrangeDesign, OpenSlots, ToggleSettings, OpenAssistant };
            float[] widths = { 234, 174, 134, 234, 164, 190 };
            float left = -590;
            for (int i = 0; i < names.Length; i++)
            {
                var position = new Vector2(left + widths[i] / 2, -143);var size = new Vector2(widths[i], 48);
                if (names[i] == "ATLAS IA") assistantDock=Button(dock,names[i],position,size,actions[i]);
                else if (names[i] == "Ajustes") Button(dock, names[i], position, size, actions[i]);
                else EditButton(dock, names[i], position, size, actions[i]);
                left += widths[i] + 10;
            }
        }
        void ToggleSettings() => ToggleUnifiedSettings();
        public void StartPlacement(ServiceKind kind)
        {
            if (Busy || EditingText || ConfiguringConnection) return;
            if (Graph.nodes.Count >= Architecture.MaxNodes) { SetStatus("Mesa llena: máximo 12 objetos."); return; }
            CancelPlacement(); ConnectingMode = false; connectionSource = null; RefreshSelection(); Rig.ReleaseForConfiguration();
            placementKind = kind;
            placementGhost = Shape(world, "Placement preview", PrimitiveType.Cube, new Vector3(0, 1.45f, 2), Vector3.one * .38f * ComponentScale, ServiceCatalog.Get(kind).Color).transform;
            placementGhost.GetComponent<Renderer>().sharedMaterial = SpecialMaterial("LabHologram", ServiceCatalog.Get(kind).Color);
            ClearInspector(); Text(inspector, "CREAR / " + ServiceCatalog.Get(kind).Name, new Vector2(0, 320), new Vector2(506, 80), 30, Cyan);
            Text(inspector, "Apunta al espacio de la mesa.\nGatillo sobre espacio libre: colocar.\n\nB / Y / Esc: cancelar.\nTodavía no se ha creado el objeto.", new Vector2(0, 105), new Vector2(506, 250), 25, White);
            Button(inspector, "Confirmar ubicación", new Vector2(0, -150), new Vector2(506, 65), ConfirmPlacement, Green);
            Button(inspector, "Cancelar colocación", new Vector2(0, -250), new Vector2(506, 60), () => { CancelPlacement(); CloseWorkspace(); });
            SetStatus("COLOCAR · " + ServiceCatalog.Get(kind).Name + " · confirma o cancela"); UpdateButtons();
        }
        public void PreviewPlacement(Vector3 point) { if (placementGhost) placementGhost.localPosition = ClampWorkspace(point); }
        public void ConfirmPlacement()
        {
            if (!Placing || Busy) return;
            var point = placementGhost.localPosition;
            if (Graph.nodes.Any(n => Vector3.Distance(n.position, point) < .48f*ComponentScale)) { SetStatus("Elige un espacio libre: el objeto está demasiado cerca de otro."); return; }
            Remember(); var node = Graph.Add(placementKind, point); CancelPlacement(); CreateView(node); Changed(); selected = views[node.id]; draftId = null;
            RefreshSelection(); CloseWorkspace(); SetStatus(node.name + " creado. Define sus propiedades y conexiones."); Feedback.Play(1);
        }
        void CancelPlacement() { if (placementGhost) { placementGhost.gameObject.SetActive(false); Destroy(placementGhost.gameObject); placementGhost = null; } }
        void PrepareDraft()
        {
            if (!selected || draftId == selected.Model.id) return;
            draftId = selected.Model.id; draftName = selected.Model.name; draftSetting = selected.Model.setting;
        }
        void DrawDefinition()
        {
            PrepareDraft(); var node = selected.Model; var def = ServiceCatalog.Get(node.kind);
            var name = EditButton(inspector, "Nombre: " + draftName, new Vector2(0, 315), new Vector2(506, 62), () => OpenDesignKeyboard("NOMBRE VISIBLE / NO ES EL NOMBRE FÍSICO AWS", draftName, 80, v => { draftName = v; ShowInspector(); }));
            name.Label.richText = false; name.Label.fontSizeMax = 22; name.Label.enableAutoSizing = true; name.Label.fontSizeMin = 12;
            Text(inspector, def.Name + " / " + def.Category, new Vector2(0, 255), new Vector2(506, 38), 19, def.Color);
            Text(inspector, def.SettingLabel.ToUpperInvariant() + " · elige y aplica", new Vector2(0, 210), new Vector2(506, 35), 17, Muted);
            int count = DesignSemantics.OptionCount(node.kind);
            for (int i = 0; i < count; i++) {
                int option = i; string label = node.kind == ServiceKind.EventBridge ? "Bus personalizado" : def.Settings[i];
                var button = EditButton(inspector, label, new Vector2(count == 1 ? 0 : (i % 2 == 0 ? -130 : 130), 153 - (i / 2) * 63), new Vector2(count == 1 ? 506 : 245, 54), () => { draftSetting = option; ShowInspector(); }, draftSetting == i ? Green : Muted);
                if (draftSetting == i) button.Surface.Accent = Green;
            }
            EditButton(inspector, "Aplicar cambios", new Vector2(-130, 20), new Vector2(245, 54), ApplyDefinition, Green);
            EditButton(inspector, "Cancelar edición", new Vector2(130, 20), new Vector2(245, 54), () => { draftId = null; ShowInspector(); SetStatus("Edición descartada."); });
            EditButton(inspector, "Relaciones", new Vector2(-130, -53), new Vector2(245, 54), () => ShowRelations(0));
            EditButton(inspector, "Ayuda / ficha", new Vector2(130, -53), new Vector2(245, 54), ShowDefinitionHelp);
        }
        public void ApplyDefinition()
        {
            if (!selected || Busy) return;
            var node = selected.Model;
            if (string.IsNullOrWhiteSpace(draftName) || draftName.Trim().Length > 80) { SetStatus("Escribe un nombre visible de 1 a 80 caracteres."); return; }
            if (node.kind == ServiceKind.SQS && draftSetting == 1 && Graph.links.Any(l => l.to == node.id && Graph.Find(l.from).kind == ServiceKind.S3)) { SetStatus("Desconecta la notificación S3 antes de elegir FIFO."); return; }
            if (node.name != draftName.Trim() || node.setting != draftSetting) { Remember(); node.name = draftName.Trim(); node.setting = draftSetting; Changed(); }
            draftId = null; ShowInspector(); SetStatus("Definición aplicada. Revisa el diseño antes de desplegar.");
        }
        void ShowDefinitionHelp() => InObjectInspector(DrawDefinitionHelp);
        void DrawDefinitionHelp()
        {
            if (!selected) return;
            var n = selected.Model; ClearInspector();
            Text(inspector, ServiceCatalog.Get(n.kind).Name, new Vector2(0, 335), new Vector2(506, 65), 32, Cyan);
            Text(inspector, DesignSemantics.Help(n.kind), new Vector2(0, 140), new Vector2(506, 290), 25, White);
            Text(inspector, "Nombre visible: etiqueta del diseño. AWS genera sus propios identificadores físicos.", new Vector2(0, -74), new Vector2(506, 110), 21, Muted);
            EditButton(inspector, inspectorPinned ? "Ficha: fija · acercar al objeto" : "Ficha: contextual · fijar aquí", new Vector2(0, -190), new Vector2(506, 58), () => { inspectorPinned = !inspectorPinned; PositionInspector(); ShowDefinitionHelp(); });
            EditButton(inspector, "Volver a definir", new Vector2(0, -295), new Vector2(506, 58), ShowInspector);
        }
        void PositionInspector()
        {
            if (inspectorPinned || !selected || objectInspector.GetComponent<LabMenu>().Grabbed) return;
            objectInspector.position = selected.transform.position + new Vector3(.95f, .1f, -.35f);
            var direction = objectInspector.position - Rig.ViewCamera.transform.position; direction.y = 0;
            if (direction.sqrMagnitude > .01f) objectInspector.rotation = Quaternion.LookRotation(direction);
        }
        void ShowRelations(int page) => InObjectInspector(() => DrawRelations(page));
        void DrawRelations(int page)
        {
            if (!selected) return;
            var node = selected.Model; var edges = Graph.links.Where(l => l.from == node.id || l.to == node.id).ToList();
            relationPage = Mathf.Clamp(page, 0, Mathf.Max(0, edges.Count - 1)); ClearInspector();
            FitText("RELACIONES / " + node.name, new Vector2(0, 337), new Vector2(506, 90), 27, Cyan);
            if (edges.Count > 0) {
                var edge = edges[relationPage]; var a = Graph.Find(edge.from); var b = Graph.Find(edge.to);
                FitText(a.name + "\n↓ " + DesignSemantics.Operation(a.kind, b.kind) + "\n" + b.name, new Vector2(0, 155), new Vector2(506, 220), 25, White);
                HighlightEdge(edge);
                EditButton(inspector, "Ir al destino", new Vector2(-130, 2), new Vector2(245, 55), () => Select(views[b.id]));
                EditButton(inspector, "Quitar enlace", new Vector2(130, 2), new Vector2(245, 55), () => { Remember(); Graph.links.Remove(edge); RebuildLinks(); Changed(); ShowRelations(relationPage); }, Orange);
                EditButton(inspector, "← Anterior", new Vector2(-130, -75), new Vector2(245, 50), () => ShowRelations(relationPage - 1));
                EditButton(inspector, "Siguiente →", new Vector2(130, -75), new Vector2(245, 50), () => ShowRelations(relationPage + 1));
            } else Text(inspector, "Sin relaciones. Usa el puerto SALIDA de un objeto y la ENTRADA del destino.", new Vector2(0, 135), new Vector2(506, 170), 25, Muted);
            EditButton(inspector, "Desconectar recurso", new Vector2(0, -155), new Vector2(506, 53), () => { Remember(); Graph.links.RemoveAll(l => l.from == node.id || l.to == node.id); RebuildLinks(); Changed(); ShowRelations(0); });
            EditButton(inspector, "Eliminar recurso", new Vector2(0, -234), new Vector2(506, 53), () => { Remember(); var next = Graph.Copy(); next.Remove(node.id); SetGraph(next); }, Hex("#FF8290"));
            EditButton(inspector, "Volver a definir", new Vector2(0, -320), new Vector2(506, 55), ShowInspector);
        }
        void HighlightEdge(ResourceLink edge)
        {
            foreach (var v in views.Values) v.SetSelected(v.Model.id == edge.from || v.Model.id == edge.to);
            foreach (var link in linkViews) link.SetHighlighted(link.FromId == edge.from && link.ToId == edge.to);
        }
        public void InspectLink(string from, string to)
        {
            if (Busy || Placing || EditingText || ConfiguringConnection) return;
            ConnectingMode = false; connectionSource = null; selected = views[from];
            var edges = Graph.links.Where(l => l.from == from || l.to == from).ToList();
            ShowRelations(edges.FindIndex(l => l.from == from && l.to == to));
        }
        public void SelectPort(NodeView node, bool output)
        {
            if (Busy || Placing || EditingText || ConfiguringConnection) return;
            if (output) { ConnectingMode = true; connectionSource = node.Model.id; selected = node; RefreshSelection(); ShowInspector(); SetStatus("SALIDA · " + node.Model.name + " · elige una ENTRADA compatible."); }
            else if (ConnectingMode && connectionSource != null) Select(node);
            else SetStatus("Empieza por el puerto SALIDA del objeto de origen.");
        }
        public void PreviewFlow()
        {
            if (Busy || Placing) return;
            if (FlowPreviewActive) { StopFlowPreview(); SetStatus("Previsualización detenida."); return; }
            var issues = Graph.Validate(); if (issues.Count > 0) { SetStatus(issues[0]); return; }
            if (linkViews.All(l => l.IsObservation)) { SetStatus("Este diseño sólo tiene asociaciones de observabilidad; no hay una ruta de datos para animar."); return; }
            // Longest path depth gives a clear ordered illustration, not timing measured in AWS.
            var depth = Graph.nodes.ToDictionary(n => n.id, n => 0);
            for (int i = 0; i < Graph.nodes.Count; i++) foreach (var l in Graph.links)
                if (Graph.Find(l.to).kind != ServiceKind.CloudWatch) depth[l.to] = Mathf.Max(depth[l.to], depth[l.from] + 1);
            foreach (var link in linkViews) if (!link.IsObservation) link.Preview(depth[link.FromId] * 1.5f, 1.5f, Feedback.ReducedMotion);
            flowRemaining = (depth.Values.Max() + 1) * 1.5f;
            UpdateCounts();
            SetStatus("PREVISUALIZACIÓN DEL DISEÑO · cápsulas ilustrativas · no es tráfico AWS observado");
        }
        public void StopFlowPreview() { flowRemaining = 0; foreach (var link in linkViews) link.StopPreview(); if (Graph != null && countsText) UpdateCounts(); }
        void Update() { TickGuide(); TickAssistant();TickCodeEditor(); if (flowRemaining > 0) { flowRemaining -= Time.unscaledDeltaTime; if (flowRemaining <= 0) { StopFlowPreview(); SetStatus("Previsualización finalizada. Edita o revisa tu arquitectura."); } } }
        void ArrangeDesign()
        {
            if (views.Values.Any(v => v.Grabbed)) { SetStatus("Suelta los objetos antes de ordenar."); return; }
            Remember();
            for (int i = 0; i < Graph.nodes.Count; i++) { var n = Graph.nodes[i]; n.position = TablePosition(i); views[n.id].transform.localPosition = n.position; }
            SetStatus("Objetos distribuidos en la mesa. Deshacer recupera sus posiciones; el despliegue permanece igual.");
        }
        void ShowLibrary(int page)
        {
            List<SavedDesign> entries;
            try { entries = Library.Read(); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { SetStatus("No se pudo leer la biblioteca."); return; }
            libraryPage = Mathf.Clamp(page, 0, Mathf.Max(0, entries.Count - 1)); ClearInspector();
            Text(inspector, "BIBLIOTECA LOCAL", new Vector2(0, 355), new Vector2(506, 50), 28, Cyan);
            EditButton(inspector, "Guardar diseño con nombre", new Vector2(0, 285), new Vector2(506, 58), () => OpenDesignKeyboard("NOMBRE DEL DISEÑO", "Diseño " + (entries.Count + 1), 40, SaveNamedDesign));
            if (entries.Count > 0) {
                var entry = entries[libraryPage];
                var title = Text(inspector, entry.name, new Vector2(0, 205), new Vector2(506, 75), 28, White); title.richText = false; title.fontSizeMax = 28; title.enableAutoSizing = true;
                DrawThumbnail(entry.architecture);
                Text(inspector, (libraryPage + 1) + " / " + entries.Count + " · " + entry.architecture.nodes.Count + " objetos · " + entry.architecture.region, new Vector2(0, -75), new Vector2(506, 40), 19, Muted);
                EditButton(inspector, "Abrir este diseño", new Vector2(0, -143), new Vector2(506, 60), () => { Remember(); var next = entry.architecture.Copy(); foreach (var n in next.nodes) n.position = ClampWorkspace(n.position); SetGraph(next); SetStatus("Diseño abierto. Deshacer recupera el anterior; no se ha desplegado."); });
                EditButton(inspector, "← Anterior", new Vector2(-130, -225), new Vector2(245, 55), () => ShowLibrary(libraryPage - 1));
                EditButton(inspector, "Siguiente →", new Vector2(130, -225), new Vector2(245, 55), () => ShowLibrary(libraryPage + 1));
            } else Text(inspector, "Guarda tus diseños para volver a abrirlos aquí. Se conserva su disposición espacial. No se guardan credenciales.", new Vector2(0, 40), new Vector2(506, 280), 26, White);
            EditButton(inspector, "Volver al diseño", new Vector2(0, -330), new Vector2(506, 58), CloseWorkspace);
        }
        void SaveNamedDesign(string name)
        {
            try { Library.Save(name, Graph); ShowLibrary(0); SetStatus("Diseño guardado en la biblioteca local."); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { SetStatus("No se guardó: " + e.Message); ShowLibrary(0); }
        }
        void DrawThumbnail(Architecture graph)
        {
            // Reconstructed miniature uses only graph geometry; no camera image or credentials are persisted.
            if (graph.nodes.Count == 0) return;
            var positions = new Dictionary<string, Vector2>();
            float minX = graph.nodes.Min(n => n.position.x), maxX = graph.nodes.Max(n => n.position.x);
            float minZ = graph.nodes.Min(n => n.position.z), maxZ = graph.nodes.Max(n => n.position.z);
            foreach (var n in graph.nodes) positions[n.id] = new Vector2(Mathf.InverseLerp(minX - .2f, maxX + .2f, n.position.x) * 420 - 210, Mathf.InverseLerp(minZ - .2f, maxZ + .2f, n.position.z) * 150 - 25);
            foreach (var l in graph.links) { var a = positions[l.from]; var b = positions[l.to]; var bar = Block(inspector, (a + b) / 2, new Vector2((b - a).magnitude, 2), Muted); bar.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg); }
            foreach (var n in graph.nodes) { Block(inspector, positions[n.id], new Vector2(48, 34), PanelColor); Text(inspector, ServiceCatalog.Get(n.kind).Glyph, positions[n.id], new Vector2(48, 34), 18, ServiceCatalog.Get(n.kind).Color, TextAnchor.MiddleCenter); }
        }
        void OpenDesignKeyboard(string title, string value, int limit, Action<string> accept, bool allowEmpty = false, string acceptLabel = "Aceptar nombre", string cancelLabel = "Cancelar nombre")
        {
            keyboardCodeMode=false;
            Rig.ReleaseForConfiguration(); ConnectingMode = false; connectionSource = null; RefreshSelection();
            if (designKeyboard) { designKeyboard.gameObject.SetActive(false); Destroy(designKeyboard.gameObject); }
            designKeyboard = Panel(world, "Design keyboard", new Vector3(0, 1.9f, 1.15f), new Vector2(1000, 740));
            keyboardValue = value; keyboardLimit = limit; acceptKeyboard = accept;
            Text(designKeyboard, title, new Vector2(0, 315), new Vector2(920, 60), 24, Cyan);
            keyboardText = Text(designKeyboard, value, new Vector2(0, 228), new Vector2(920, 96), 26, White); keyboardText.richText = false;
            string[] rows = { "1234567890", "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM-_" };
            for (int r = 0; r < rows.Length; r++) for (int c = 0; c < rows[r].Length; c++) { string key = rows[r][c].ToString(); Button(designKeyboard, key, new Vector2((c - (rows[r].Length - 1) / 2f) * 88, 115 - r * 68), new Vector2(80, 58), () => TypeDesignText(key)); }
            Button(designKeyboard, "Espacio", new Vector2(-310, -185), new Vector2(260, 55), () => TypeDesignText(" "));
            Button(designKeyboard, "Borrar", new Vector2(0, -185), new Vector2(260, 55), () => TypeDesignText(null));
            Button(designKeyboard, "Vaciar", new Vector2(310, -185), new Vector2(260, 55), () => { keyboardValue = ""; keyboardText.text = ""; });
            Button(designKeyboard, cancelLabel, new Vector2(-235, -285), new Vector2(430, 60), CloseDesignKeyboard);
            Button(designKeyboard, acceptLabel, new Vector2(235, -285), new Vector2(430, 60), () => { if (!allowEmpty && string.IsNullOrWhiteSpace(keyboardValue)) return; var action = acceptKeyboard; var text = keyboardValue.Trim(); CloseDesignKeyboard(); action(text); }, Green);
            UpdateButtons();
        }
        public void TypeDesignText(string value)
        {
            if (!EditingText) return;
            if (value == null) { if (keyboardValue.Length > 0) keyboardValue = keyboardValue.Substring(0, keyboardValue.Length - 1); }
            else foreach (char c in value) if (!char.IsControl(c) && (keyboardCodeMode || (c != '<' && c != '>')) && keyboardValue.Length < keyboardLimit) keyboardValue += c;
            keyboardText.text = keyboardValue;
        }
        void CloseDesignKeyboard() { if (designKeyboard) designKeyboard.gameObject.SetActive(false); acceptKeyboard = null; UpdateButtons(); }
        void FitText(string value, Vector2 position, Vector2 size, int fontSize, Color color)
        {
            var label = Text(inspector, value, position, size, fontSize, color); label.richText = false;
            label.fontSizeMax = fontSize; label.fontSizeMin = 17; label.enableAutoSizing = true;
        }
        void ShowReview(int page = 0)
        {
            ClearInspector(); var issues = Graph.Validate(); int pages = Graph.nodes.Count + 1; page = Mathf.Clamp(page, 0, pages - 1);
            Text(inspector, "REVISAR / " + (page + 1) + " DE " + pages, new Vector2(0, 355), new Vector2(506, 50), 27, Cyan);
            if (page < Graph.nodes.Count) {
                var node = Graph.nodes[page]; var def = ServiceCatalog.Get(node.kind);
                FitText(node.name, new Vector2(0, 276), new Vector2(506, 80), 30, def.Color);
                string setting = node.kind == ServiceKind.EventBridge ? "Bus personalizado" : def.Settings[Mathf.Clamp(node.setting, 0, def.Settings.Length - 1)];
                var related = issues.Where(s => s.Contains(node.name) || s.Contains(node.id)).ToArray();
                string detail = setting + "\n" + Graph.links.Count(l => l.to == node.id) + " entradas · " + Graph.links.Count(l => l.from == node.id) + " salidas\n\n" + (related.Length > 0 ? string.Join("\n", related) : DesignSemantics.Help(node.kind));
                FitText(detail, new Vector2(0, 75), new Vector2(506, 280), 23, White);
                EditButton(inspector, "Ir al objeto", new Vector2(0, -118), new Vector2(506, 55), () => { ConnectingMode = false; Select(views[node.id]); });
            } else {
                Text(inspector, "RECURSOS AUXILIARES", new Vector2(0, 278), new Vector2(506, 55), 23, White);
                FitText("Objetos visuales ≠ recursos AWS.\n\n" + string.Join("\n\n", DesignSemantics.Auxiliaries(Graph)), new Vector2(0, 50), new Vector2(506, 365), 22, White);
            }
            int current = page;
            EditButton(inspector, "← Anterior", new Vector2(-130, -202), new Vector2(245, 52), () => ShowReview(current - 1));
            EditButton(inspector, "Siguiente →", new Vector2(130, -202), new Vector2(245, 52), () => ShowReview(current + 1));
            EditButton(inspector, "Validar " + (IsCloud ? "con API" : "diseño local"), new Vector2(0, -280), new Vector2(506, 55), ValidateGraph);
            EditButton(inspector, "Volver al diseño", new Vector2(0, -355), new Vector2(506, 55), CloseWorkspace);
            SetStatus(issues.Count == 0 ? "Revisión local válida. Explora objetos y recursos auxiliares antes de desplegar." : issues.Count + " ajustes pendientes · " + issues[0]);
        }
    }
}
