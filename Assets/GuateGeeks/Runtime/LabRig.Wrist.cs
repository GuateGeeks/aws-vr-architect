using UnityEngine;
using UnityEngine.XR.Hands;

namespace GuateGeeks.AwsVr
{
    // Wrist menu. Turn the inside of the wrist toward your face and a touch-sized menu appears beside it, on the
    // little-finger side, so the other hand reaches it without crossing the menu hand. Tap with any finger of the
    // other hand (or aim and pinch). It stays up while a finger is on it, and folds away when the wrist turns.
    // Controllers: the left menu button toggles it above the controller. Desktop rehearsal: M toggles it in the
    // lower-left corner of the view. The menu lives on the left wrist by default; settings can move it right.
    public sealed partial class LabRig
    {
        public const string WristPreference = "GuateGeeks.Wrist.Right.v1";
        public const float WristScale = .00021f;
        static readonly Vector2 WristSize = new Vector2(820, 610), WristButton = new Vector2(172, 140);
        static readonly XRHandJointID[] FingerTips = { XRHandJointID.ThumbTip, XRHandJointID.IndexTip, XRHandJointID.MiddleTip, XRHandJointID.RingTip, XRHandJointID.LittleTip };
        RectTransform wristMenu;
        TMPro.TMP_Text wristHint;
        LabTarget[] wristButtons;
        float wristDwell, wristGrace;
        bool wristLatched, wristShownByHand;
        readonly Collider[] touchHits = new Collider[24];
        public bool WristOnRight { get; private set; }
        public RectTransform WristMenu => wristMenu;
        public bool WristMenuVisible => wristMenu && wristMenu.gameObject.activeSelf;

        void BuildWristMenu()
        {
            WristOnRight = PlayerPrefs.GetInt(WristPreference, 0) == 1;
            wristMenu = LabVisuals.Focus(LabVisuals.Panel(transform, "Wrist menu", Vector3.zero, WristSize, movable: false));
            wristMenu.localScale = Vector3.one * WristScale;
            LabVisuals.Text(wristMenu, "MUÑECA  /  ACCESO", new Vector2(-160, 262), new Vector2(460, 50), 24, LabVisuals.Cyan);
            wristHint = LabVisuals.Text(wristMenu, "", new Vector2(190, 262), new Vector2(390, 50), 19, LabVisuals.Muted, TextAnchor.MiddleRight);
            string[] labels = { "CREAR", "MESA", "FICHA", "ATLAS", "INSPECCIÓN", "CÓDIGO", "AJUSTES", "GUÍA", "DESHACER", "CONECTAR", "TRAER AQUÍ", "CENTRAR" };
            System.Action[] actions = {
                lab.ToggleCatalog, lab.ToggleControls, lab.ToggleObjectInspector, lab.ToggleAssistantPanel,
                lab.ToggleInspection, lab.ToggleSelectedLambdaCode, () => lab.OpenQuickSettings(), lab.ToggleGuide,
                lab.UndoLast, () => { if (!lab.Busy) lab.ToggleConnect(); }, BringPanelsHere, Recenter };
            wristButtons = new LabTarget[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int row = i / 4, column = i % 4;
                var button = LabVisuals.Button(wristMenu, labels[i], new Vector2((column - 1.5f) * (WristButton.x + 24), 140 - row * (WristButton.y + 24)), WristButton, actions[i]);
                button.Label.enableAutoSizing = true; button.Label.fontSizeMin = 16; button.Label.fontSizeMax = 26; button.Label.fontSize = 26;
                LabVisuals.Retrack(button.Label, 4);
                wristButtons[i] = button;
            }
            wristMenu.gameObject.SetActive(false);
        }
        public void SetWristOnRight(bool right)
        {
            WristOnRight = right; PlayerPrefs.SetInt(WristPreference, right ? 1 : 0); PlayerPrefs.Save();
            HideWrist(); wristLatched = false;
        }
        // Controller button or desktop key. In the headset with tracked hands the wrist gesture is enough.
        public void ToggleWristMenu() { wristLatched = !wristLatched; if (!wristLatched) HideWrist(); else if (!xr) UpdateWristDesktop(true); }
        public void ShowWristMenu(bool show) { wristLatched = show; if (!show) HideWrist(); else if (!xr) UpdateWristDesktop(true); }
        bool WristAllowed(Hand host) => lab && !lab.EditingText && !lab.ConfiguringConnection && !(host != null && (host.Held || host.HeldMenu));
        bool IsWristHost(Hand hand) => hand != null && (hand == right) == WristOnRight;
        bool WristTouched
        {
            get { if (wristButtons != null) foreach (var b in wristButtons) if (b && b.Hovered) return true; return false; }
        }
        void HideWrist() { wristDwell = 0; wristShownByHand = false; if (wristMenu && wristMenu.gameObject.activeSelf) wristMenu.gameObject.SetActive(false); }

