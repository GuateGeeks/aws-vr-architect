using System.Linq;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    public sealed partial class ArchitectureLab
    {
        RectTransform settingsPanel;
        bool readerBeforeSettings;
        readonly GameObject[] settingsPages = new GameObject[3];
        TMPro.TMP_Text regionSetting, simulationSetting;
        void BuildUnifiedSettings()
        {
            settingsPanel = Panel(world, "Settings console", new Vector3(0, 2.05f, .8f), new Vector2(1120, 1100));
            settingsPanel.localScale = Vector3.one * .0014f;
            Text(settingsPanel, "AJUSTES / TU ESPACIO", new Vector2(0, 492), new Vector2(1030, 60), 30, Cyan);
            settingsPanel.GetComponent<HoloPanelGraphic>().color = new Color(.035f, .085f, .12f, 1);
            string[] tabs = { "Conexión AWS", "Espacio", "Controles" };
            for (int i = 0; i < 3; i++)
            {
                int page = i;
                settingsPages[i] = new GameObject(tabs[i], typeof(RectTransform));
                settingsPages[i].transform.SetParent(settingsPanel, false);
                Button(settingsPanel, tabs[i], new Vector2((i - 1) * 350, 413), new Vector2(330, 62), () => ShowSettingsPage(page));
            }
            DockSettingsContent("05 · Cloud connection", 0, Vector2.zero, 1.35f);
            DockSettingsContent("04 · Environment settings", 1, new Vector2(0, 210), .92f);
            DockSettingsContent("Comfort controls", 1, new Vector2(0, -40), .76f);
            DockSettingsContent("Controls reference", 2, new Vector2(0, 205), .74f);
            DockSettingsTransform(connectionForm, settingsPanel, new Vector2(0, -40), .9f);
            Text(settingsPages[1].transform, "Elige fondo, passthrough y comodidad en este panel.\nLa opción de fondo se guarda en este visor.", new Vector2(0, -220), new Vector2(980, 110), 25, White);
            componentSizeText=Text(settingsPages[1].transform,"",new Vector2(0,-295),new Vector2(980,35),22,Cyan);
            float[] sizes={.5f,.75f,1f,1.25f};string[] sizeNames={"Compacto 50%","Medio 75%","Normal 100%","Grande 125%"};
            for(int i=0;i<sizes.Length;i++){float scale=sizes[i];Button(settingsPages[1].transform,sizeNames[i],new Vector2((i-1.5f)*250,-350),new Vector2(235,58),()=>SetComponentScale(scale));}
            Button(settingsPages[1].transform,"Distribuir en la mesa",new Vector2(0,-422),new Vector2(980,54),ArrangeDesign);
            RefreshComponentSize();
            Text(settingsPages[2].transform, "MANOS\nApunta y junta pulgar e índice para seleccionar.\nMantén la pinza sobre un objeto o asa para moverlo.\nSuelta para dejarlo. Usa los botones para cancelar.\n\nCONTROLES\nGatillo: seleccionar · Grip: mover · Stick: distancia\nB / Y: cancelar · A + X: recuperar paneles", new Vector2(0, -90), new Vector2(970, 380), 26, White);
            Button(settingsPages[2].transform, "Restaurar paneles", new Vector2(-250, -343), new Vector2(460, 62), Rig.ResetMenus);
            Button(settingsPages[2].transform, "Centrar vista", new Vector2(250, -343), new Vector2(460, 62), Rig.Recenter);
            Button(settingsPanel, "Cerrar ajustes", new Vector2(0, -491), new Vector2(1030, 62), () => { if (!ConfiguringConnection && !credentialBusy) SetSettingsVisible(false); });
            regionSetting = EditButton(settingsPages[0].transform, "Región del diseño", new Vector2(-250, -395), new Vector2(460, 58), () => {
                if (IsCloud) { SetStatus("La región la determina la API."); return; }
                Remember(); Graph.region = Graph.region == "us-east-1" ? "us-west-2" : "us-east-1"; Changed(); RefreshGlobalSettings();
            }).Label;
            simulationSetting = EditButton(settingsPages[0].transform, "Simular fallo: NO", new Vector2(250, -395), new Vector2(460, 58), () => {
                if (IsCloud) { SetStatus("AWS usa resultados reales."); return; }
                SimulateFailure = !SimulateFailure; RefreshGlobalSettings();
            }).Label;
            ShowSettingsPage(0); settingsPanel.gameObject.SetActive(false);
        }
        void DockSettingsContent(string name, int page, Vector2 position, float scale)
        {
            var panel = GetComponentsInChildren<RectTransform>(true).First(t => t.name == name);
            DockSettingsTransform(panel, settingsPages[page].transform, position, scale);
        }
        void DockSettingsTransform(RectTransform panel, Transform parent, Vector2 position, float scale)
        {
            var menu = panel.GetComponent<LabMenu>();
            if (menu) { menu.enabled = false; if (menu.Handle) { menu.Handle.gameObject.SetActive(false); Destroy(menu.Handle.gameObject); } Destroy(menu); }
            var surface = panel.GetComponent<HoloPanelGraphic>(); if (surface) surface.enabled = false;
            panel.SetParent(parent, false); panel.localPosition = position; panel.localRotation = Quaternion.identity; panel.localScale = Vector3.one * scale;
        }
        void RefreshGlobalSettings()
        {
            if (regionSetting) regionSetting.text = "Región: " + (Graph?.region ?? "us-east-1") + (IsCloud ? " (API)" : "");
            if (simulationSetting) simulationSetting.text = IsCloud ? "AWS · resultados reales" : "Simular fallo: " + (SimulateFailure ? "SÍ" : "NO");
        }
        void ShowSettingsPage(int page)
        {
            if (ConfiguringConnection || credentialBusy) return;

            for (int i = 0; i < settingsPages.Length; i++) settingsPages[i].SetActive(i == page);
        }
        void ToggleUnifiedSettings()
        {
            if (!settingsPanel || ConfiguringConnection || credentialBusy) return;
            SetSettingsVisible(!settingsPanel.gameObject.activeSelf);
        }
        void SetSettingsVisible(bool visible)
        {
            if (visible && !settingsPanel.gameObject.activeSelf)
            {
                readerBeforeSettings = workspaceInspector && workspaceInspector.gameObject.activeSelf;
                if (workspaceInspector) workspaceInspector.gameObject.SetActive(false);
            }
            settingsPanel.gameObject.SetActive(visible);
            if (!visible && readerBeforeSettings && workspaceInspector)
            { workspaceInspector.gameObject.SetActive(true); readerBeforeSettings = false; }
        }
        void SettingsConnection(bool open)
        {
            if (!settingsPanel) return;
            SetSettingsVisible(true);
            for (int i = 0; i < settingsPages.Length; i++) settingsPages[i].SetActive(!open && i == 0);
        }
    }
}
