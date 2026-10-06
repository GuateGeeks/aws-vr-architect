using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // Ergonomic zones for hand-first VR. Interactive surfaces sit inside a comfortable reach envelope in front of
    // the chest, reading surfaces sit just below the eye line, and nothing needs a raised or fully extended arm.
    // Sizes follow common near-field guidance: touch targets of at least 2.2 cm, ~3.6 cm keyboard pitch and text
    // around 15 dmm (15 mm tall seen from 1 m, about 0.9°).
    public static class ComfortLayout
    {
        public const float EyeHeight = 1.65f;
        public const float MinTouchTarget = .022f;
        public const float KeyPitch = .036f;
        // Farthest centre of an interactive near-field surface from the eyes (elbow slightly bent).
        public const float ReachFromEyes = .72f;
        // Typing pose in the head's yaw frame: in front of the lower chest, tilted back toward the eyes.
        public static readonly Vector3 KeyboardOffset = new Vector3(0, -.36f, .42f);
        public const float KeyboardTilt = 38;
        // Desktop rehearsal has no hands: the keyboard sits low in the view where a monitor can show it.
        public static readonly Vector3 DesktopKeyboardOffset = new Vector3(0, -.30f, .82f);
        public const float DesktopKeyboardTilt = 20;

        public static Vector2 WorldSize(RectTransform rect)
        {
            if (!rect) return Vector2.zero;
            var s = rect.lossyScale; return new Vector2(rect.rect.width * Mathf.Abs(s.x), rect.rect.height * Mathf.Abs(s.y));
        }
        // Puts a keyboard at the typing pose of the current head and scales it so its keys are KeyPitch apart.
        // The keyboard is then world-locked: typing on a surface that moves with your head is not possible.
        public static void PlaceKeyboard(RectTransform keyboard, Transform head, bool xr, float pitchPixels)
        {
            if (!keyboard || !head) return;
            var yaw = Quaternion.Euler(0, head.eulerAngles.y, 0);
            keyboard.SetPositionAndRotation(head.position + yaw * (xr ? KeyboardOffset : DesktopKeyboardOffset),
                yaw * Quaternion.Euler(xr ? KeyboardTilt : DesktopKeyboardTilt, 0, 0));
            float parent = keyboard.parent ? Mathf.Abs(keyboard.parent.lossyScale.x) : 1;
            keyboard.localScale = Vector3.one * (KeyPitch / pitchPixels) / Mathf.Max(1e-4f, parent);
        }
    }
}
