using System.Linq;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    // Hand-first, near-field interface: the wrist menu's actions, head-following reading panels and keyboards
    // that appear at the typing pose. Reading panels and keyboards live outside the personal console because
    // they are placed relative to the person (their head), not to the station.
    public sealed partial class ArchitectureLab
    {
        Transform nearRoot;
        RectTransform controlsPanel;
        TMPro.TMP_Text layoutSetting, wristSetting;
        // Follow slots: code in the middle, inspection to the right of it, live evidence to the left.
        public const float CodeScale = .0004f, ReaderScale = .00042f, EvidenceScale = .0004f;
        Transform NearRoot
        {
            get
            {
                if (nearRoot) return nearRoot;
                nearRoot = new GameObject("Near-field interface").transform; nearRoot.SetParent(world, false);
                return nearRoot;
            }
        }
        RectTransform ControlsPanel => controlsPanel ? controlsPanel
            : controlsPanel = GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "02 · Architecture controls");

        public bool CatalogVisible => catalogPanel && catalogPanel.gameObject.activeSelf;
        public bool ControlsVisible => ControlsPanel && ControlsPanel.gameObject.activeSelf;
        public bool ObjectInspectorVisible => objectInspector && objectInspector.gameObject.activeSelf;
        public bool AssistantPanelVisible => assistantPanel && assistantPanel.gameObject.activeSelf;
        public bool InspectionVisible => workspaceInspector && workspaceInspector.gameObject.activeSelf;
        public bool CodeStudioVisible => codePanel && codePanel.gameObject.activeSelf;
        public bool SettingsVisible => settingsPanel && settingsPanel.gameObject.activeSelf;
        public bool GuideVisible => guidedPanel && guidedPanel.gameObject.activeSelf;
        public bool SelectedIsLambda => selected && selected.Model.kind == ServiceKind.Lambda;

        void Toggle(RectTransform panel)
        {
            if (!panel) return;
            bool show = !panel.gameObject.activeSelf; panel.gameObject.SetActive(show);
            Feedback.Play(show ? LabFeedback.Open : LabFeedback.Close);
        }
        public void ToggleCatalog() => Toggle(catalogPanel);
        public void ToggleControls() => Toggle(ControlsPanel);
        public void ToggleObjectInspector() { Toggle(objectInspector); if (ObjectInspectorVisible) ShowInspector(); }
        public void ToggleAssistantPanel() { if (AssistantPanelVisible) assistantPanel.gameObject.SetActive(false); else OpenAssistant(); }
        public void ToggleGuide() { if (GuideVisible) guidedPanel.gameObject.SetActive(false); else OpenGuidedDemo(); }
        public void ToggleInspection()
        {
            if (!InspectionVisible) { OpenSlots(); return; }
            if (Busy) { SetStatus("Espera a que termine la operación en curso."); return; }
            if (Placing) CancelPlacement();
            CloseWorkspace();
        }
        public void ToggleSelectedLambdaCode()
        {
            if (CodeStudioVisible) { if (!codeBusy) codePanel.gameObject.SetActive(false); return; }
            if (SelectedIsLambda) OpenLambdaEditor(selected.Model.id);
            else SetStatus("Selecciona una Lambda para abrir su código.");
        }
        public void UndoLast() { if (Busy || codeBusy || RoomReadOnly) return; confirming = false; resetting = false; Undo(); }

        void Follow(RectTransform panel, float yaw, int priority, float scale)
        {
            panel.SetParent(NearRoot, true);
            HeadFollow.Attach(panel, this, yaw, priority, scale);
        }
        // Keyboards open at the typing pose in front of the chest, world-locked, keys KeyPitch apart.
        RectTransform KeyboardPanel(string name, Vector2 size, float pitchPixels)
        {
            var keyboard = Focus(Panel(NearRoot, name, Vector3.zero, size));
            var menu = keyboard.GetComponent<LabMenu>(); if (menu) menu.Persist = false;
            var head = Rig && Rig.ViewCamera ? Rig.ViewCamera.transform : Camera.main ? Camera.main.transform : null;
            ComfortLayout.PlaceKeyboard(keyboard, head, Rig && Rig.IsXR, pitchPixels);
            return keyboard;
        }
        // «Controles» settings: panel layout and which wrist wears the menu.
        void BuildComfortSettings(Transform page)
        {
            layoutSetting = Button(page, "", new Vector2(-250, -415), new Vector2(460, 54), () => {
                if (!Space) return;
                Space.SetLayoutChoice((Space.LayoutChoice + 1) % 3); RefreshComfortSettings();
            }).Label;
            wristSetting = Button(page, "", new Vector2(250, -415), new Vector2(460, 54), () => {
                if (!Rig) return;
                Rig.SetWristOnRight(!Rig.WristOnRight); RefreshComfortSettings();
            }).Label;
            RefreshComfortSettings();
        }
        void RefreshComfortSettings()
        {
            if (layoutSetting && Space)
                layoutSetting.text = Space.LayoutChoice == SharedSpace.LayoutNear ? "Paneles: al alcance"
                    : Space.LayoutChoice == SharedSpace.LayoutPanoramic ? "Paneles: panorámicos" : "Paneles: automático";
            if (wristSetting && Rig) wristSetting.text = "Menú de muñeca: " + (Rig.WristOnRight ? "derecha" : "izquierda");
        }
    }
}