        // Tracked hand: show when the inner wrist faces the eyes for a moment, hide after the wrist turns away.
        void UpdateWristFromHand(XRHand hand, bool isLeft)
        {
            var host = isLeft ? left : right; if (!IsWristHost(host)) return;
            wristLatched = false;
            if (!WristAllowed(host) || !hand.isTracked || !JointPosition(hand, XRHandJointID.Wrist, out var wrist) || !hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm)
                || !JointPosition(hand, XRHandJointID.IndexProximal, out var index) || !JointPosition(hand, XRHandJointID.LittleProximal, out var little)
                || !JointPosition(hand, XRHandJointID.MiddleProximal, out var middle)) { HideWrist(); return; }
            var eye = cam.transform.position;
            var normal = headOffset.rotation * palm.rotation * Vector3.down;
            var toward = (eye - wrist).normalized;
            bool facing = Vector3.Dot(normal, toward) > .55f && Vector3.Angle(cam.transform.forward, wrist - eye) < 50;
            wristDwell = facing ? wristDwell + Time.unscaledDeltaTime : 0;
            if (facing) wristGrace = Time.unscaledTime + .4f;
            bool visible = WristMenuVisible;
            // Never fold away under a finger that is pressing it.
            if (visible && WristTouched) wristGrace = Mathf.Max(wristGrace, Time.unscaledTime + .4f);
            bool show = visible ? Time.unscaledTime <= wristGrace : wristDwell > .22f;
            if (!show) { HideWrist(); return; }
            var ulnar = (little - index).normalized; var along = (middle - wrist).normalized;
            ShowWristAt(wrist + ulnar * .1f - along * .02f + toward * .035f, !visible, "Toca con cualquier dedo");
            wristShownByHand = true;
        }
        // Controller: the menu button latches the menu above the host controller.
        void UpdateWristFromController(Hand hand)
        {
            if (!IsWristHost(hand)) return;
            if (!wristLatched || !WristAllowed(hand)) { if (!wristShownByHand) HideWrist(); return; }
            var eye = cam.transform.position; var anchor = hand.Visual.position;
            ShowWristAt(anchor + Vector3.up * .13f + (eye - anchor).normalized * .03f, !WristMenuVisible, "Gatillo · botón menú cierra");
        }
        void UpdateWristDesktop(bool snap = false)
        {
            if (!wristLatched || !WristAllowed(null)) { HideWrist(); return; }
            var head = cam.transform;
            ShowWristAt(head.position + head.rotation * new Vector3(-.17f, -.12f, .5f), snap || !WristMenuVisible, "Clic · M cierra");
        }
        void ShowWristAt(Vector3 position, bool snap, string hint)
        {
            if (!wristMenu) return;
            if (!wristMenu.gameObject.activeSelf) { wristMenu.gameObject.SetActive(true); snap = true; }
            var rotation = Quaternion.LookRotation(position - cam.transform.position, Vector3.up);
            if (snap || lab.Feedback && lab.Feedback.ReducedMotion) wristMenu.SetPositionAndRotation(position, rotation);
            else
            {
                float k = 1 - Mathf.Exp(-18 * Time.unscaledDeltaTime);
                wristMenu.SetPositionAndRotation(Vector3.Lerp(wristMenu.position, position, k), Quaternion.Slerp(wristMenu.rotation, rotation, k));
            }
            if (wristHint.text != hint) wristHint.text = hint;
            RefreshWristStates();
        }
        // Lit buttons show which panels are open, so the wrist doubles as a map of the workspace.
        void RefreshWristStates()
        {
            if (wristButtons == null) return;
            Lit(wristButtons[0], lab.CatalogVisible); Lit(wristButtons[1], lab.ControlsVisible); Lit(wristButtons[2], lab.ObjectInspectorVisible);
            Lit(wristButtons[3], lab.AssistantPanelVisible); Lit(wristButtons[4], lab.InspectionVisible); Lit(wristButtons[5], lab.CodeStudioVisible);
            Lit(wristButtons[6], lab.SettingsVisible); Lit(wristButtons[7], lab.GuideVisible); Lit(wristButtons[9], lab.ConnectingMode);
            bool code = lab.CodeStudioVisible || lab.SelectedIsLambda;
            if (wristButtons[5].Available != code) wristButtons[5].SetAvailable(code);
        }
        static void Lit(LabTarget target, bool on)
        {
            if (!target) return;
            var color = on ? LabVisuals.Green : LabVisuals.White;
            if (target.Accent == color) return;
            target.Accent = color; if (target.Surface) target.Surface.Accent = on ? LabVisuals.Green : LabVisuals.Cyan;
            target.SetAvailable(target.Available);
        }
        // «Traer aquí»: the console comes to where you stand and look; head-following panels re-centre and unpin.
        public void BringPanelsHere()
        {
            bool moved = lab.Space && lab.Space.BringTo(cam.transform);
            HeadFollow.Recenter();
            lab.SetStatus(moved || !lab.Space ? "Paneles frente a ti, al alcance de la mano."
                : "Sala compartida: tus paneles vuelven a tu estación para no invadir la de otra persona.");
        }
        bool JointPosition(XRHand hand, XRHandJointID id, out Vector3 position)
        {
            position = Vector3.zero;
            if (!hand.GetJoint(id).TryGetPose(out var pose)) return false;
            position = headOffset.TransformPoint(pose.position); return true;
        }
        // Direct touch probe used by every fingertip: the nearest touchable button whose face the tip is in front of.
        public LabTarget TouchProbe(Vector3 tip, bool fromWristHost, out float distance)
        {
            distance=float.MaxValue; LabTarget best=null;
            int count=Physics.OverlapSphereNonAlloc(tip,.055f,touchHits,~0,QueryTriggerInteraction.Collide);
            for(int i=0;i<count;i++) {
                var target=touchHits[i].GetComponent<LabTarget>();
                if(!target || !target.Surface || target.Menu || !target.Available || !lab.CanInteract(target)) continue;
                // The hand wearing the menu cannot press it: only the other hand can.
                if(fromWristHost && wristMenu && target.transform.IsChildOf(wristMenu)) continue;
                var rect=target.transform as RectTransform; if(!rect) continue;
                Vector3 local=rect.InverseTransformPoint(tip);
                float front=-local.z*Mathf.Abs(rect.lossyScale.z);
                if(!rect.rect.Contains(new Vector2(local.x,local.y)) || front<-.025f || front>.055f) continue;
                if(Mathf.Abs(front)<Mathf.Abs(distance)) { distance=front; best=target; }
            }
            return best;
        }
        LabTarget TouchTarget(Vector3 tip, bool isLeft, out float distance) => TouchProbe(tip, IsWristHost(isLeft ? left : right), out distance);
    }
}
