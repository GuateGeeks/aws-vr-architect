using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Hands;

namespace GuateGeeks.AwsVr
{
    // Reconstructed from the 0.24.4 public layout and native interaction paths.
    public struct QuetzalArm
    {
        public const int PalmPoint = 5, TouchPoints = 6;
        public bool Tracked, Articulated, Pointing, PointingGesture, Offered, HasFingertip;
        public Vector3 Wrist, Elbow, Dorsal, Fingertip, PointOrigin, PointDirection, Velocity;
        public bool Busy;
        public int TouchMask;
        public Vector3 Thumb, Middle, RingTip, Little, Palm, PalmFacing;
        // Index finger as a perch: its base knuckle and whether it is held straight.
        public Vector3 IndexKnuckle;
        public bool IndexExtended;
        public Vector3 Forearm => (Wrist - Elbow).sqrMagnitude > .000001f ? (Wrist - Elbow).normalized : Vector3.forward;
        public float Slope => Mathf.Abs(Vector3.Dot(Forearm, Vector3.up));
        public Vector3 IndexDirection => (Fingertip - IndexKnuckle).sqrMagnitude > .000001f ? (Fingertip - IndexKnuckle).normalized : Forearm;
        // A straight, roughly level index finger with a tracked tip can carry the bird.
        public bool FingerPerch => Tracked && HasFingertip && IndexExtended && Mathf.Abs(IndexDirection.y) < .6f;
        public Vector3 TouchPoint(int i)
        {
            switch (i) { case 0: return Thumb; case 1: return Fingertip; case 2: return Middle; case 3: return RingTip; case 4: return Little; default: return Palm; }
        }
        public void SetTouchPoint(int i, Vector3 p)
        {
            if (i < 0 || i >= TouchPoints) return;
            switch (i) { case 0: Thumb = p; break; case 1: Fingertip = p; break; case 2: Middle = p; break; case 3: RingTip = p; break; case 4: Little = p; break; default: Palm = p; break; }
            TouchMask |= 1 << i;
        }
    }

