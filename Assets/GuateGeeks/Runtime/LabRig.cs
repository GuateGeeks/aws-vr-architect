using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace GuateGeeks.AwsVr
{
    // OpenXR controller actions and a desktop rehearsal mode share the same interaction targets.
    public sealed partial class LabRig : MonoBehaviour
    {
        sealed class Hand
        {
            public InputAction Position, Rotation, AimPosition, AimRotation, Tracked, Trigger, Grip, Stick, Cancel, Reset;
            public Transform Visual;
            public Transform Reticle;
            public LineRenderer Ray;
            public LabTarget Hover;
            public NodeView Held;
            public NodeView PendingGrab;
            public float PendingDistance;
            public Vector3 PendingOffset;
            public bool PendingNear;
            public bool DrawingPort;
            public LabMenu HeldMenu;
            public float Distance;
            public Vector3 Offset;
            public bool WasGrip;
            public IEnumerable<InputAction> Actions { get { yield return Position; yield return Rotation; yield return AimPosition; yield return AimRotation; yield return Tracked; yield return Trigger; yield return Grip; yield return Stick; yield return Cancel; yield return Reset; } }
        }
        ArchitectureLab lab;
        Camera cam;
        Transform origin, headOffset;
        readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();
        Hand left, right;
        InputAction headPosition, headRotation, wristButton;
        bool xr, seated, paused, focused = true;
        float yaw, pitch = 9;
        LabTarget mouseHover;
        NodeView mouseHeld;
        NodeView mousePendingGrab;
        LabMenu mouseMenu;
        Transform mouseReticle;
        bool previewHasTarget, pointerBlocked;
        float mouseDistance;
        Vector3 mouseOffset;
        TMPro.TMP_Text hint;
        float nextTurn;
        bool wasReset;
        public bool IsXR => xr;
        public Camera ViewCamera => cam;
        public bool AwaitingObjectGrab => mousePendingGrab || left != null && left.PendingGrab || right != null && right.PendingGrab;
        public bool AssistantTalkHeld => xr ? right != null && right.Reset.IsPressed() && !(left != null && left.Reset.IsPressed()) : Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
        public Vector3 PresenceHand => xr && right != null && right.Visual ? right.Visual.position : cam.transform.position + cam.transform.forward * .25f - Vector3.up * .35f;

        public void Initialize(ArchitectureLab owner)
        {
            lab = owner;
            origin = new GameObject("XR Origin · stationary comfort rig").transform; origin.SetParent(transform, false);
            headOffset = new GameObject("Tracking space").transform; headOffset.SetParent(origin, false);
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)); go.tag = "MainCamera"; go.transform.SetParent(headOffset, false);
            cam = go.GetComponent<Camera>(); cam.nearClipPlane = .05f; cam.farClipPlane = 50; cam.fieldOfView = 60;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = LabVisuals.Hex("#060D17");
            cam.transform.localPosition = new Vector3(0, 1.94f, -1.5f); cam.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            left = CreateHand("LeftHand", LabVisuals.Cyan); right = CreateHand("RightHand", LabVisuals.Orange);
            mouseReticle = LabVisuals.Shape(transform,"Desktop target marker",PrimitiveType.Sphere,Vector3.zero,Vector3.one*.014f,LabVisuals.White).transform;
            mouseReticle.gameObject.SetActive(false);
            headPosition = Action("Head position", "<XRHMD>/centerEyePosition", InputActionType.Value);
            headRotation = Action("Head rotation", "<XRHMD>/centerEyeRotation", InputActionType.Value);
            var help = LabVisuals.Panel(transform, "Controls reference", new Vector3(0, .58f, .5f), new Vector2(1360, 120), background: false);
            help.localRotation = Quaternion.Euler(38, 0, 0);
            hint = LabVisuals.Text(help, "", Vector2.zero, new Vector2(1360, 120), 19, LabVisuals.Muted, TextAnchor.MiddleCenter);
            var comfort = LabVisuals.Panel(transform, "Comfort controls", new Vector3(0, 2.97f, 3.99f), new Vector2(1250, 60), background: false);
            // Comfort controls sit below the main subtitle, within reach of either controller ray.
            comfort.localPosition = new Vector3(0, 2.68f, 3.99f);
            LabVisuals.Button(comfort, "Centrar vista", new Vector2(-540, 0), new Vector2(255, 50), Recenter);
            var seat = LabVisuals.Button(comfort, "Altura: de pie", new Vector2(-270, 0), new Vector2(255, 50), null);
            seat.Action = () => { seated = !seated; seat.Label.text = seated ? "Altura: sentado" : "Altura: de pie"; Recenter(); };
            var motion=LabVisuals.Button(comfort,"Animación: SÍ",Vector2.zero,new Vector2(255,50),null);
            motion.Action=()=>{lab.Feedback.ToggleMotion(); motion.Label.text=lab.Feedback.ReducedMotion?"Animación: NO":"Animación: SÍ";};
            var sound=LabVisuals.Button(comfort,"Sonido: SÍ",new Vector2(270,0),new Vector2(255,50),null);
            sound.Action=()=>{lab.Feedback.ToggleAudio(); sound.Label.text=lab.Feedback.Muted?"Sonido: NO":"Sonido: SÍ";};
            var snap=LabVisuals.Button(comfort,"Ajuste: libre",new Vector2(540,0),new Vector2(255,50),null);
            snap.Action=()=>{lab.ToggleGridSnap(); snap.Label.text=lab.GridSnap?"Ajuste: 10 cm":"Ajuste: libre";};
            Application.onBeforeRender += BeforeRender;
            // The left controller's menu button toggles the wrist menu (the right one belongs to the system).
            wristButton = Action("Wrist menu", "<XRController>{LeftHand}/{MenuButton}", InputActionType.Button);
            BuildWristMenu();
        }
        InputAction Action(string name, string binding, InputActionType type)
        {
            var action = new InputAction(name, type, binding); action.Enable(); return action;
        }
        Hand CreateHand(string usage, Color color)
        {
            var h = new Hand(); string path = "<XRController>{" + usage + "}/";
            h.Position = Action(usage + " position", path + "devicePosition", InputActionType.Value);
            h.Rotation = Action(usage + " rotation", path + "deviceRotation", InputActionType.Value);
            h.AimPosition = Action(usage + " aim position", path + "pointerPosition", InputActionType.Value);
            h.AimRotation = Action(usage + " aim rotation", path + "pointerRotation", InputActionType.Value);
            h.Tracked = Action(usage + " tracked", path + "isTracked", InputActionType.Button);
            h.Trigger = Action(usage + " select", path + "triggerPressed", InputActionType.Button);
            h.Grip = Action(usage + " grab", path + "gripPressed", InputActionType.Button);
            h.Stick = Action(usage + " stick", path + "primary2DAxis", InputActionType.Value);
            h.Cancel = Action(usage + " cancel", path + "secondaryButton", InputActionType.Button);
            h.Reset = Action(usage + " recover menus", path + "primaryButton", InputActionType.Button);
            h.Visual = new GameObject(usage + " controller").transform; h.Visual.SetParent(headOffset, false);
            // Controller as a titanium emitter: graphite grip, a glowing aperture ring and core at the front,
            // and an energy-beam ray (travelling dashes, fading with distance) in the hand's colour.
            LabVisuals.Metal(h.Visual, "Controller", PrimitiveType.Capsule, new Vector3(0, -.012f, -.01f), new Vector3(.032f, .056f, .032f), LabVisuals.Hex("#2B3944"));
            LabVisuals.Metal(h.Visual, "Controller collar", PrimitiveType.Cylinder, new Vector3(0, 0, .022f), new Vector3(.03f, .006f, .03f), LabVisuals.Hex("#8AA2B1")).transform.localRotation = Quaternion.Euler(90, 0, 0);
            var aperture = new GameObject("Emitter aperture").transform; aperture.SetParent(h.Visual, false);
            aperture.localPosition = new Vector3(0, 0, .027f); aperture.localRotation = Quaternion.Euler(90, 0, 0);
            var ring = LabVisuals.Ring(aperture, Vector3.zero, .017f, color, .006f, 32); ring.sharedMaterial = LabVisuals.Beam(color, false); ring.textureMode = LineTextureMode.Stretch;
            LabVisuals.Shape(h.Visual, "Emitter core", PrimitiveType.Sphere, new Vector3(0, 0, .028f), Vector3.one * .009f, LabVisuals.Ice);
            h.Ray = LabVisuals.Line(h.Visual, "Selection ray", new[] { Vector3.zero, Vector3.forward * 5 }, color, .008f);
            h.Ray.sharedMaterial = LabVisuals.Beam(color); h.Ray.textureMode = LineTextureMode.Stretch; h.Ray.numCapVertices = 0;
            h.Ray.colorGradient = new Gradient { alphaKeys = new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.85f, .5f), new GradientAlphaKey(.25f, 1) },
                colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) } };
            h.Reticle=LabVisuals.Shape(h.Visual,"Target marker",PrimitiveType.Sphere,Vector3.zero,Vector3.one*.014f,color).transform;
            h.Visual.gameObject.SetActive(false); return h;
        }
        void Update()
        {
            if (!focused || paused) return;
            SubsystemManager.GetSubsystems(displays); bool running = displays.Exists(d => d.running);
            if (running != xr)
            {
                xr = running; ReleaseHands();
                // Headset on: alone at the full table the console becomes the near-field cockpit (unless panoramic was
                // chosen), which also moves the stand; headset off: desktop rehearsal goes back to panoramic.
                if (lab.Space) lab.Space.Refresh();
                HeadFollow.Recenter(); wristLatched = false;
                if (xr)
                {
                    origin.position = StandPosition; origin.rotation = StandRotation;
                    // Fixed foveation: the periphery renders at lower resolution (Quest 3 has no eye tracking).
                    foreach (var display in displays) if (display.running) display.foveatedRenderingLevel = 1;
                    SubsystemManager.GetSubsystems(inputs);
                    foreach (var system in inputs) system.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                    UpdateHead(); Recenter();
                }
                else { origin.SetPositionAndRotation(Vector3.zero, Quaternion.identity); headOffset.localPosition = Vector3.zero; cam.transform.localPosition = new Vector3(0, 1.94f, -1.5f); cam.transform.localRotation = Quaternion.Euler(9, 0, 0); }
            }
            hint.text = xr ? "MUÑECA  gírala hacia ti: menú / TOCA  con cualquier dedo / GATILLO o PINZA  lejos / GRIP  mover / B o Y  soltar\nBotón menú (izq.): muñeca con controles · A + X: recuperar paneles"
                : "CLIC  seleccionar    /    ARRASTRAR  mover    /    RUEDA  distancia    /    CLIC DERECHO  mirar\nWASD  explorar  ·  Q / E  altura  ·  C  conectar  ·  M  muñeca  ·  Esc  cancelar  ·  R  centrar / Shift+R  menús";
            if (xr) {
                if (wristButton.WasPressedThisFrame()) ToggleWristMenu();
                bool reset = left.Tracked.IsPressed() && right.Tracked.IsPressed() && left.Reset.IsPressed() && right.Reset.IsPressed();
                if (reset && !wasReset) ResetMenus(); wasReset = reset;
                previewHasTarget=false; lab.HideConnectionPreview(); mouseReticle.gameObject.SetActive(false); UpdateHead(); UpdateHand(left); UpdateHand(right);
            }
            else UpdateDesktop();
        }
        void BeforeRender() { if (xr) UpdateHead(); }
        void UpdateHead()
        {
            cam.transform.localPosition = headPosition.ReadValue<Vector3>();
            var q = headRotation.ReadValue<Quaternion>(); if (Quaternion.Dot(q, q) > .5f) cam.transform.localRotation = q;
        }
        void UpdateHand(Hand hand)
        {
            bool tracked = hand.Tracked.IsPressed();
            if (!tracked) { UpdateTrackedHand(hand, hand == left); return; }
            StopTrackedHand(hand); hand.Visual.gameObject.SetActive(true); UpdateWristFromController(hand);
            if (!tracked) { EndHandGrab(hand); hand.WasGrip = hand.Grip.IsPressed(); Hover(ref hand.Hover, null); return; }
            hand.Visual.localPosition = hand.Position.ReadValue<Vector3>();
            var rotation = hand.Rotation.ReadValue<Quaternion>(); if (Quaternion.Dot(rotation, rotation) > .5f) hand.Visual.localRotation = rotation;
            var aimRotation = hand.AimRotation.ReadValue<Quaternion>();
            bool hasAim = Quaternion.Dot(aimRotation, aimRotation) > .5f;
            var ray = hasAim ? new Ray(headOffset.TransformPoint(hand.AimPosition.ReadValue<Vector3>()), headOffset.rotation * aimRotation * Vector3.forward)
                : new Ray(hand.Visual.position, hand.Visual.forward);
            var target = Pick(ray, out float distance);
            if (lab.Placing && !target && !pointerBlocked) lab.PreviewPlacement(ray.GetPoint(lab.PointerReach(3)));
            Hover(ref hand.Hover, target);
            lab.ObserveVoicePointer(hand==left?"left":"right",target,ray,pointerBlocked);
            hand.Reticle.gameObject.SetActive(target && target.Available);
            hand.Reticle.position=ray.GetPoint(Mathf.Max(.01f,distance-.02f));
            if (target && target.Node) {lab.PreviewConnection(target.Node,ray.GetPoint(distance));previewHasTarget=true;}
            else if(!previewHasTarget) lab.PreviewConnection(null,ray.GetPoint(lab.PointerReach(3)));
            hand.Ray.SetPosition(0, hand.Visual.InverseTransformPoint(ray.origin));
            hand.Ray.SetPosition(1, hand.Visual.InverseTransformPoint(ray.GetPoint(Mathf.Min(distance, 8))));
            if (hand.Trigger.WasPressedThisFrame() && !hand.Held && !hand.HeldMenu) {
                if (lab.Placing && !target && !pointerBlocked) lab.ConfirmPlacement(); else target?.Activate();
                hand.DrawingPort = target && target.Port == 2; Pulse(hand, .2f);
            }
            if (hand.Trigger.WasReleasedThisFrame() && hand.DrawingPort) { if (target && target.Port == 1) { target.Activate(); Pulse(hand, .35f); } hand.DrawingPort = false; }
            bool grip = hand.Grip.IsPressed();
            if (grip && (!hand.WasGrip || (lab.NetworkRoom != null && !hand.Held && !hand.HeldMenu)))
            {
                if (!hand.WasGrip) hand.PendingGrab = null;
                var menu = !hand.WasGrip && target ? target.Menu : null;
                if (menu && menu.TryGrab(hand, ray, distance, hasAim ? headOffset.rotation * aimRotation : hand.Visual.rotation))
                { hand.HeldMenu = menu; Pulse(hand, .35f); }
                // Near grabbing uses the same object ownership lock as distance grabbing.
                NodeView near = null; float nearest = .22f * lab.Table.Stroke; // smaller holograms on a smaller table
                foreach (var view in lab.Views.Values) { float d = Vector3.Distance(hand.Visual.position, view.transform.position); if (d < nearest) { near = view; nearest = d; } }
                var node = near ? near : target ? target.Node : null;
                if (lab.NetworkRoom != null && !menu)
                {
                    if (!hand.WasGrip && node)
                    {
                        hand.PendingGrab = node; hand.PendingDistance = near ? .13f : distance;
                        hand.PendingOffset = node.transform.position - ray.GetPoint(hand.PendingDistance);
                    }
                    node = hand.PendingGrab;
                }
                if (!menu && node && lab.BeginGrab(node))
                {
                    hand.Held = node; hand.Distance = lab.NetworkRoom != null ? hand.PendingDistance : near ? .13f : distance;
                    hand.Offset = lab.NetworkRoom != null ? hand.PendingOffset : node.transform.position - ray.GetPoint(hand.Distance);
                    hand.PendingGrab = null; Pulse(hand, .35f);
                }
            }
            if (hand.Held)
            {
                hand.Distance = Mathf.Clamp(hand.Distance + hand.Stick.ReadValue<Vector2>().y * Time.unscaledDeltaTime * 1.4f, .13f, 5);
                hand.Held.transform.position = lab.ClampToTable(ray.GetPoint(hand.Distance) + hand.Offset);
            }
            if (hand.HeldMenu)
                hand.HeldMenu.Move(hand, ray, hasAim ? headOffset.rotation * aimRotation : hand.Visual.rotation,
                    hand.Stick.ReadValue<Vector2>().y * Time.unscaledDeltaTime * 1.4f);
            if (!grip && hand.WasGrip) EndHandGrab(hand);
            hand.WasGrip = grip;
            if (hand.Cancel.WasPressedThisFrame()) { ReleaseHands(); lab.CancelInteraction(); }
            float turn = hand.Stick.ReadValue<Vector2>().x;
            // Snap turn would break co-location in a shared room, so it is only available alone.
            if (!SharedSpace.SharedRoomActive && !hand.Held && !hand.HeldMenu && Mathf.Abs(turn) > .75f && Time.unscaledTime > nextTurn)
            { origin.RotateAround(cam.transform.position, Vector3.up, Mathf.Sign(turn) * 30); nextTurn = Time.unscaledTime + .45f; }
        }
        void Pulse(Hand hand, float amount)
        {
            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(hand == left ? XRNode.LeftHand : XRNode.RightHand);
            if (device.TryGetHapticCapabilities(out var capability) && capability.supportsImpulse) device.SendHapticImpulse(0, amount, .04f);
        }
        LabTarget Pick(Ray ray, out float distance)
        {
            pointerBlocked = false;
            if (Physics.Raycast(ray, out var hit, 12, ~0, QueryTriggerInteraction.Collide))
            { distance = hit.distance; var target = hit.collider.GetComponentInParent<LabTarget>(); pointerBlocked = target && !lab.CanInteract(target); return pointerBlocked ? null : target; }
            // Aim assist: a near miss (within 2.5 cm of the ray) still lands on an available target, so small
            // buttons and ports forgive hand tremor at arm's length.
            if (Physics.SphereCast(ray, AimAssistRadius, out hit, 12, ~0, QueryTriggerInteraction.Collide))
            {
                var target = hit.collider.GetComponentInParent<LabTarget>();
                if (target && target.Available && lab.CanInteract(target)) { distance = hit.distance; return target; }
            }
            distance = 8; return null;
        }
        void Hover(ref LabTarget previous, LabTarget next)
        {
            if (previous == next) return;
            if (previous) previous.Hover(false); previous = next; if (previous) previous.Hover(true);
        }
        void UpdateDesktop()
        {
            var mouse = Mouse.current; var keyboard = Keyboard.current;
            if (mouse != null)
            {
                var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
                var target = Pick(ray, out float distance); Hover(ref mouseHover, target);
                lab.ObserveVoicePointer("mouse",target,ray,pointerBlocked);
                if (lab.Placing && !target && !pointerBlocked) lab.PreviewPlacement(ray.GetPoint(lab.PointerReach(3)));
                mouseReticle.gameObject.SetActive(target && target.Available);
                mouseReticle.position=ray.GetPoint(Mathf.Max(.01f,distance-.02f));
                lab.PreviewConnection(target?target.Node:null,ray.GetPoint(target?distance:lab.PointerReach(4)));
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    mousePendingGrab = null;
                    if (target && target.Menu && target.Menu.TryGrab(this, ray, distance, cam.transform.rotation)) mouseMenu = target.Menu;
                    else if (lab.Placing && !target && !pointerBlocked) lab.ConfirmPlacement(); else target?.Activate();
                    if (!mouseMenu && !lab.ConnectingMode && target && target.Port == 0 && target.Node)
                    {
                        mousePendingGrab = target.Node; mouseDistance = distance; mouseOffset = target.Node.transform.position - ray.GetPoint(distance);
                        if (lab.BeginGrab(target.Node)) { mouseHeld = target.Node; mousePendingGrab = null; }
                    }
                }
                if(lab.NetworkRoom != null && mouse.leftButton.isPressed && !mouseHeld && !mouseMenu && !lab.ConnectingMode && mousePendingGrab && lab.BeginGrab(mousePendingGrab))
                { mouseHeld=mousePendingGrab; mousePendingGrab=null; }
                if (mouse.leftButton.wasReleasedThisFrame) mousePendingGrab = null;
                if (mouseMenu)
                {
                    mouseMenu.Move(this, ray, cam.transform.rotation, mouse.scroll.ReadValue().y * .002f);
                    if (mouse.leftButton.wasReleasedThisFrame) { mouseMenu.Release(this); mouseMenu = null; }
                }
                if (mouseHeld)
                {
                    mouseDistance = Mathf.Clamp(mouseDistance + mouse.scroll.ReadValue().y * .002f, .5f, 7);
                    mouseHeld.transform.position = lab.ClampToTable(ray.GetPoint(mouseDistance) + mouseOffset);
                    if (mouse.leftButton.wasReleasedThisFrame) { lab.EndGrab(mouseHeld); mouseHeld = null; }
                }
                if (mouse.rightButton.isPressed)
                { var delta = mouse.delta.ReadValue(); yaw += delta.x * .12f; pitch = Mathf.Clamp(pitch - delta.y * .12f, -65, 65); cam.transform.localRotation = Quaternion.Euler(pitch, yaw, 0); }
            }
            if (keyboard == null) { UpdateWristDesktop(); return; }
            if (lab.ConfiguringConnection || lab.EditingText) { HideWrist(); if (keyboard.escapeKey.wasPressedThisFrame) lab.CancelInteraction(); return; }
            if (keyboard.mKey.wasPressedThisFrame) ToggleWristMenu();
            UpdateWristDesktop();
            var move = new Vector3((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.eKey.isPressed ? 1 : 0) - (keyboard.qKey.isPressed ? 1 : 0), (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            cam.transform.position += Quaternion.Euler(0, yaw, 0) * move * Time.unscaledDeltaTime * 1.3f;
            var pos = cam.transform.position; cam.transform.position = new Vector3(Mathf.Clamp(pos.x, -6.5f, 6.5f), Mathf.Clamp(pos.y, .6f, 3.5f), Mathf.Clamp(pos.z, -5, 7));
            if (keyboard.cKey.wasPressedThisFrame && !lab.Busy) lab.ToggleConnect();
            if (keyboard.escapeKey.wasPressedThisFrame) { ReleaseHands(); lab.CancelInteraction(); }
            if (keyboard.rKey.wasPressedThisFrame) { if (keyboard.shiftKey.isPressed) ResetMenus(); else Recenter(); }
        }
        void EndHandGrab(Hand hand) { hand.DrawingPort = false; if (hand.Held) lab.EndGrab(hand.Held); if (hand.HeldMenu) hand.HeldMenu.Release(hand); hand.Held = null; hand.PendingGrab = null; hand.HeldMenu = null; hand.WasGrip = false; }
        public void ReleaseForConfiguration() => ReleaseHands();
        void ReleaseHands()
        {
            HideWrist();
            if (lab) { lab.HideConnectionPreview(); lab.StopFlowPreview(); }
            if (left != null) { StopTrackedHand(left); EndHandGrab(left); left.WasGrip = left.Grip.IsPressed(); left.Visual.gameObject.SetActive(false); Hover(ref left.Hover, null); }
            if (right != null) { StopTrackedHand(right); EndHandGrab(right); right.WasGrip = right.Grip.IsPressed(); right.Visual.gameObject.SetActive(false); Hover(ref right.Hover, null); }
            if (mouseHeld) lab.EndGrab(mouseHeld); mouseHeld = null; mousePendingGrab = null; Hover(ref mouseHover, null);
            if (mouseMenu) mouseMenu.Release(this); mouseMenu = null;
            if(mouseReticle) mouseReticle.gameObject.SetActive(false);
        }
        public const float AimAssistRadius = .025f;
        // Where the local person stands: in front of the table alone, or on their numbered station in a shared room.
        Vector3 StandPosition { get { var space = lab ? lab.Space : null; return space ? space.StandPosition : new Vector3(0, 0, -.5f); } }
        Quaternion StandRotation => lab && lab.Space ? lab.Space.Rotation : Quaternion.identity;
        public void Recenter()
        {
            ReleaseHands();
            // The console returns to the station (undoing «Traer aquí») and reading panels re-follow the new view.
            if (lab && lab.Space) lab.Space.ResetAnchor();
            HeadFollow.Recenter();
            var stand = StandPosition; var facing = StandRotation;
            if (xr)
            {
                // Align the current physical head pose to the station (facing the table centre) without requiring
                // runtime recenter support. In a shared room this is the co-location step for each headset.
                origin.rotation = facing * Quaternion.Euler(0, -cam.transform.localEulerAngles.y, 0);
                Vector3 physical = origin.rotation * cam.transform.localPosition;
                origin.position = new Vector3(stand.x - physical.x, 0, stand.z - physical.z);
                // Seated mode explicitly lifts the workspace viewpoint to a comfortable standing height.
                headOffset.localPosition = new Vector3(0, seated ? 1.65f - cam.transform.localPosition.y : 0, 0);
            }
            else
            {
                // Desktop rehearsal: one metre behind the station, looking at the table.
                yaw = facing.eulerAngles.y; pitch = 9;
                var eye = stand + facing * Vector3.back; eye.y = seated ? 1.65f : 1.94f;
                cam.transform.localPosition = eye; cam.transform.localRotation = Quaternion.Euler(pitch, yaw, 0);
            }
        }
        public void ResetMenus()
        {
            ReleaseHands();
            if (lab.Space) lab.Space.ResetAnchor();
            foreach (var menu in lab.GetComponentsInChildren<LabMenu>(true)) menu.ResetPose();
            HeadFollow.Recenter();
            lab.SetStatus("Menús restaurados a su posición inicial.");
        }
        void OnApplicationFocus(bool hasFocus) { focused = hasFocus; if (!hasFocus) ReleaseHands(); }
        void OnApplicationPause(bool isPaused) { paused = isPaused; if (isPaused) ReleaseHands(); }
        void OnDestroy()
        {
            Application.onBeforeRender -= BeforeRender;
            foreach (var hand in new[] { left, right }) if (hand != null) foreach (var a in hand.Actions) a?.Dispose();
            headPosition?.Dispose(); headRotation?.Dispose(); wristButton?.Dispose();
        }
    }
}
