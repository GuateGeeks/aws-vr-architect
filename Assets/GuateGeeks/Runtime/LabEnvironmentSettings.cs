using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

namespace GuateGeeks.AwsVr
{
    public enum LabBackground { VirtualRoom, Hidden, Passthrough }

    public sealed class LabEnvironmentSettings : MonoBehaviour
    {
        public const string PreferenceKey = "GuateGeeks.Background.v1";
        public LabBackground Mode { get; private set; }
        public bool PassthroughActive { get; private set; }
        public GameObject VirtualRoom { get; private set; }
        ArchitectureLab lab;
        Camera view;
        ARCameraManager cameraManager;
        ARSession session;
        TMPro.TMP_Text description;
        LabTarget roomButton, hiddenButton, passthroughButton;
        bool paused;
        float requestedAt;

        public void Initialize(ArchitectureLab owner, GameObject room)
        {
            lab = owner; VirtualRoom = room; view = lab.Rig.ViewCamera;
            var panel = LabVisuals.Panel(transform, "04 · Environment settings", new Vector3(0, 3.62f, 3.6f), new Vector2(1120, 190));
            LabVisuals.Text(panel, "04 / CONFIGURACIÓN DEL ESPACIO", new Vector2(0, 67), new Vector2(1060, 36), 22, LabVisuals.Cyan, TextAnchor.MiddleCenter);
            roomButton = LabVisuals.Button(panel, "Fondo: laboratorio", new Vector2(-355, 15), new Vector2(340, 52), () => SetMode(LabBackground.VirtualRoom));
            hiddenButton = LabVisuals.Button(panel, "Fondo: oculto", new Vector2(0, 15), new Vector2(340, 52), () => SetMode(LabBackground.Hidden));
            passthroughButton = LabVisuals.Button(panel, "Passthrough", new Vector2(355, 15), new Vector2(340, 52), () => SetMode(LabBackground.Passthrough));
            description = LabVisuals.Text(panel, "", new Vector2(-130, -56), new Vector2(790, 52), 18, LabVisuals.Muted, TextAnchor.MiddleCenter);
            LabVisuals.Button(panel, "Restaurar menús", new Vector2(400, -56), new Vector2(280, 40), lab.Rig.ResetMenus);
            int saved = PlayerPrefs.GetInt(PreferenceKey, 0);
            Mode = saved >= 0 && saved <= 2 ? (LabBackground)saved : LabBackground.VirtualRoom;
            if (Application.isEditor && Mode == LabBackground.Passthrough) Mode = LabBackground.VirtualRoom;
            requestedAt = Time.unscaledTime; ApplyVisuals();
        }
        public bool SetMode(LabBackground mode)
        {
            if (mode < LabBackground.VirtualRoom || mode > LabBackground.Passthrough) return false;
            if (mode == LabBackground.Passthrough && CameraSubsystem() == null)
            { lab.SetStatus("Passthrough requiere un Quest con OpenXR activo. Puedes ocultar el fondo en PC."); return false; }
            Mode = mode; requestedAt = Time.unscaledTime;
            if (mode != LabBackground.Passthrough && cameraManager) cameraManager.enabled = false;
            PlayerPrefs.SetInt(PreferenceKey, (int)mode); PlayerPrefs.Save(); ApplyVisuals();
            return true;
        }
        XRCameraSubsystem CameraSubsystem() => XRGeneralSettings.Instance && XRGeneralSettings.Instance.Manager && XRGeneralSettings.Instance.Manager.activeLoader
            ? XRGeneralSettings.Instance.Manager.activeLoader.GetLoadedSubsystem<XRCameraSubsystem>() : null;
        void Update()
        {
            if (!lab) return;
            bool requested = Mode == LabBackground.Passthrough;
            if (requested && !paused && lab.Rig.IsXR && CameraSubsystem() != null)
            {
                if (!session)
                {
                    var go = new GameObject("Passthrough AR session"); go.SetActive(false); go.transform.SetParent(transform, false);
                    session = go.AddComponent<ARSession>(); session.matchFrameRateRequested = false; go.SetActive(true);
                }
                if (!cameraManager) cameraManager = view.gameObject.AddComponent<ARCameraManager>();
                if (!cameraManager.enabled) cameraManager.enabled = true;
            }
            bool active = requested && !paused && cameraManager && cameraManager.enabled && cameraManager.subsystem != null && cameraManager.subsystem.running;
            if (active != PassthroughActive)
            {
                PassthroughActive = active; ApplyVisuals();
                Debug.Log("AWS Architect Lab passthrough: " + (active ? "active" : "inactive"));
            }
            if (requested && !paused && !active && Time.unscaledTime - requestedAt > 12)
            {
                SetMode(LabBackground.VirtualRoom);
                lab.SetStatus("No se pudo activar passthrough. Se restauró el laboratorio; puedes volver a intentarlo.");
            }
        }
        void ApplyVisuals()
        {
            if (Mode != LabBackground.Passthrough) PassthroughActive = false;
            VirtualRoom.SetActive(Mode == LabBackground.VirtualRoom || (Mode == LabBackground.Passthrough && !PassthroughActive));
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = PassthroughActive ? Color.clear : LabVisuals.Hex("#060D17");
            roomButton.SetAvailable(Mode != LabBackground.VirtualRoom);
            hiddenButton.SetAvailable(Mode != LabBackground.Hidden);
            passthroughButton.SetAvailable(Mode != LabBackground.Passthrough);
            description.text = Mode == LabBackground.Passthrough ? (PassthroughActive ? "Tu habitación real · hologramas y menús visibles" : "Activando passthrough en Quest…")
                : Mode == LabBackground.Hidden ? "Entorno virtual oculto · fondo oscuro" : "Laboratorio holográfico · toma las asas para mover menús";
        }
        void OnApplicationPause(bool value)
        {
            paused = value; requestedAt = Time.unscaledTime;
            if (value && cameraManager) cameraManager.enabled = false;
        }
        void OnDestroy() { if (cameraManager) Destroy(cameraManager); }
    }
}