    public sealed class QuetzalSenses
    {
        public const float UpperArm = .29f, ForearmLength = .26f;
        static readonly XRHandJointID[] TouchJoints = { XRHandJointID.ThumbTip, XRHandJointID.IndexTip, XRHandJointID.MiddleTip, XRHandJointID.RingTip, XRHandJointID.LittleTip, XRHandJointID.Palm };
        public QuetzalArm Left, Right;
        public Vector3 Eye;
        public Quaternion EyeRotation;
        public bool HasEye;
        public bool Simulated { get; private set; }
        public Func<bool, bool> HandBusy;
        public Vector3 BodyForward { get; private set; } = Vector3.forward;
        readonly List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
        readonly Vector3[] joints = new Vector3[26];
        readonly bool[] valid = new bool[26];
        Vector3 lastLeft, lastRight, leftVelocity, rightVelocity;
        bool hadLeft, hadRight;
        public QuetzalArm Arm(int index) => index == 0 ? Left : Right;
        public void Simulate(Vector3 eye, Quaternion eyeRotation, QuetzalArm left, QuetzalArm right)
        {
            Simulated = HasEye = true; Eye = eye; EyeRotation = eyeRotation; Left = left; Right = right;
            SetForward();
        }
        public void StopSimulating() { Simulated = false; hadLeft = hadRight = false; }
        void SetForward()
        {
            var flat = Vector3.ProjectOnPlane(EyeRotation * Vector3.forward, Vector3.up);
            if (flat.sqrMagnitude > .001f) BodyForward = flat.normalized;
        }
        public void Tick(Camera cam, bool xr, float dt)
        {
            if (Simulated) return;
            HasEye = cam;
            if (!cam) { Left = Right = default; hadLeft = hadRight = false; return; }
            Eye = cam.transform.position; EyeRotation = cam.transform.rotation; SetForward();
            Left = xr ? Read(cam, true) : default;
            Right = xr ? Read(cam, false) : default;
            // Desktop rehearsal: hold 1/2 to offer the left/right arm.
            if (!xr && Keyboard.current != null)
            {
                if (Keyboard.current.digit1Key.isPressed) Left = Desktop(cam, true);
                if (Keyboard.current.digit2Key.isPressed) Right = Desktop(cam, false);
            }
            Finish(ref Left, ref lastLeft, ref leftVelocity, ref hadLeft, dt, true);
            Finish(ref Right, ref lastRight, ref rightVelocity, ref hadRight, dt, false);
        }
        void Finish(ref QuetzalArm arm, ref Vector3 last, ref Vector3 velocity, ref bool had, float dt, bool left)
        {
            if (!arm.Tracked) { had = false; velocity = Vector3.zero; arm.Offered = false; return; }
            var raw = had && dt > .00001f ? (arm.Wrist - last) / dt : Vector3.zero;
            velocity = Vector3.Lerp(velocity, Vector3.ClampMagnitude(raw, 8), 1 - Mathf.Exp(-16 * Mathf.Max(0, dt)));
            last = arm.Wrist; had = true; arm.Velocity = velocity;
            arm.Busy = HandBusy != null && HandBusy(left);
            arm.Offered = !arm.Busy && IsOffered(in arm, Eye);
        }
        QuetzalArm Desktop(Camera cam, bool left)
        {
            var p = cam.transform.position + BodyForward * .42f + Vector3.Cross(Vector3.up, BodyForward) * (left ? -.18f : .18f) - Vector3.up * .4f;
            var a = FromController(p, Quaternion.LookRotation(BodyForward), p, cam.transform.forward, true, left, Eye, BodyForward);
            a.Elbow = p - Vector3.Cross(Vector3.up, BodyForward) * (left ? -.24f : .24f);
            a.Dorsal = Vector3.up; return a;
        }
        QuetzalArm Read(Camera cam, bool left)
        {
            SubsystemManager.GetSubsystems(subsystems);
            var system = subsystems.Find(s => s.running);
            var hand = system == null ? default : left ? system.leftHand : system.rightHand;
            var aim = left ? MetaAimHand.left : MetaAimHand.right;
            if (aim != null && ((MetaAimFlags)aim.aimFlags.ReadValue() & (MetaAimFlags.SystemGesture | MetaAimFlags.MenuPressed)) != 0) return default;
            var origin = cam.transform.parent;
            if (hand.isTracked && hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm))
            {
                for (int i = 0; i < valid.Length; i++)
                {
                    valid[i] = hand.GetJoint(XRHandJointIDUtility.FromIndex(i)).TryGetPose(out var pose);
                    if (valid[i]) joints[i] = origin ? origin.TransformPoint(pose.position) : pose.position;
                }
                return FromHand(joints, valid, (origin ? origin.rotation : Quaternion.identity) * palm.rotation, left, Eye, BodyForward);
            }
            var device = left ? XRController.leftHand : XRController.rightHand;
            if (device == null || !device.isTracked.isPressed) return default;
            var position = device.devicePosition.ReadValue(); var rotation = device.deviceRotation.ReadValue();
            if (origin) { position = origin.TransformPoint(position); rotation = origin.rotation * rotation; }
            var trigger = device.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("triggerPressed");
            return FromController(position, rotation, position, rotation * Vector3.forward, trigger != null && trigger.isPressed, left, Eye, BodyForward);
        }
        public static QuetzalArm FromHand(Vector3[] joints, bool[] valid, Quaternion palm, bool left, Vector3 eye, Vector3 bodyForward)
        {
            int wrist = XRHandJointID.Wrist.ToIndex(), middle = XRHandJointID.MiddleProximal.ToIndex(), index = XRHandJointID.IndexTip.ToIndex();
            if (joints == null || valid == null || joints.Length < 26 || valid.Length < 26 || !valid[wrist] || !valid[middle]) return default;
            var direction = (joints[middle] - joints[wrist]).normalized;
            // The forearm follows the hand (the same estimate draws the perch gauntlet), so the bird stands on the glove.
            var a = new QuetzalArm { Tracked = true, Articulated = true, Wrist = joints[wrist],
                Elbow = ForearmElbow(joints[wrist], direction, left, eye, bodyForward), Dorsal = palm * Vector3.up,
                PalmFacing = palm * Vector3.down, PointDirection = direction, PointOrigin = joints[wrist] };
            for (int i = 0; i < TouchJoints.Length; i++) if (valid[TouchJoints[i].ToIndex()]) a.SetTouchPoint(i, joints[TouchJoints[i].ToIndex()]);
            a.HasFingertip = valid[index];
            if (a.HasFingertip)
            {
                var knuckle = joints[XRHandJointID.IndexProximal.ToIndex()];
                a.IndexKnuckle = knuckle;
                a.PointDirection = (a.Fingertip - knuckle).normalized; a.PointOrigin = a.Fingertip;
                bool indexExtended = Vector3.Distance(a.Fingertip, a.Wrist) > Vector3.Distance(knuckle, a.Wrist) + .035f;
                int intermediate = XRHandJointID.IndexIntermediate.ToIndex();
                bool straight = !valid[intermediate] || Vector3.Dot((joints[intermediate] - knuckle).normalized, a.PointDirection) > .85f;
                a.IndexExtended = indexExtended && straight;
                bool middleCurled = valid[XRHandJointID.MiddleTip.ToIndex()] && Vector3.Distance(a.Middle, a.Wrist) < Vector3.Distance(joints[middle], a.Wrist) + .035f;
                a.Pointing = a.PointingGesture = indexExtended && middleCurled;
            }
            a.Offered = IsOffered(in a, eye); return a;
        }
        public static QuetzalArm FromController(Vector3 position, Quaternion rotation, Vector3 aimOrigin, Vector3 aimDirection, bool trigger, bool left, Vector3 eye, Vector3 bodyForward)
        {
            var a = new QuetzalArm { Tracked = true, Wrist = position, Dorsal = rotation * Vector3.up,
                Elbow = EstimateElbow(position, rotation * Vector3.forward, left, eye, bodyForward),
                PointOrigin = aimOrigin, PointDirection = aimDirection.normalized, Pointing = trigger, PointingGesture = trigger,
                PalmFacing = rotation * Vector3.down };
            // Controllers offer a perch and a pointing ray, never fabricated fingertip touch points.
            a.Offered = IsOffered(in a, eye); return a;
        }
        public static Vector3 EstimateElbow(Vector3 wrist, Vector3 handDirection, bool left, Vector3 eye, Vector3 forward)
        {
            var side = Vector3.Cross(Vector3.up, forward).normalized * (left ? -1 : 1);
            var shoulder = eye - Vector3.up * .22f + side * .18f;
            var reach = wrist - shoulder;
            float length = Mathf.Clamp(reach.magnitude, .06f, UpperArm + ForearmLength - .005f);
            var axis = reach.sqrMagnitude > .00001f ? reach.normalized : forward;
            float along = (UpperArm * UpperArm - ForearmLength * ForearmLength + length * length) / (2 * length);
            float bend = Mathf.Sqrt(Mathf.Max(0, UpperArm * UpperArm - along * along));
            var down = Vector3.ProjectOnPlane(Vector3.down + side * .45f, axis).normalized;
            var elbow = shoulder + axis * along + down * bend;
            var fromWrist = elbow - wrist;
            return wrist + (fromWrist.sqrMagnitude > .00001f ? fromWrist.normalized : -handDirection.normalized) * ForearmLength;
        }
        // Tracked hands give no elbow: the forearm is taken mostly along the hand (a perch arm is held with a straight
        // wrist), corrected toward the arm-length estimate. Shared with ArmoredHandVisual's gauntlet.
        public const float HandAlongForearm = .6f;
        public static Vector3 ForearmElbow(Vector3 wrist, Vector3 handDirection, bool left, Vector3 eye, Vector3 bodyForward)
        {
            var estimate = (wrist - EstimateElbow(wrist, handDirection, left, eye, bodyForward)).normalized;
            var hand = handDirection.sqrMagnitude > 1e-8f ? handDirection.normalized : estimate;
            var axis = Vector3.Slerp(estimate, hand, HandAlongForearm);
            return wrist - (axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.forward) * ForearmLength;
        }
        public static bool IsOffered(in QuetzalArm arm, Vector3 eye)
        {
            return arm.Tracked && !arm.Busy && arm.Slope < .6f && arm.Dorsal.y > .25f &&
                arm.Velocity.magnitude < .75f && Vector3.Distance(arm.Wrist, eye) < 1.15f &&
                arm.Wrist.y < eye.y - .12f && arm.Wrist.y > eye.y - 1;
        }
    }
}
