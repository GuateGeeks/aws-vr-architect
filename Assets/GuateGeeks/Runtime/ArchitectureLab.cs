using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    public sealed partial class ArchitectureLab : MonoBehaviour
    {
        public Architecture Graph { get; private set; }
        public bool Busy { get; private set; }
        public bool ConnectingMode { get; private set; }
        public IReadOnlyDictionary<string, NodeView> Views => views;
        public bool SessionReady { get; private set; }
        public bool Deployed { get; private set; }
        public int EventCount { get; private set; }
        public bool SimulateFailure { get; private set; }
        public LabRig Rig { get; private set; }
        public LabFeedback Feedback { get; private set; }
        public LabEnvironmentSettings Environment { get; private set; }
        public bool GridSnap { get; private set; }
        public bool HasConnectionPreview => connectionPreview && connectionPreview.enabled;
        readonly Dictionary<string, NodeView> views = new Dictionary<string, NodeView>();
        readonly List<LinkView> linkViews = new List<LinkView>();
        readonly Stack<Architecture> history = new Stack<Architecture>();
        readonly List<LabTarget> editingButtons = new List<LabTarget>();
        ICloudApi api;
        Transform world, graphRoot, linksRoot;
        RectTransform inspector, objectInspector, workspaceInspector;
        void SetReaderSize(Vector2 size)
        {
            workspaceInspector.sizeDelta = size;
            var handle = workspaceInspector.GetComponent<LabMenu>().Handle as RectTransform;
            if (handle) handle.anchoredPosition = new Vector2(0, size.y / 2 + 23);
        }
        Text statusText, countsText, sessionText, telemetryText;
        Image progress;
        LabTarget connectButton, deployButton, testButton, cancelButton;
        NodeView selected;
        string connectionSource;
        Coroutine operation;
        bool confirming, resetting;
        bool definitionModified;
        int revision;
        LineRenderer connectionPreview;
        RectTransform connectionHintPanel;
        Text connectionMeaning;
        readonly Vector3[] previewPoints = new Vector3[25];
        string SavePath => Path.Combine(Application.persistentDataPath, "aws-day-architecture.json");

        void Start()
        {
            Application.targetFrameRate = 72;
            float savedScale=PlayerPrefs.GetFloat(ComponentScalePreference,1);
            ComponentScale=float.IsNaN(savedScale)||float.IsInfinity(savedScale)?1:Mathf.Clamp(savedScale,.5f,1.25f);
            api = new MockCloudApi();
            Feedback = gameObject.AddComponent<LabFeedback>();
            world = new GameObject("AWS Day · Holographic lab").transform; world.SetParent(transform, false);
            var environment = new GameObject("Virtual room"); environment.transform.SetParent(world, false);
            HoloEnvironment.Build(environment.transform); BuildSharedSpace(); BuildInterface();
            connectionPreview = Line(world,"Pending connection",previewPoints,Orange,.022f); connectionPreview.sharedMaterial=Beam(Orange); connectionPreview.textureMode=LineTextureMode.Stretch; connectionPreview.enabled=false;
            connectionHintPanel = Panel(world, "Connection explanation", Vector3.zero, new Vector2(550, 115), movable: false);
            connectionMeaning = Text(connectionHintPanel, "", Vector2.zero, new Vector2(520, 105), 20, White, TextAnchor.MiddleCenter);
            connectionHintPanel.gameObject.SetActive(false);
            Rig = gameObject.AddComponent<LabRig>(); Rig.Initialize(this);
            Environment = gameObject.AddComponent<LabEnvironmentSettings>(); Environment.Initialize(this, environment);
            BuildCloudPanel(); BuildUnifiedSettings(); BuildContextRing(); BuildCollaboration();
            SetGraph(Architecture.Preset(0));
            ShowInspector();
            operation = StartCoroutine(StartConnection());
            Debug.Log("AWS Architect Lab ready: holographic environment, interface, rig and " + views.Count + " resources created. API: local mock.");
        }
        void OnDestroy() { NetworkRoom?.Dispose(); roomBroker?.Disconnect(); StopAssistant(); StopLiveInspection();AbortCodeOperation(); StopAllCoroutines(); api?.Disconnect(); Release(); }

        LabTarget EditButton(Transform parent, string label, Vector2 pos, Vector2 size, Action action, Color? color = null)
        {
            var b = Button(parent, label, pos, size, () => { if (!Busy && !codeBusy && !RoomReadOnly) { confirming = false; resetting = false; action(); } }, color);
            editingButtons.Add(b); return b;
        }
        void BuildInterface()
        {
            // Event identity: the AWS Community Day Guatemala logo, projected with a soft backlight and registration brackets.
            var header = Panel(world, "Lab identity", new Vector3(0, 3.36f, 4.0f), new Vector2(1600, 850), background: false);
            header.localScale = Vector3.one * .0014f;
            EventBranding.Build(header, new Vector2(1600, 850));
            Space.RegisterIdentity(header);
            // Slim status bar below the reactor hub: one message line plus a quiet counts line.
            var banner = Panel(PersonalRoot, "Mission status", new Vector3(0, 2.3f, 3.95f), new Vector2(1100, 86));
            banner.localScale = Vector3.one * .0017f;
            Block(banner, new Vector2(-548, 0), new Vector2(4, 86), Cyan);
            statusText = Text(banner, "Preparando sesión local…", new Vector2(0, 10), new Vector2(1050, 52), 24, White, TextAnchor.MiddleCenter);
            statusText.enableAutoSizing = true; statusText.fontSizeMin = 15; statusText.fontSizeMax = 24;
            countsText = Text(banner, "", new Vector2(0, -26), new Vector2(1050, 22), 15, Muted, TextAnchor.MiddleCenter);
            countsText.enableAutoSizing = true; countsText.fontSizeMin = 11; countsText.fontSizeMax = 15;

            var catalog = catalogPanel = Panel(PersonalRoot, "01 · Service catalog", new Vector3(-2.03f, 1.83f, 2.25f), new Vector2(540, 865), -29);
            Text(catalog, "01  /  COMPONENTES", new Vector2(0, 385), new Vector2(476, 45), 25, Cyan);
            Text(catalog, "Agrega servicios a tu espacio", new Vector2(0, 339), new Vector2(476, 40), 21, Muted);
            Block(catalog, new Vector2(0, 303), new Vector2(476, 1), LineColor);
            for (int i = 0; i < ServiceCatalog.All.Length; i++)
            {
                var def = ServiceCatalog.All[i];
                var serviceButton = EditButton(catalog, def.Name + "  <size=15> / " + def.Category + "</size>", new Vector2(0, 253 - i * 78), new Vector2(476, 64), () => StartPlacement(def.Kind), def.Color);
                AwsServiceIcons.Add(serviceButton.transform, def.Kind, new Vector2(-200, 0), 48);
                serviceButton.Label.rectTransform.anchoredPosition = new Vector2(34, 0);
                serviceButton.Label.rectTransform.sizeDelta = new Vector2(376, 60);
                serviceButton.Label.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
            }
            Ghost(EditButton(catalog, "Plegar catálogo", new Vector2(0, -332), new Vector2(476, 58), () => { catalogPanel.gameObject.SetActive(false); Feedback.Play(LabFeedback.Close); }));
            modeText = Text(catalog, "100% LOCAL  ·  SIN COSTOS AWS", new Vector2(0, -401), new Vector2(476, 34), 17, Green);

            objectInspector = Panel(PersonalRoot, "03 · Inspector", new Vector3(2.03f, 1.83f, 2.25f), new Vector2(570, 865), 29);
            objectInspector.GetComponent<LabMenu>().Moved = () => inspectorPinned = true;
            workspaceInspector = inspector = Panel(PersonalRoot, "Workspace reader", new Vector3(0, 2.05f, .8f), new Vector2(570, 865));
            workspaceInspector.localScale = Vector3.one * .0014f;
            workspaceInspector.GetComponent<HoloPanelGraphic>().color = new Color(.035f, .085f, .12f, 1);
            workspaceInspector.gameObject.SetActive(false);
            var dock = Panel(PersonalRoot, "02 · Architecture controls", new Vector3(0, 1.16f, 1.2f), new Vector2(1250, 435));
            dock.localScale = Vector3.one * .0017f;
            dock.localRotation = Quaternion.Euler(25, 0, 0);
            Text(dock, "02  /  MESA DE ARQUITECTURA", new Vector2(-300, 127), new Vector2(590, 32), 19, Cyan);
            // Team strip: who stands at each station and who is editing (shared room only).
            teamText = Text(dock, "", new Vector2(0, 178), new Vector2(1190, 30), 17, White, TextAnchor.MiddleCenter);
            teamText.enableAutoSizing = true; teamText.fontSizeMin = 11; teamText.fontSizeMax = 17; teamText.gameObject.SetActive(false);
            sessionText = Text(dock, "CONECTANDO…", new Vector2(390, 127), new Vector2(440, 32), 17, Green, TextAnchor.MiddleRight);
            string[] presets = { "API serverless", "Eventos + cola", "Procesar archivos" };
            // Hierarchy: templates and file/tool rows are quiet ghost actions; the build flow is the lit primary row.
            for (int i = 0; i < 3; i++) { int p = i; Ghost(EditButton(dock, presets[i], new Vector2((i - 1) * 400, 77), new Vector2(382, 48), () => LoadPreset(p))); }
            EditButton(dock, "Crear", new Vector2(-480, 10), new Vector2(223, 63), () => catalogPanel.gameObject.SetActive(true));
            EditButton(dock, "Definir", new Vector2(-240, 10), new Vector2(223, 63), ShowInspector);
            connectButton = EditButton(dock, "Conectar nodos", new Vector2(0, 10), new Vector2(223, 63), ToggleConnect);
            EditButton(dock, "Revisar diseño", new Vector2(240, 10), new Vector2(223, 63), () => ShowReview());
            deployButton = EditButton(dock, "Desplegar demo  →", new Vector2(480, 10), new Vector2(223, 63), RequestDeployment, Orange);
            string[] labels = { "Deshacer", "Guardar", "Cargar", "Limpiar", "Guía" };
            Action[] acts = { Undo, Save, Load, ConfirmReset, OpenGuidedDemo };
            for (int i = 0; i < labels.Length; i++) Ghost(EditButton(dock, labels[i], new Vector2((i - 2) * 240, -65), new Vector2(223, 48), acts[i]));
            BuildStudioTools(dock);
            apiText = Text(dock, "MOCK API  /  Ningún recurso se crea en AWS", new Vector2(0, -207), new Vector2(1190, 30), 17, Muted, TextAnchor.MiddleCenter);
        }

        IEnumerator ConnectSession()
        {
            Busy = true; UpdateButtons();
            sessionText.text = IsCloud ? "CONECTANDO AWS…" : "CONECTANDO DEMO…";
            yield return api.Connect((ok, message) => {
                SessionReady = ok; sessionText.text = ok ? (IsCloud ? "●  AWS · SLOT " + Cloud.Slot : "●  SESIÓN DEMO") : "SIN SESIÓN";
                if (ok && IsCloud) { Graph.region = Cloud.Region; Changed(); }
                if (!ok && IsCloud) { profile.autoConnect = false; SaveProfileSafely(); }
                SetStatus(message);
            });
            if (IsCloud) yield return FinishRememberedConnection();
            Busy = false; operation = null; RefreshCloudLabels(); ShowInspector(); UpdateButtons();
            if(!Application.isEditor && !PlayerPrefs.HasKey("GuateGeeks.Guide.Seen.v1")) {OpenGuidedDemo();PlayerPrefs.SetInt("GuateGeeks.Guide.Seen.v1",1);PlayerPrefs.Save();}
        }
        public void SetStatus(string text) { statusText.text = text; }
        void UpdateCounts() { countsText.text = FlowPreviewActive ? "PREVISUALIZACIÓN DEL DISEÑO · NO ES TRÁFICO AWS OBSERVADO" : Graph.nodes.Count + " OBJETOS   /   " + Graph.links.Count + " CONEXIONES   /   " + Graph.region + (IsCloud ? "   /   AWS · SLOT " + Cloud.Slot : "   /   SIMULACIÓN") + (definitionModified ? "   /   DISEÑO MODIFICADO" : ""); }
        void Remember()=>RememberSnapshot(Graph.Copy(),ComponentScale);
        void Changed()
        {
            StopFlowPreview();
            if (Deployed) definitionModified = true;
            revision++; Deployed = false; EventCount = 0; Graph.ResetStates(); confirming = false;
            foreach (var link in linkViews) link.Flowing = false;
            UpdateCounts(); UpdateButtons(); RefreshGlobalSettings();
        }
        public void SetGraph(Architecture graph)
        {
            CancelPlacement(); draftId = null;
            bool preserveDeployment = Deployed && Graph != null && DesignSemantics.Definition(Graph) == DesignSemantics.Definition(graph);
            if (graphRoot) { graphRoot.gameObject.SetActive(false); Destroy(graphRoot.gameObject); }
            graphRoot = new GameObject("Editable architecture").transform; graphRoot.SetParent(world, false);
            Graph = graph; if (IsCloud && !string.IsNullOrEmpty(Cloud.Region)) Graph.region = Cloud.Region;
            Graph.ResetStates(); views.Clear(); linkViews.Clear();
            selected = null; connectionSource = null; ConnectingMode = false;
            HideConnectionPreview();
            foreach (var node in Graph.nodes) CreateView(node);
            RebuildLinks(); Changed();
            if (preserveDeployment) { Deployed = true; definitionModified = false; foreach (var n in Graph.nodes) n.state = ResourceState.Ready; UpdateCounts(); }
            ShowInspector();
        }
        void CreateView(ResourceNode node)
        {
            var go = new GameObject(node.name); go.transform.SetParent(graphRoot, false);
            var view = go.AddComponent<NodeView>(); view.Initialize(node, this); go.transform.localScale=Vector3.one*EffectiveScale(node); views.Add(node.id, view);
        }
        void RebuildLinks()
        {
            if (linksRoot) { linksRoot.gameObject.SetActive(false); Destroy(linksRoot.gameObject); }
            linksRoot = new GameObject("Directed links").transform; linksRoot.SetParent(graphRoot, false); linkViews.Clear();
            foreach (var edge in Graph.links)
            {
                var go = new GameObject("Link"); go.transform.SetParent(linksRoot, false);
                var view = go.AddComponent<LinkView>(); view.Initialize(views[edge.from], views[edge.to], this); linkViews.Add(view);
            }
        }
        Vector3 NextResourcePosition()
        {
            for(int i=0;i<12;i++) {
                var candidate=TablePosition(i);
                if(Graph.nodes.All(n=>Vector3.Distance(n.position,candidate)>.5f*ComponentScale))return candidate;
            }
            return new Vector3(0,1.4f,2.1f);
        }
        public void AddResource(ServiceKind kind)
        {
            if (Busy || RoomReadOnly) return;
            if (Graph.nodes.Count >= Architecture.MaxNodes) { SetStatus("Mesa llena: elimina un recurso para agregar otro. Máximo 12."); return; }
            Remember();
            Vector3 spot = NextResourcePosition();
            var node = Graph.Add(kind, spot); node.setting=Mathf.Clamp(PlayerPrefs.GetInt("GuateGeeks.ComponentDefault."+(int)kind,0),0,DesignSemantics.OptionCount(kind)-1); CreateView(node); Changed(); selected = views[node.id]; RefreshSelection(); ShowInspector();
            SetStatus(node.name + " agregado. Usa «Conectar nodos» para incorporarlo al flujo.");
        }
        public void Select(NodeView view)
        {
            if (Busy || Placing || EditingText || ConfiguringConnection) return;
            confirming = false; if (selected != view) draftId = null; selected = view; PositionInspector();
            if (ConnectingMode)
            {
                if (connectionSource == null) { connectionSource = view.Model.id; SetStatus("Origen: " + view.Model.name + ". Selecciona el destino."); }
                else
                {
                    var before = Graph.Copy();
                    if (Graph.Connect(connectionSource, view.Model.id, out string message))
                    { history.Push(before); RebuildLinks(); Changed(); SetStatus("Conectado: " + message); connectionSource = null; }
                    else { SetStatus(message); Feedback.Play(2); }
                }
            }
            else SetStatus(view.Model.name + " seleccionado. Cambia sus opciones en el panel derecho.");
            RefreshSelection(); ShowInspector();
        }
        void RefreshSelection()
        {
            foreach (var v in views.Values)
            {
                v.SetSelected(v == selected || v.Model.id == connectionSource);
                int hint = !ConnectingMode || connectionSource == null ? 0 : v.Model.id == connectionSource ? 1 : Graph.CanConnect(connectionSource,v.Model.id,out _) ? 2 : 3;
                v.SetConnectionHint(hint);
            }
            foreach (var link in linkViews) link.SetHighlighted(selected && (link.FromId == selected.Model.id || link.ToId == selected.Model.id));
            HideConnectionPreview(); ClaimSelection();
        }
        public void HideConnectionPreview() { if (connectionPreview) connectionPreview.enabled = false; if (connectionHintPanel) connectionHintPanel.gameObject.SetActive(false); }
        public void PreviewConnection(NodeView destination, Vector3 aim)
        {
            bool active=ConnectingMode && connectionSource!=null && views.ContainsKey(connectionSource) && !Busy;
            connectionPreview.enabled=active; connectionHintPanel.gameObject.SetActive(active); if(!active) return;
            Vector3 a=views[connectionSource].transform.position;
            Vector3 b=destination?destination.transform.position:ClampWorkspace(aim);
            string reason = "Elige una entrada compatible.";
            bool valid=destination && Graph.CanConnect(connectionSource,destination.Model.id,out reason);
            connectionMeaning.text = valid ? DesignSemantics.Operation(views[connectionSource].Model.kind, destination.Model.kind) + "\n" + destination.Model.name : reason;
            connectionMeaning.richText = false;
            connectionHintPanel.position = b + new Vector3(0, .65f, -.12f);
            if (Rig && Rig.ViewCamera) connectionHintPanel.rotation = Quaternion.LookRotation(connectionHintPanel.position - Rig.ViewCamera.transform.position);
            connectionPreview.sharedMaterial=Beam(valid?Green:destination?Alert:Orange);
            for(int i=0;i<previewPoints.Length;i++) {float t=(float)i/(previewPoints.Length-1); previewPoints[i]=world.InverseTransformPoint(Vector3.Lerp(a,b,t)+Vector3.up*Mathf.Sin(t*Mathf.PI)*.15f);}
            connectionPreview.SetPositions(previewPoints);
        }
        public void ToggleGridSnap() { GridSnap=!GridSnap; SetStatus(GridSnap?"Ajuste a cuadrícula: 10 cm al soltar.":"Movimiento libre activado."); }
        public void ToggleConnect()
        {
            if (Placing || EditingText || ConfiguringConnection) return;
            ConnectingMode = !ConnectingMode; connectionSource = null; RefreshSelection(); UpdateButtons();
            SetStatus(ConnectingMode ? "Modo conexión: selecciona primero el origen y después el destino. B / Esc para salir." : "Modo selección. Puedes tomar y mover los recursos.");
        }
        public void CancelInteraction()
        {
            if(voicePreviewActive){CancelPendingVoiceAction();return;}
            if (credentialBusy) return;
            if (EditingText) { CloseDesignKeyboard(); return; }
            if (Placing) { CancelPlacement(); CloseWorkspace(); SetStatus("Colocación cancelada."); return; }
            draftId = null; StopFlowPreview();
            if (ConfiguringConnection) { CloseConnectionForm(); return; }
            if (Busy) { CancelDeployment(); return; }
            ConnectingMode = false; connectionSource = null; confirming = false; resetting = false;
            RefreshSelection(); UpdateButtons(); CloseWorkspace(); SetStatus("Acción cancelada. Selecciona un recurso para continuar.");
        }
        public bool BeginGrab(NodeView node)
        {
            if (Busy || RoomReadOnly || ConfiguringConnection || EditingText || Placing || node.Grabbed || NetworkRoom != null && views.Values.Any(v => v.Grabbed)) return false;
            if (RejectLocked(node)) return false;
            if (!Collab.Claim(node.Model.id)) { SetStatus("Reservando objeto… mantén el agarre hasta la confirmación."); return false; }
            confirming = false; Remember(); node.Grabbed = true; selected = node; RefreshSelection(); ShowInspector(); return true;
        }
        public void EndGrab(NodeView node)
        {
            if (!node) return;
            if (GridSnap) { var p=node.transform.localPosition; node.transform.localPosition=ClampWorkspace(new Vector3(Mathf.Round(p.x*10)/10,Mathf.Round(p.y*10)/10,Mathf.Round(p.z*10)/10)); }
            node.Grabbed = false; node.Model.position = node.transform.localPosition;
            if (NetworkRoom != null) roomPendingRelease = node.Model.id;
            else Collab.Claim(null);
            // Layout edits preserve a successful deployment because they do not alter the architecture.
            UpdateCounts();
        }
        public static Vector3 ClampWorkspace(Vector3 position) => new Vector3(Mathf.Clamp(position.x, -1.6f, 1.6f), Mathf.Clamp(position.y, 1.02f, 2.1f), Mathf.Clamp(position.z, 1.65f, 3.65f));
        // The projection table sits at (0, 0.74, 2.65) in lab space; pulses travel across its surface.
        void TablePulse(Color color, float delay = 0) => HoloPulse.Spawn(world, new Vector3(0, .755f, 2.65f), .35f, 1.85f, color, .04f, delay);
        void LoadPreset(int index) { roomGlobal = NetworkRoom != null; Remember(); SetGraph(Architecture.Preset(index)); TablePulse(Cyan); SetStatus("Plantilla cargada. Puedes deshacer para recuperar el diseño anterior."); }
        void Undo() { if (UndoRoomOperation()) return; if (history.Count == 0) { SetStatus("Todavía no hay cambios para deshacer."); return; } var previous=history.Pop();if(historyScales.TryGetValue(previous,out float scale))RestoreViewScale(scale);historyScales.Remove(previous);SetGraph(previous);SetStatus("Cambio deshecho."); }
        void ValidateGraph()
        {
            if (IsCloud) { if (!Busy) operation = StartCoroutine(ValidateCloud()); return; }
            var issues = Graph.Validate(); Feedback.Play(issues.Count==0?1:2); SetStatus(issues.Count == 0 ? "Diseño válido. Tu arquitectura está lista para un despliegue simulado." : issues[0] + (issues.Count > 1 ? " (" + issues.Count + " ajustes pendientes)" : ""));
        }
        void ClearInspector()
        {
            inspector.gameObject.SetActive(true);
            if (inspector == workspaceInspector)
            {
                if (!drawingLiveReader) StopLiveInspection();
                readerBeforeSettings = false;
                if (settingsPanel) settingsPanel.gameObject.SetActive(false);
                SetReaderSize(new Vector2(570, 865));
            }
            var menu = inspector.GetComponent<LabMenu>();
            foreach (Transform child in inspector) { if ((menu && child == menu.Handle) || child.name == EventBranding.WatermarkName) continue; child.gameObject.SetActive(false); Destroy(child.gameObject); }
            // Destroyed inspector buttons are removed so session updates only touch live controls.
            editingButtons.RemoveAll(b => !b || b.transform.IsChildOf(inspector));
        }
        void ShowInspector() => InObjectInspector(DrawObjectInspector);
        void InObjectInspector(Action draw)
        {
            var previous = inspector; inspector = objectInspector;
            try { draw(); } finally { inspector = previous; }
        }
        void CloseWorkspace() { StopLiveInspection(); workspaceInspector.gameObject.SetActive(false); ShowInspector(); }
        void DrawObjectInspector()
        {
            ClearInspector();
            Text(inspector, "03  /  " + (HasPendingDefinition ? "EDICIÓN SIN APLICAR" : selected ? "DEFINIR OBJETO" : "TU MISIÓN"), new Vector2(0, 385), new Vector2(506, 45), 25, Cyan);
            if (selected)
            {
                DrawDefinition();
            }
            else
            {
                Text(inspector, "De idea a arquitectura", new Vector2(0, 309), new Vector2(506, 75), 33, White);
                Text(inspector, "01   AGREGA\nElige servicios en el catálogo.\n\n02   CONECTA\nOrigen → destino en tu mesa.\n\n03   DESPLIEGA\n" + (IsCloud ? "Valida y crea recursos en AWS." : "Valida y observa la simulación."), new Vector2(0, 91), new Vector2(506, 340), 24, White);
            }
            Block(inspector, new Vector2(0, -120), new Vector2(506, 1), LineColor);
            if(selected && selected.Model.kind==ServiceKind.Lambda)EditButton(inspector,"Código Lambda",new Vector2(-130,-173),new Vector2(245,54),()=>OpenLambdaEditor(selected.Model.id));
            if(IsCloud)EditButton(inspector,"Diagnóstico vivo",new Vector2(selected && selected.Model.kind==ServiceKind.Lambda?130:0,-173),new Vector2(selected && selected.Model.kind==ServiceKind.Lambda?245:506,54),OpenSelectedDiagnostics);
            else if(!selected || selected.Model.kind!=ServiceKind.Lambda)FitText("Selecciona un objeto para ver su definición.",new Vector2(0,-177),new Vector2(506,75),22,Muted);
            EditButton(inspector, IsCloud ? "Inspeccionar este objeto en AWS" : "Ajustes del laboratorio", new Vector2(0, -250), new Vector2(506, 54), () => { if (IsCloud) InspectSelectedCloudObject(); else ToggleSettings(); });
            testButton = Button(inspector, "Enviar evento de prueba  →", new Vector2(0, -310), new Vector2(506, 63), TestFlow, Green);
            testButton.SetAvailable(Deployed && !Busy);
            telemetryText = Text(inspector, EventCount == 0 ? "Despliega para enviar eventos" : IsCloud ? EventCount + " eventos aceptados · consulta CloudWatch" : EventCount + " eventos  ·  142 ms  ·  200 OK (demo)", new Vector2(0, -380), new Vector2(506, 60), 19, Muted);
            UpdateButtons();
        }
        void ConfirmReset()
        {
            resetting = true; ClearInspector();
            Text(inspector, "¿Limpiar la mesa?", new Vector2(0, 220), new Vector2(506, 80), 37, White);
            Text(inspector, "Empezarás con un espacio vacío. Puedes recuperar el diseño con Deshacer.", new Vector2(0, 100), new Vector2(506, 130), 25, Muted);
            Button(inspector, "Sí, limpiar", new Vector2(0, -40), new Vector2(506, 70), () => { if (resetting && !Busy) { roomGlobal=NetworkRoom!=null; Remember(); SetGraph(new Architecture()); resetting = false; SetStatus("Mesa vacía. Agrega tu primer servicio."); } }, Orange);
            Button(inspector, "Volver", new Vector2(0, -140), new Vector2(506, 62), CancelInteraction);
        }
        void RequestDeployment()
        {
            if(NetworkRoom != null && (RoomReadOnly || !NetworkRoom.IsFacilitator)) {SetStatus("Solo el facilitador despliega la revisión confirmada de la sala.");return;}
            if (HasPendingDefinition) { ShowInspector(); SetStatus("Aplica o cancela la edición pendiente antes de desplegar."); return; }
            StopFlowPreview();
            if (!SessionReady) { SetStatus("La sesión todavía no está lista."); return; }
            var issues = IsCloud ? AwsCloudApi.ValidateLocally(Graph, Cloud.Region) : Graph.Validate(); if (issues.Count > 0) { SetStatus(issues[0]); return; }
            if (views.Values.Any(v => v.Grabbed)) { SetStatus("Suelta los recursos antes de desplegar."); return; }
            ConnectingMode = false; connectionSource = null; RefreshSelection(); UpdateButtons(); confirming = true;
            ClearInspector();
            Text(inspector, "LISTO PARA CONSTRUIR", new Vector2(0, 330), new Vector2(506, 60), 24, Cyan);
            Text(inspector, "Revisa tu diseño", new Vector2(0, 248), new Vector2(506, 75), 37, White);
            Text(inspector, Graph.nodes.Count + " objetos visuales\n" + Graph.links.Count + " conexiones\nRegión: " + Graph.region + "\n\n" + (IsCloud ? "AWS REAL · SLOT " + Cloud.Slot : SimulateFailure ? "Fallo de demostración ACTIVADO" : "Simulación de creación completa"), new Vector2(0, 75), new Vector2(506, 245), 26, White);
            Text(inspector, IsCloud ? "Crea recursos con costo real.\nEl operador debe eliminarlos al terminar." : "Todos los resultados son ficticios.\nNo se crean recursos reales.", new Vector2(0, -110), new Vector2(506, 90), 22, Muted);
            if (IsCloud) Text(inspector, Cloud.Endpoint, new Vector2(0, -173), new Vector2(506, 40), 16, Cyan);
            Button(inspector, IsCloud ? "Confirmar creación AWS  →" : "Confirmar simulación  →", new Vector2(0, -231), new Vector2(506, 72), BeginDeployment, Orange);
            Button(inspector, "Volver al diseño", new Vector2(0, -328), new Vector2(506, 62), CancelInteraction);
            SetStatus(IsCloud ? "Revisa el diseño, la región y el slot antes de crear recursos reales." : "Revisa el resumen en el panel derecho y confirma la simulación.");
        }
        public void BeginDeployment()
        {
            if(NetworkRoom != null && (RoomReadOnly || !NetworkRoom.IsFacilitator))return;
            if (!confirming || Busy || !SessionReady) return;
            if (!SaveCloudCheckpoint(true)) return;
            confirming = false; Busy = true; Deployed = false; EventCount = 0; Graph.ResetStates(); UpdateButtons();
            ClearInspector();
            Text(inspector, IsCloud ? "DESPLIEGUE AWS · SLOT " + Cloud.Slot : "DESPLIEGUE SIMULADO", new Vector2(0, 345), new Vector2(506, 60), 25, Orange);
            Text(inspector, "Construyendo\ntu arquitectura", new Vector2(0, 210), new Vector2(506, 145), 39, White);
            Text(inspector, "Observa el estado de creación\nde cada objeto de tu diseño.", new Vector2(0, 70), new Vector2(506, 100), 24, Muted);
            Block(inspector, new Vector2(0, -40), new Vector2(500, 12), LineColor);
            progress = Block(inspector, new Vector2(-250, -40), new Vector2(0, 12), Orange);
            progress.rectTransform.pivot = new Vector2(0, .5f);
            cancelButton = Button(inspector, IsCloud ? "Dejar de observar" : "Cancelar simulación", new Vector2(0, -180), new Vector2(506, 65), CancelDeployment);
            Text(inspector, IsCloud ? "AWS continúa aunque cierres el visor" : "MOCK API  /  MODO DEMOSTRACIÓN", new Vector2(0, -340), new Vector2(506, 55), 18, Green);
            operation = StartCoroutine(DeployRoutine(Graph.Copy()));
        }
        IEnumerator DeployRoutine(Architecture snapshot)
        {
            bool succeeded = false;
            yield return api.Deploy(snapshot, SimulateFailure, ev =>
            {
                if (ev.ResourceId != null && Graph.Find(ev.ResourceId) != null) Graph.Find(ev.ResourceId).state = ev.State;
                if (progress) progress.rectTransform.sizeDelta = new Vector2(500 * ev.Progress, 12);
                SetStatus(ev.Message); if (ev.Finished) succeeded = ev.Success;
                if (IsCloud && Cloud.StackId != checkpoint?.stackId) SaveCloudCheckpoint(false);
            });
            Busy = false; Deployed = succeeded; operation = null;
            if (succeeded) { definitionModified = false; TablePulse(Green); TablePulse(Green, .18f); }
            SyncCloudSession();
            Feedback.Play(succeeded?1:2);
            foreach (var link in linkViews) link.Flowing = false;
            CloseWorkspace(); UpdateButtons();
        }
        public void CancelDeployment()
        {
            if (slotOperation) { StopSlotOperation(); return; }
            if (credentialBusy || !Busy || (!SessionReady && !IsCloud)) return;
            enteredPassword = "";
            if (operation != null) StopCoroutine(operation); operation = null;
            Cloud?.StopWatching();
            Busy = false; Deployed = false; Graph.ResetStates(); foreach (var link in linkViews) link.Flowing = false;
            SyncCloudSession();
            CloseWorkspace(); UpdateButtons(); SetStatus(IsCloud ? "Observación detenida. AWS continúa. Guarda el diseño y usa «Desplegar / retomar AWS» para consultar el mismo slot." : "Simulación cancelada. El diseño permanece en la mesa.");
        }
        public void TestFlow()
        {
            if (!Deployed || Busy) return;
            StopFlowPreview();
            if (IsCloud) Cloud.EventResourceId = selected ? selected.Model.id : null;
            Busy = true; UpdateButtons(); SetStatus("Enviando evento de prueba a tu arquitectura…");
            operation = StartCoroutine(TestRoutine(revision));
        }
        IEnumerator TestRoutine(int atRevision)
        {
            yield return api.Invoke((ok, message) => { if (atRevision == revision) { if (ok) EventCount++; SetStatus(message); } });
            Busy = false; operation = null; SyncCloudSession(); ShowInspector(); UpdateButtons();
        }
        void UpdateButtons()
        {
            foreach (var b in editingButtons) if (b) b.SetAvailable(!Busy && !ConfiguringConnection && !EditingText && !Placing);
            if (connectButton) connectButton.Label.text = ConnectingMode ? "Conectando · Salir" : "Conectar nodos";
            if (deployButton) deployButton.SetAvailable(!Busy && !ConfiguringConnection && !EditingText && !Placing && SessionReady);
            if (testButton) testButton.SetAvailable(!Busy && !ConfiguringConnection && !EditingText && !Placing && Deployed && SessionReady);
            foreach (var button in cloudButtons) if (button) button.SetAvailable(!Busy && !EditingText && !Placing);
            if (stopCloudButton) stopCloudButton.SetAvailable(Busy && IsCloud && !credentialBusy);
        }
        void Save()
        {
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(Graph, true)); SetStatus("Diseño guardado en este dispositivo. «Cargar» lo restaura."); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { SetStatus("No se pudo guardar el diseño en este dispositivo."); Debug.LogWarning(e.Message); }
        }
        void Load()
        {
            try
            {
                if (!File.Exists(SavePath)) { SetStatus("No hay un diseño guardado en este dispositivo."); return; }
                string json = File.ReadAllText(SavePath);
                var next = JsonUtility.FromJson<Architecture>(json);
                // Incomplete but editable graphs are allowed; structural corruption is not.
                if (!DesignLibrary.Readable(next)) throw new FormatException();
                foreach (var node in next.nodes) node.position = ClampWorkspace(node.position);
                roomGlobal=NetworkRoom!=null; Remember(); SetGraph(next); SetStatus("Diseño recuperado. Valida y despliega cuando estés listo.");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is FormatException)
            { SetStatus("No se pudo cargar el archivo. Tu diseño actual sigue intacto."); }
        }
    }
}

