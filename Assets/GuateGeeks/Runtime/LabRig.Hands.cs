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
            // Five fingertip probes per hand: any finger can press a button or a key.
            public readonly MultiFingerTouch Fingers = new MultiFingerTouch();
            public readonly LabTarget[] FingerHover = new LabTarget[MultiFingerTouch.Fingers];
            public readonly object[] FingerTargets = new object[MultiFingerTouch.Fingers];
            public readonly float[] FingerFronts = new float[MultiFingerTouch.Fingers];
            public readonly Vector3[] FingerTips = new Vector3[MultiFingerTouch.Fingers];
            public readonly LineRenderer[] Cursors = new LineRenderer[MultiFingerTouch.Fingers];
            public bool NearGrab;
        }
        readonly Dictionary<Hand, TrackedHand> trackedHands = new Dictionary<Hand, TrackedHand>();
        readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
        void StopTrackedHand(Hand hand)
        {
            if (!trackedHands.TryGetValue(hand, out var state) || !state.Active) return;
            EndHandGrab(hand); Hover(ref hand.Hover, null); state.Active = false; state.Pinch.Step(false, 0);
            state.PressTarget = null; state.PlaceOnRelease = false; state.Skeleton.SetActive(false);
            ResetFingers(state); state.NearGrab=false; if(IsWristHost(hand)) HideWrist();
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
            UpdateWristFromHand(skeleton,isLeft);
            Vector3 tip=Vector3.zero; bool hasTip=skeleton.isTracked && skeleton.GetJoint(XRHandJointID.IndexTip).TryGetPose(out _);
            if(state.NearGrab && hand.Held && !hasTip) {EndHandGrab(hand);state.NearGrab=false;state.Pinch.Step(false,0);}
            if(hasTip) { skeleton.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var tipPose); tip=headOffset.TransformPoint(tipPose.position); }
            // Every fingertip probes the surface in front of it; the closest contact steers the hand's hover and reticle.
            LabTarget touch=null; Vector3 touchTip=tip; float touchFront=float.MaxValue;
            bool canTouch=!hand.Held && !hand.HeldMenu && skeleton.isTracked;
            for(int f=0;f<MultiFingerTouch.Fingers;f++) {
                LabTarget hit=null; float front=0;
                if(canTouch && skeleton.GetJoint(FingerTips[f]).TryGetPose(out var fingerPose)) {
                    state.FingerTips[f]=headOffset.TransformPoint(fingerPose.position);
                    hit=TouchTarget(state.FingerTips[f],isLeft,out front);
                }
                state.FingerTargets[f]=hit; state.FingerFronts[f]=hit?front:0;
                // Glow and cursor only for fingers that are really coming in (3 cm), so a flat hand is not noisy.
                var shown=hit && front<.03f?hit:null;
                Hover(ref state.FingerHover[f],shown); ShowTouchCursor(state,f,shown,front);
                if(hit && Mathf.Abs(front)<Mathf.Abs(touchFront)) {touch=hit;touchFront=front;touchTip=state.FingerTips[f];}
            }
            int pressed=state.Fingers.Step(state.FingerTargets,state.FingerFronts,Time.unscaledTime,Time.unscaledDeltaTime);
            if(touch) {target=touch; distance=Vector3.Distance(ray.origin,touchTip);}
            if(pressed>=0 && state.FingerTargets[pressed] is LabTarget key && key) { key.Activate(); state.PressTarget=null; state.Pinch.Step(false,0); }
            NodeView near=null; float nearest=.16f*lab.Table.Stroke;
            if(hasTip && !touch && !lab.ConnectingMode && !hand.HeldMenu) foreach(var view in lab.Views.Values) {
                float d=Vector3.Distance(tip,view.transform.position); if(d<nearest && lab.CanInteract(view.Target)) {nearest=d;near=view;}
            }
            if(near && !touch) target=near.Target;
            Hover(ref hand.Hover, target);
            if(hasAim || target)lab.ObserveVoicePointer(isLeft?"left":"right",target,ray,!hasAim && !target || pointerBlocked);
            hand.Ray.SetPosition(0, Vector3.zero); hand.Ray.SetPosition(1, Vector3.forward * Mathf.Min(distance, 8));
            hand.Reticle.gameObject.SetActive(target && target.Available); hand.Reticle.position = touch ? touchTip : near ? tip : ray.GetPoint(Mathf.Max(.01f, distance - .02f));
            if (hasAim && lab.Placing && !target && !pointerBlocked) lab.PreviewPlacement(ray.GetPoint(lab.PointerReach(3)));
            if (target && target.Node) { lab.PreviewConnection(target.Node, ray.GetPoint(distance)); previewHasTarget = true; }
            else if (!previewHasTarget) lab.PreviewConnection(null, ray.GetPoint(lab.PointerReach(3)));
            float strength=device.pinchStrengthIndex.ReadValue();
            // Entering the touch zone cancels a pending ray click; disabling pinch can emit Released.
            if(touch) { state.PressTarget=null; state.PlaceOnRelease=false; }
            state.Pinch.Step(!touch, strength);
            hand.Reticle.localScale=Vector3.one*Mathf.Lerp(.014f,.028f,strength);
            hand.Reticle.GetComponent<Renderer>().sharedMaterial=LabVisuals.Material(state.Pinch.Pressed?LabVisuals.Green:LabVisuals.White);
            hand.Ray.widthMultiplier=state.Pinch.Pressed?.012f:.007f; // soft beam: the visible core is about half its width
            if (state.Pinch.Started || (lab.NetworkRoom != null && state.Pinch.Pressed && !hand.Held && !hand.HeldMenu && hand.PendingGrab))
            {
                if (state.Pinch.Started) hand.PendingGrab = null;
                state.PressTarget = target; state.PlaceOnRelease = hasAim && lab.Placing && !target && !pointerBlocked;
                if (state.Pinch.Started && target && target.Menu && target.Menu.TryGrab(hand, ray, distance, hand.Visual.rotation)) hand.HeldMenu = target.Menu;
                else if (hand.PendingGrab || target && target.Node && target.Port == 0)
                {
                    if (state.Pinch.Started)
                    {
                        target.Activate(); hand.PendingGrab = target.Node; hand.PendingNear = near == target.Node;
                        hand.PendingDistance = distance; hand.PendingOffset = target.Node.transform.position - (hand.PendingNear ? tip : ray.GetPoint(distance));
                    }
                    if (!lab.ConnectingMode && hand.PendingGrab && lab.BeginGrab(hand.PendingGrab))
                    {
                        hand.Held = hand.PendingGrab; state.NearGrab = hand.PendingNear; hand.Distance = hand.PendingDistance; hand.Offset = hand.PendingOffset;
                        hand.PendingGrab = null;
                    }
                    state.PressTarget = null;
                }
                else if (target && target.Port == 2) { target.Activate(); hand.DrawingPort = true; state.PressTarget = null; }
            }
            if (state.Pinch.Pressed)
            {
                if (hand.Held) hand.Held.transform.position = lab.ClampToTable((state.NearGrab && hasTip?tip:ray.GetPoint(hand.Distance)) + hand.Offset);
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
        void ResetFingers(TrackedHand state)
        {
            state.Fingers.Reset();
            for(int f=0;f<MultiFingerTouch.Fingers;f++) { Hover(ref state.FingerHover[f],null); state.FingerTargets[f]=null; ShowTouchCursor(state,f,null,0); }
        }
        // A small ring on the surface under each fingertip: it tightens as the finger approaches and turns green on
        // contact, so depth is readable before the press (there is no haptic feedback with bare hands).
        void ShowTouchCursor(TrackedHand state, int finger, LabTarget target, float front)
        {
            var cursor = state.Cursors[finger];
            if (!target)
            {
                if (cursor && cursor.enabled) cursor.enabled = false;
                if (state.Mesh) state.Mesh.SetTouch(finger, 0, false);
                return;
            }
            if (!cursor)
            {
                cursor = state.Cursors[finger] = LabVisuals.Ring(transform, Vector3.zero, 1, LabVisuals.Ice, .0016f, 28);
                cursor.name = "Fingertip touch cursor"; cursor.textureMode = LineTextureMode.Stretch;
            }
            float proximity = 1 - Mathf.Clamp01(front / .03f); bool pressing = front <= .004f;
            var surface = target.transform;
            cursor.transform.SetPositionAndRotation(state.FingerTips[finger] + surface.forward * (front - .0015f), surface.rotation * Quaternion.Euler(90, 0, 0));
            cursor.transform.localScale = Vector3.one * Mathf.Lerp(.012f, .0045f, proximity);
            cursor.sharedMaterial = LabVisuals.Beam(pressing ? LabVisuals.Green : LabVisuals.Ice, false);
            cursor.enabled = true;
            if (state.Mesh) state.Mesh.SetTouch(finger, proximity, pressing);
        }
    }
}
