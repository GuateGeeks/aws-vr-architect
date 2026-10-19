using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // How the quetzal moves its head, after the way birds do it:
    // - Fixate and snap: the head holds still in space on a fixation, then jumps to the next one in a saccade that
    //   settles in under a tenth of a second (a stiff spring, never a slow drift). A moving target is followed by a run
    //   of small jumps rather than smooth pursuit, and a perched bird's head stays put while its perch moves under it.
    // - Lateral eyes: a quetzal's eyes look out to the sides, so it inspects things by turning one eye toward them and
    //   switches eyes now and then; aiming an eye at something above or below tilts the head, which is the familiar
    //   head-cock. Close work (nibbling, preening, landing) and flight use the frontal view along the bill.
    // - Level: the head is posed in world space against the horizon, so a turn never corkscrews whatever the body's
    //   pitch (the perched body is tilted 62°), within a neck range measured from the body's heading.
    // Layered on top: bows and chin lifts under a stroking finger, the alarm duck, quick feather-settling head shakes,
    // nibble and preen jabs that reach out along the bill, and the raised bill of a call.
    public sealed partial class DigitalQuetzal
    {
        enum Eye { Front, Left, Right }
        // Optical axes in head space: each lateral eye looks out ~60° from the bill; the frontal view runs just under it.
        static readonly Vector3 LeftEyeAxis = new Vector3(-.866f, 0, .5f), RightEyeAxis = new Vector3(.866f, 0, .5f), FrontAxis = new Vector3(0, -.1f, 1).normalized;
        const float HeadShakeSeconds = .38f;
        Eye gazeEye;
        Vector3 gazePoint;
        Quaternion headWorld = Quaternion.identity;
        int gazeFocus = -1;
        float nextSaccade, headShake, headShakeSide = 1, pendingShake = -1, jabPhase, jab;
        bool hasHeadWorld;

        // monocular: may inspect with one eye; interest (0..1) shortens fixations and makes eye switches likelier;
        // continuous: smooth pursuit for flight, where the head stays aligned with the path.
        void Gaze(float dt, Vector3 target, int focus, bool monocular, float interest, bool continuous, float yawLimit = 110)
        {
            var from = head.position;
            if (!hasHeadWorld) { headWorld = head.rotation; gazePoint = target; hasHeadWorld = true; }
            if (continuous) { gazePoint = target; gazeEye = Eye.Front; gazeFocus = focus; }
            else if (focus != gazeFocus || elapsed >= nextSaccade || Vector3.Angle(target - from, gazePoint - from) > 8) Fixate(target, focus, monocular, interest);
            var goal = ClampNeck(OrientEye(gazePoint - from, gazeEye), yawLimit);
            headWorld = Quaternion.Slerp(headWorld, goal, Ease(continuous ? 9 : 34, dt));
        }
        void Fixate(Vector3 target, int focus, bool monocular, float interest)
        {
            var from = head.position; var to = target - from;
            bool fresh = focus != gazeFocus;
            var before = OrientEye(gazePoint - from, gazeEye);
            // Successive fixations land a little apart, as real ones do; that scatter is the only "idle" head motion.
            gazePoint = target + Random.insideUnitSphere * to.magnitude * Mathf.Tan(2.5f * Mathf.Deg2Rad);
            var eye = Eye.Front;
            if (monocular)
            {
                var heading = BodyHeading();
                var side = Vector3.Dot(to, Vector3.Cross(Vector3.up, heading)) >= 0 ? Eye.Right : Eye.Left;
                float off = Vector3.Angle(heading, Vector3.ProjectOnPlane(to, Vector3.up));
                if (off > 75) eye = side;                                                   // far round: only the near eye reaches
                else if (Random.value < Mathf.Lerp(.28f, .12f, interest)) eye = Eye.Front;  // a face-on look now and then
                else if (!fresh && gazeEye != Eye.Front && Random.value < Mathf.Lerp(.2f, .55f, interest)) eye = gazeEye == Eye.Left ? Eye.Right : Eye.Left;
                else eye = !fresh && gazeEye != Eye.Front ? gazeEye : side;
            }
            gazeEye = eye; gazeFocus = focus;
            nextSaccade = elapsed + Mathf.Lerp(1.6f, .35f, interest) * Random.Range(.6f, 1.5f);
            Saccades++;
            // Birds often blink with a large head saccade.
            if (Quaternion.Angle(before, OrientEye(gazePoint - from, gazeEye)) > 25 && Random.value < .35f) { blinkTime = 0; nextBlink = elapsed + Random.Range(2f, 5f); }
        }
        // The head orientation that puts the chosen eye's axis on the target with the head as level as that allows.
        Quaternion OrientEye(Vector3 direction, Eye eye)
        {
            if (direction.sqrMagnitude < 1e-8f) return headWorld;
            direction.Normalize();
            var up = Vector3.ProjectOnPlane(Vector3.up, direction);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(headWorld * Vector3.up, direction);
            var axis = eye == Eye.Left ? LeftEyeAxis : eye == Eye.Right ? RightEyeAxis : FrontAxis;
            return Quaternion.LookRotation(direction, up.normalized) * Quaternion.Inverse(Quaternion.LookRotation(axis, Vector3.up));
        }
        // Horizontal heading of the body: valid upright, level or pitched past vertical in a flare.
        Vector3 BodyHeading()
        {
            var h = Vector3.ProjectOnPlane(bird.forward - bird.up, Vector3.up);
            return h.sqrMagnitude > 1e-6f ? h.normalized : Vector3.ProjectOnPlane(bird.forward, Vector3.up).normalized;
        }
        Quaternion ClampNeck(Quaternion goal, float yawLimit)
        {
            var f = goal * Vector3.forward; var heading = BodyHeading();
            var flat = Vector3.ProjectOnPlane(f, Vector3.up);
            float elevation = Mathf.Asin(Mathf.Clamp(f.y, -1, 1)) * Mathf.Rad2Deg;
            float yaw = flat.sqrMagnitude > 1e-6f && heading.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(heading, flat, Vector3.up) : 0;
            float clampedYaw = Mathf.Clamp(yaw, -yawLimit, yawLimit), clampedElevation = Mathf.Clamp(elevation, -70, 60);
            if (Mathf.Abs(yaw - clampedYaw) < .01f && Mathf.Abs(elevation - clampedElevation) < .01f) return goal;
            var across = Quaternion.AngleAxis(clampedYaw, Vector3.up) * heading;
            var limited = across * Mathf.Cos(clampedElevation * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(clampedElevation * Mathf.Deg2Rad);
            return Quaternion.FromToRotation(f, limited) * goal;
        }
        void ShakeHead(float delay = 0) { if (delay > 0) pendingShake = elapsed + delay; else { headShake = HeadShakeSeconds; headShakeSide = Random.value < .5f ? -1 : 1; crestPulse = Mathf.Max(crestPulse, .5f); } }
        // Expressions over the gaze, then the neck and head joints: the neck takes a share of the turn and carries the
        // head's position (held in space on a moving perch, reaching out in a jab, pulled in when alarmed).
        void PoseHead(float dt, float neckShare, bool jabbing, bool stabilise)
        {
            if (pendingShake >= 0 && elapsed >= pendingShake) { pendingShake = -1; ShakeHead(); }
            float t = act == Act.None ? 0 : Mathf.Clamp01(actTime / actLength);
            float pitch = bow * 26 - chin * 28 + duck * 14 - (act == Act.Call ? Mathf.Sin(Mathf.PI * t) * 26 : 0);
            float roll = cock * 12, yaw = 0;
            // Rubbing its head against a scratching finger: small to-and-fro pushes with a roll.
            float rubWave = Mathf.Sin(elapsed * Mathf.PI * 2 * 2.2f);
            roll += rub * 11 * rubWave;
            if (headShake > 0)
            {
                float s = 1 - headShake / HeadShakeSeconds, envelope = Mathf.Sin(Mathf.PI * s), phase = s * HeadShakeSeconds * Mathf.PI * 2 * 13;
                roll += Mathf.Sin(phase) * 34 * envelope * headShakeSide; yaw += Mathf.Sin(phase + 1.2f) * 9 * envelope;
                headShake -= dt;
            }
            // Jabs: quick reaches along the bill, four a second, the bill opening at the end of each.
            if (jabbing) jabPhase += dt * Mathf.PI * 2 * 4.2f; else jabPhase = 0;
            jab = Mathf.MoveTowards(jab, jabbing ? Mathf.Pow(Mathf.Max(0, Mathf.Sin(jabPhase)), 2) : 0, dt * 30);
            var world = headWorld * Quaternion.Euler(pitch + jab * 6, yaw, roll);
            var local = Quaternion.Inverse(bird.rotation) * world;
            neck.localRotation = Quaternion.Slerp(Quaternion.identity, local, neckShare);
            head.localRotation = Quaternion.Inverse(neck.localRotation) * local;
            var nominal = bird.TransformPoint(QuetzalMesh.NeckBase);
            if (!hasStableNeck || !stabilise) { stableNeck = nominal; hasStableNeck = true; }
            else stableNeck = Vector3.Lerp(stableNeck, nominal, Ease(5.5f, dt));
            var drift = Vector3.ClampMagnitude(stableNeck - nominal, .03f * bird.lossyScale.x);
            var along = bird.InverseTransformDirection(world * Vector3.forward);
            float lunge = peck > 0 ? Mathf.Sin(Mathf.PI * (1 - peck / PeckSeconds)) : 0;
            var reach = along * (.013f * jab - .01f * duck + .024f * lunge);
            if (rub > 0) reach += bird.InverseTransformDirection((touchPoint - head.position).normalized) * (.009f * rub * (.5f + .5f * rubWave));
            neck.localPosition = Vector3.Lerp(neck.localPosition, QuetzalMesh.NeckBase + bird.InverseTransformVector(drift) + reach, Ease(40, dt));
        }
    }
}
