using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace GuateGeeks.AwsVr
{
    // Hysteresis prevents jitter clicks. Tracking recovery always requires an open hand first.
    public sealed class HandPinchState
    {
        bool armed;
        public bool Pressed { get; private set; }
        public bool Started { get; private set; }
        public bool Released { get; private set; }
        public void Step(bool valid, float strength)
        {
            Started = Released = false;
            if (!valid) { Released = Pressed; Pressed = false; armed = false; return; }
            if (strength < .55f) { armed = true; Released = Pressed; Pressed = false; }
            else if (armed && !Pressed && strength >= .8f) { Pressed = Started = true; }
        }
    }

    public sealed partial class LabRig
    {
        sealed class TrackedHand
        {
            public readonly HandPinchState Pinch = new HandPinchState();
            public GameObject Skeleton;
            public ArmoredHandVisual Mesh;
            public bool Active, PlaceOnRelease;
            public LabTarget PressTarget;
            public Vector3 AimPosition;
            public Quaternion AimRotation;
            public readonly DirectTouchState Touch = new DirectTouchState();
            public bool NearGrab;
        }
        readonly Dictionary<Hand, TrackedHand> trackedHands = new Dictionary<Hand, TrackedHand>();
        readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
        void StopTrackedHand(Hand hand)
        {
            if (!trackedHands.TryGetValue(hand, out var state) || !state.Active) return;
            EndHandGrab(hand); Hover(ref hand.Hover, null); state.Active = false; state.Pinch.Step(false, 0);
            state.PressTarget = null; state.PlaceOnRelease = false; state.Skeleton.SetActive(false);
            state.Touch.Reset(); state.NearGrab=false; if(hand==left) HidePalm();
            hand.Visual.Find("Controller").gameObject.SetActive(true);
            hand.WasGrip = hand.Grip.IsPressed();
        }
        void UpdateTrackedHand(Hand hand, bool isLeft)
        {
            SubsystemManager.GetSubsystems(handSubsystems);
            var subsystem = handSubsystems.Find(s => s.running);
            var skeleton = subsystem == null ? default : isLeft ? subsystem.leftHand : subsystem.rightHand;
            var device = isLeft ? MetaAimHand.left : MetaAimHand.right;
            var flags = device == null ? MetaAimFlags.None : (MetaAimFlags)device.aimFlags.ReadValue();
            bool hasAim=(flags & MetaAimFlags.Valid)!=0;
            bool valid = device != null && device.isTracked.isPressed && (hasAim || skeleton.isTracked) &&
                (flags & (MetaAimFlags.SystemGesture | MetaAimFlags.MenuPressed)) == 0;
            if (!valid)
            {
                StopTrackedHand(hand); EndHandGrab(hand); Hover(ref hand.Hover, null); hand.Visual.gameObject.SetActive(false); return;
            }
            if (!trackedHands.TryGetValue(hand, out var state))
            {
                state = new TrackedHand { Skeleton = new GameObject(isLeft ? "Left armored hand" : "Right armored hand") };
                state.Skeleton.transform.SetParent(headOffset, false);
                state.Mesh = state.Skeleton.AddComponent<ArmoredHandVisual>(); state.Mesh.Initialize(isLeft);
                trackedHands.Add(hand, state);
            }
            Vector3 aim = device.devicePosition.ReadValue(); Quaternion rotation = device.deviceRotation.ReadValue();
            if (!state.Active)
            {
                EndHandGrab(hand); state.Active = true; state.AimPosition = aim; state.AimRotation = rotation;
                state.Pinch.Step(false, 0); state.Skeleton.SetActive(true); hand.Visual.Find("Controller").gameObject.SetActive(false);
            }
            float blend = 1 - Mathf.Exp(-22 * Time.unscaledDeltaTime);
            state.AimPosition = Vector3.Lerp(state.AimPosition, aim, blend); state.AimRotation = Quaternion.Slerp(state.AimRotation, rotation, blend);
            hand.Visual.gameObject.SetActive(true); hand.Visual.SetLocalPositionAndRotation(state.AimPosition, state.AimRotation);
            var ray = new Ray(hand.Visual.position, hand.Visual.forward);
            float distance=8; var target = hasAim?Pick(ray,out distance):null;
            UpdatePalm(skeleton,isLeft);
            Vector3 tip=Vector3.zero; bool hasTip=skeleton.isTracked && skeleton.GetJoint(XRHandJointID.IndexTip).TryGetPose(out _);
            if(state.NearGrab && hand.Held && !hasTip) {EndHandGrab(hand);state.NearGrab=false;state.Pinch.Step(false,0);}
            LabTarget touch=null;
            if(hasTip) {
                skeleton.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var tipPose); tip=headOffset.TransformPoint(tipPose.position);
                if(!hand.Held && !hand.HeldMenu) {
                    touch=TouchTarget(tip,isLeft,out float front);
                    bool fire=state.Touch.Step(touch,front,touch,Time.unscaledDeltaTime);
                    if(touch) {target=touch; distance=Vector3.Distance(ray.origin,tip);}
                    if(fire) { touch.Activate(); state.PressTarget=null; state.Pinch.Step(false,0); }
                }
            } else state.Touch.Reset();
            NodeView near=null; float nearest=.16f;
            if(hasTip && !touch && !lab.ConnectingMode && !hand.HeldMenu) foreach(var view in lab.Views.Values) {
                float d=Vector3.Distance(tip,view.transform.position); if(d<nearest && lab.CanInteract(view.Target)) {nearest=d;near=view;}
            }
            if(near && !touch) target=near.Target;
            Hover(ref hand.Hover, target);
            if(hasAim || target)lab.ObserveVoicePointer(isLeft?"left":"right",target,ray,!hasAim && !target || pointerBlocked);
            hand.Ray.SetPosition(0, Vector3.zero); hand.Ray.SetPosition(1, Vector3.forward * Mathf.Min(distance, 8));
            hand.Reticle.gameObject.SetActive(target && target.Available); hand.Reticle.position = touch || near ? tip : ray.GetPoint(Mathf.Max(.01f, distance - .02f));
            if (hasAim && lab.Placing && !target && !pointerBlocked) lab.PreviewPlacement(ray.GetPoint(3));
            if (target && target.Node) { lab.PreviewConnection(target.Node, ray.GetPoint(distance)); previewHasTarget = true; }
            else if (!previewHasTarget) lab.PreviewConnection(null, ray.GetPoint(3));
            float strength=device.pinchStrengthIndex.ReadValue();
            // Entering the touch zone cancels a pending ray click; disabling pinch can emit Released.
            if(touch) { state.PressTarget=null; state.PlaceOnRelease=false; }
            state.Pinch.Step(!touch, strength);
            hand.Reticle.localScale=Vector3.one*Mathf.Lerp(.014f,.028f,strength);
            hand.Reticle.GetComponent<Renderer>().sharedMaterial=LabVisuals.Material(state.Pinch.Pressed?LabVisuals.Green:LabVisuals.White);
            hand.Ray.widthMultiplier=state.Pinch.Pressed?.012f:.007f; // soft beam: the visible core is about half its width
            if (state.Pinch.Started || (lab.NetworkRoom != null && state.Pinch.Pressed && !hand.Held && !hand.HeldMenu && target && target.Node))
            {
                state.PressTarget = target; state.PlaceOnRelease = hasAim && lab.Placing && !target && !pointerBlocked;
                if (target && target.Menu && target.Menu.TryGrab(hand, ray, distance, hand.Visual.rotation)) hand.HeldMenu = target.Menu;
                else if (target && target.Node && target.Port == 0)
                {
                    target.Activate();
                    if (!lab.ConnectingMode && lab.BeginGrab(target.Node)) { hand.Held = target.Node; state.NearGrab=near==target.Node; hand.Distance = distance; hand.Offset = hand.Held.transform.position - (state.NearGrab?tip:ray.GetPoint(distance)); }
                    state.PressTarget = null;
                }
                else if (target && target.Port == 2) { target.Activate(); hand.DrawingPort = true; state.PressTarget = null; }
            }
            if (state.Pinch.Pressed)
            {
                if (hand.Held) hand.Held.transform.position = ArchitectureLab.ClampWorkspace((state.NearGrab && hasTip?tip:ray.GetPoint(hand.Distance)) + hand.Offset);
                if (hand.HeldMenu) hand.HeldMenu.Move(hand, ray, hand.Visual.rotation, 0);
            }
            if (state.Pinch.Released)
            {
                bool wasHolding = hand.Held || hand.HeldMenu;
                if (hand.DrawingPort && target && target.Port == 1) target.Activate();
                else if (!wasHolding && state.PressTarget && target == state.PressTarget) target.Activate();
                else if (state.PlaceOnRelease && lab.Placing && !target && !pointerBlocked) lab.ConfirmPlacement();
                EndHandGrab(hand); state.PressTarget = null; state.PlaceOnRelease = false;
            }
            state.Skeleton.SetActive(subsystem != null && skeleton.isTracked);
            if (subsystem != null && skeleton.isTracked) state.Mesh.UpdateHand(skeleton);
        }
    }
}
