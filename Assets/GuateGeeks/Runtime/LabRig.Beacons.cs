using UnityEngine;

namespace GuateGeeks.AwsVr
{
    public sealed partial class LabRig
    {
        readonly BeaconButton[] beacons = { new BeaconButton(), new BeaconButton() };
        RectTransform beaconLabel;
        TMPro.TMP_Text beaconText;
        float beaconLabelUntil;
        int beaconLabelArm = -1;
        public bool HandBusy(bool isLeft)
        {
            var hand = isLeft ? left : right;
            if (hand == null) return false;
            return hand.Held || hand.HeldMenu || hand.PendingGrab || hand.DrawingPort ||
                trackedHands.TryGetValue(hand, out var state) && state.Active &&
                (state.Pinch.Pressed || state.PressTarget || System.Array.Exists(state.FingerTargets, target => target != null));
        }
        public void PressBeacon(int arm)
        {
            if (arm < 0 || arm > 1) return;
            bool allowed = DigitalQuetzal.TogglePerch(arm);
            beaconLabelArm = arm; beaconLabelUntil = Time.unscaledTime + 2.5f;
            if (!beaconLabel)
            {
                beaconLabel = LabVisuals.Panel(transform, "Perch permission", Vector3.zero, new Vector2(580, 100), movable: false);
                beaconLabel.localScale = Vector3.one * .00035f;
                beaconText = LabVisuals.Text(beaconLabel, "", Vector2.zero, new Vector2(550, 85), 25, LabVisuals.White, TextAnchor.MiddleCenter);
            }
            beaconText.text = (arm == 0 ? "BRAZO IZQUIERDO" : "BRAZO DERECHO") + "\nQUETZAL · " + (allowed ? "POSARSE: SÍ" : "POSARSE: NO");
            if (lab && lab.Feedback) lab.Feedback.Play(LabFeedback.Success);
        }
        void UpdateBeacons()
        {
            for (int i = 0; i < 2; i++)
            {
                var host = i == 0 ? left : right; var other = i == 0 ? right : left;
                bool hostValid = trackedHands.TryGetValue(host, out var h) && h.Active && h.Mesh && h.Mesh.BeaconShown;
                bool otherValid = trackedHands.TryGetValue(other, out var o) && o.Active && o.Mesh && o.Mesh.BeaconShown && !HandBusy(i != 0);
                bool tipValid = otherValid && o.HasIndexTip;
                if (beacons[i].Step(hostValid && tipValid, tipValid ? o.FingerTips[1] : Vector3.zero,
                    hostValid ? h.Mesh.BeaconPosition : Vector3.zero, hostValid ? h.Mesh.BeaconNormal : Vector3.up, Time.unscaledTime))
                {
                    PressBeacon(i); o.PressTarget = null; o.PlaceOnRelease = false; o.Pinch.Step(false, 0);
                }
                if (hostValid)
                {
                    h.Mesh.SetBeacon(DigitalQuetzal.PerchAllowed(i), beacons[i].Proximity, beacons[i].Flash(Time.unscaledTime));
                    var quetzal = DigitalQuetzal.Active;
                    h.Mesh.SetPerch(quetzal ? quetzal.OfferProgress(i) : 0, DigitalQuetzal.PerchAllowed(i), quetzal && quetzal.PerchedOn(i));
                }
            }
            if (!beaconLabel) return;
            bool show = beaconLabelArm >= 0 && Time.unscaledTime < beaconLabelUntil;
            var hand = beaconLabelArm == 0 ? left : right;
            show &= hand != null && trackedHands.TryGetValue(hand, out var state) && state.Active;
            beaconLabel.gameObject.SetActive(show);
            if (show)
            {
                var p = trackedHands[hand].Mesh.BeaconPosition + Vector3.up * .1f;
                beaconLabel.SetPositionAndRotation(p, Quaternion.LookRotation(p - cam.transform.position, Vector3.up));
            }
        }
    }
}
