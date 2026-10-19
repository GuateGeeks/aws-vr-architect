using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // How the quetzal lives with the person (the decorative patrol is in DigitalQuetzal.cs).
    // - Calling: hold a forearm out level with the back of the hand up (or point at the bird). It answers with a call,
    //   takes off and flies a curved approach that ends just beyond the arm, then flares (body upright, wings braking
    //   over the back, legs reaching forward) and lands on top of the forearm with a little crouch before folding its
    //   wings. Its size eases from the hologram flight size to life size on the way, so it never pops.
    // - Perched: it stands upright facing the person, gripping the arm with its toes. Its head stays still in space
    //   while the arm moves, as birds' heads do; it tips with the arm's accelerations and recovers, flutters to keep its
    //   balance, and takes off if the arm turns steep or moves fast. Left alone it fluffs and shakes, flicks its tail,
    //   stretches a wing, preens, looks around and calls.
    // - Hands: stroking the crown, back or breast relaxes it (eyes half closed, leaning into the hand, soft coos), the
    //   chin lifts for a scratch, a finger at the bill gets nibbled, a touched wing flicks, fingertips and forearms push
    //   the streamers aside, and a fingertip coming close gets a cocked head and a curious look. A straight, level index
    //   finger held against its lower breast makes it step up onto the finger; the other forearm held out nearby makes
    //   it hop across. A fast swipe startles it away.
    // - Pointing at the table with the free hand sends it to look there; then it comes back to the arm.
    // With no arm offered it rests on the hologram projector and now and then flies its patrol.
    public sealed partial class DigitalQuetzal
    {
        public enum Mode { Patrol, Resting, Approach, Landing, Perched, Excursion, Leaving }
        public enum PerchSite { Forearm, Finger }
        enum Zone { None, Bill, Crown, Chin, Back, Breast, LeftWing, RightWing, Tail }
        enum Act { None, Fluff, TailBob, Stretch, Preen, LookAround, Call, Turn, Wag }
        public const string PerchPreference = "GuateGeeks.Quetzal.Perch.v1.";
        // Life size on the arm; upright perching posture (degrees above horizontal); how long an offer is held.
        public const float PerchSize = 1.05f, PerchPitch = 62, OfferSeconds = 1, StepUpSeconds = .6f;
        public static readonly Vector3 ProjectorPoint = new Vector3(-1.15f, .86f, 2.65f);
        public static readonly Vector3 RestContact = ProjectorPoint + Vector3.up * .035f;
        public static readonly Color BracerColor = new Color(.16f, .88f, .77f, 1);
        public static DigitalQuetzal Active { get; private set; }
        static readonly bool[] perchAllowed = { true, true };
        static bool perchLoaded;
        readonly QuetzalSenses senses = new QuetzalSenses();
        readonly QuetzalArm[] arms = new QuetzalArm[2];
        readonly float[] offerHeld = new float[2], aimedAt = new float[2], spotHeld = new float[2], stepHeld = new float[2];
        readonly Vector3[] spots = new Vector3[2];
        readonly bool[] offerArmed = { true, true };
        // Hand colliders for the streamers, refreshed every frame.
        readonly Vector3[] handPoints = new Vector3[QuetzalArm.TouchPoints * 2], forearmFrom = new Vector3[2], forearmTo = new Vector3[2];
        int handPointCount, forearmCount;
        Transform neck, feet, crest;
        Transform[] crestFeathers, eyes;
        LineRenderer projector, restBar, bracer;
        LabRig rig;
        Camera viewCamera;
        // Body pose and motion in lab space.
        Vector3 position, velocity, accel, perchContact, perchHeading = Vector3.back, perchNormal = Vector3.up, excursionTarget, touchPoint;
        Quaternion bodyRotation = Quaternion.identity;
        // Grip used to place a standing bird and plant its feet this frame.
        Vector3 gripContact, gripHeading = Vector3.back, gripNormal = Vector3.up, targetContact;
        // Flight: a quadratic curve from flightFrom through flightControl to a (moving) end; landings are Hermite arcs.
        Vector3 flightFrom, flightControl, landFrom, landVelocity, lastContact, contactVelocity, contactAccel, stableNeck, flightHeading = Vector3.back, actLook;
        Quaternion landRotation;
        float flightTime, flightDuration = 1, landTime, landDuration = 1, landScale = PerchSize, scale = PerchSize, flapDrive = 1, brakeDrive, legs = 1, crouch, takeoff = 1, touchdown, approachWait, hoverTime;
        bool landHop, finalApproach, grounded, hasLastContact, hasStableNeck, shooFlight;
        float modeTime, elapsed, reveal, highlight, contentment, bow, duck, chin, crestRaise, settled, beak, cock, cockSide = 1, nibble, balanceFlap, boredom, fingerNear, fluff;
        float nextBlink = 2, blinkTime = 1, nextAct = 3, actTime, actLength = 1, touchLost, lastTap = -10, nextCoo;
        float petGlow, leaveCooldown, perchLost, steepTime;
        // Body tilt is a damped spring (degrees: x pitch back, y roll left): the perch's accelerations and touches kick it,
        // and it wobbles back.
        Vector2 tilt, tiltVelocity, petLean;
        // Facing on the perch changes in little hop-turns; hop is the lift of the current one.
        Vector3 standHeading = Vector3.back, turnFrom;
        float hop;
        // Touch moods: annoyance from pokes (a peck back, then a shoo), stroking, head rubs, a palm held over the bird.
        float annoyance, pendingPeck = -1, peck, rub, strokeTime, palmOver, greetAt = -1, crestPulse;
        readonly float[] wingFlickTime = { 9, 9 }, wingFlickAmount = new float[2], wingHold = new float[2];
        readonly float[] crestAngle = new float[QuetzalMesh.CrestSegments], crestSpeed = new float[QuetzalMesh.CrestSegments];
        Act act; int actSide = 1;
        int perchArm = -1, approachSide, touchArm = -1;
        PerchSite perchSite, approachSite;
        Zone touchZone;
        bool preview, built, mutedPreview;
        public Mode State { get; private set; }
        public bool Perched => State == Mode.Perched;
        public int PerchArm => perchArm;
        public PerchSite Site => perchSite;
        public Vector3 PerchContact => perchContact;
        // Where the feet grip: below the hip by the leg (the feet part hangs from the hip).
        public Vector3 FootContact => feet ? feet.position - feet.rotation * Leg * bird.lossyScale.x : bird ? bird.position : transform.position;
        public Transform Neck => neck;
        public Transform Feet => feet;
        public Transform Crest => crest;
        public Transform[] CrestFeathers => crestFeathers;
        public QuetzalSenses Senses => senses;
        public float Reveal => reveal;
        public float Highlight => highlight;
        public float Contentment => contentment;
        public float Bowing => bow;
        public float Ducking => duck;
        public float ChinLift => chin;
        public float CrestRaise => crestRaise;
        public float Settled => settled;
        public float Scale => scale;
        public float BracerAlpha => bracer && bracer.enabled ? 1 : 0;
        public string Touching => touchZone.ToString();
        public string Activity => act.ToString();
        public string Looking { get; private set; } = "Ahead";
        public Vector3 LookPoint { get; private set; }
        public int Pets { get; private set; }
        public int Taps { get; private set; }
        public int Pecks { get; private set; }
        public int Flinches { get; private set; }
        public int Shoos { get; private set; }
        public int Saccades { get; private set; }
        public int StepUps { get; private set; }
        public Camera ViewCamera { get => viewCamera ? viewCamera : Camera.main; set { viewCamera = value; } }
        // For the glove: how far an offer has come (1 while the bird is coming to, or standing on, that forearm).
        public float OfferProgress(int arm)
        {
            if (arm < 0 || arm > 1) return 0;
            if ((State == Mode.Approach && approachSide == arm && approachSite == PerchSite.Forearm) || PerchedOn(arm)) return 1;
            return Mathf.Clamp01(offerHeld[arm] / OfferSeconds);
        }
        public bool PerchedOn(int arm) => (Perched || State == Mode.Landing) && perchArm == arm && perchSite == PerchSite.Forearm;
        public static bool PerchAllowed(int arm)
        {
            LoadPerch(); return arm >= 0 && arm < 2 && perchAllowed[arm];
        }
        static void LoadPerch()
        {
            if (perchLoaded) return;
            for (int i = 0; i < 2; i++) perchAllowed[i] = PlayerPrefs.GetInt(PerchPreference + i, 1) != 0;
            perchLoaded = true;
        }
        public static void ReloadPerchPreferences() { perchLoaded = false; LoadPerch(); }
        public static void SetPerchAllowed(int arm, bool allowed, bool remember = true)
        {
            if (arm < 0 || arm > 1) return;
            LoadPerch(); perchAllowed[arm] = allowed;
            if (remember) { PlayerPrefs.SetInt(PerchPreference + arm, allowed ? 1 : 0); PlayerPrefs.Save(); }
        }
        public static bool TogglePerch(int arm) { bool value = !PerchAllowed(arm); SetPerchAllowed(arm, value); return value; }
        void BuildBehaviour()
        {
            Active = this;
            neck = new GameObject("Flexible neck").transform; neck.SetParent(bird, false); neck.localPosition = QuetzalMesh.NeckBase;
            Part(neck, "Neck plumage", QuetzalMesh.Neck(), Vector3.zero);
            head.SetParent(neck, false); head.localPosition = QuetzalMesh.HeadOnNeck;
            feet = Part(bird, "Gripping feet", QuetzalMesh.Feet(), QuetzalMesh.Hip);
            crest = new GameObject("Expressive crest").transform; crest.SetParent(head, false);
            crestFeathers = new Transform[QuetzalMesh.CrestSegments];
            for (int i = 0; i < crestFeathers.Length; i++) crestFeathers[i] = Part(crest, "Crest segment " + i, QuetzalMesh.CrestSegment(i), QuetzalMesh.CrestAnchor(i));
            eyes = new[] { Part(head, "Left eye", QuetzalMesh.Eye(-1), QuetzalMesh.EyeCenter(-1)), Part(head, "Right eye", QuetzalMesh.Eye(1), QuetzalMesh.EyeCenter(1)) };
            projector = LabVisuals.Ring(transform, ProjectorPoint - Vector3.up * .01f, .12f, LabVisuals.Cyan, .003f, 48);
            projector.name = "Quetzal hologram projector";
            // A short bar of light on the projector for the resting bird to grip.
            restBar = LabVisuals.Line(transform, "Quetzal light perch", new[] { RestContact, RestContact }, LabVisuals.Cyan, .007f);
            restBar.sharedMaterial = LabVisuals.Beam(LabVisuals.Cyan, false);
            bracer = LabVisuals.Ring(transform, Vector3.zero, .045f, BracerColor, .002f, 40); bracer.name = "Quetzal forearm perch"; bracer.enabled = false;
            BuildVoice();
            built = true; Materialize();
        }
        public void Materialize()
        {
            if (!built) return;
            preview = mutedPreview = false; reveal = 0; velocity = accel = Vector3.zero; perchArm = -1;
            touchZone = Zone.None; touchArm = -1; perchLost = steepTime = 0; act = Act.None; takeoff = legs = 1; crouch = touchdown = hop = 0;
            scale = PerchSize; hasLastContact = hasStableNeck = hasHeadWorld = false; tilt = tiltVelocity = petLean = Vector2.zero;
            annoyance = 0; pendingPeck = greetAt = -1; Glitch(.8f, .35f);
            perchContact = gripContact = RestContact; perchHeading = gripHeading = standHeading = RestHeading(); perchNormal = gripNormal = Vector3.up;
            bodyRotation = PerchedPose(perchHeading); position = BodyOnPerch(perchContact, perchHeading, perchNormal, bodyRotation, scale);
            Enter(Mode.Resting); SetVisible(true); flying = false; resetStreamers = true;
        }
        void SetPatrolPreview() { preview = mutedPreview = true; State = Mode.Patrol; reveal = 1; }
        public void PreviewPerch(int arm)
        {
            if (!built || arm < 0 || arm > 1) return;
            preview = mutedPreview = true; perchArm = arm; perchSite = PerchSite.Forearm; Enter(Mode.Perched); reveal = 1; SetVisible(true); resetStreamers = true;
            var a = senses.Arm(arm);
            if (a.Tracked) PerchFrom(in a, PerchSite.Forearm, out perchContact, out perchHeading, out perchNormal);
            else { perchContact = RestContact; perchHeading = RestHeading(); perchNormal = Vector3.up; }
            standHeading = perchHeading;
            bodyRotation = PerchedPose(perchHeading); scale = PerchSize; legs = 1; takeoff = 1;
            position = BodyOnPerch(perchContact, perchHeading, perchNormal, bodyRotation, scale);
        }
        void Enter(Mode mode) { State = mode; modeTime = 0; settled = 0; }
        void Update()
        {
            if (!built) return;
            if (LabFeedback.Current && LabFeedback.Current.ReducedMotion)
            {
                flying = false;
                if (bird.gameObject.activeSelf) SetVisible(false);
                if (bracer) bracer.enabled = false;
                if (projector) projector.enabled = false;
                if (restBar) restBar.enabled = false;
                voice.Stop(); return;
            }
            if (!preview && !bird.gameObject.activeSelf) Materialize();
            var cam = ViewCamera;
            if (!rig && cam) { rig = cam.GetComponentInParent<LabRig>(); if (rig) senses.HandBusy = rig.HandBusy; }
            senses.Tick(cam, rig && rig.IsXR, Mathf.Min(Time.unscaledDeltaTime, .05f));
            Step(Mathf.Min(Time.unscaledDeltaTime, .05f), cam);
        }
        public void Advance(float seconds)
        {
            if (!built || seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            int n = Mathf.CeilToInt(Mathf.Min(seconds, 60) / .02f); float dt = Mathf.Min(seconds, 60) / n;
            for (int i = 0; i < n; i++) Step(dt, ViewCamera);
        }
        Vector3 Lab(Vector3 world) => transform.parent ? transform.parent.InverseTransformPoint(world) : world;
        Vector3 World(Vector3 lab) => transform.parent ? transform.parent.TransformPoint(lab) : lab;
        Vector3 LabDirection(Vector3 world) => transform.parent ? transform.parent.InverseTransformDirection(world) : world;
        Quaternion ParentRotation => transform.parent ? transform.parent.rotation : Quaternion.identity;
        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        static float Ease(float rate, float dt) => 1 - Mathf.Exp(-rate * dt);

        void Step(float dt, Camera cam)
        {
            elapsed += dt; modeTime += dt; leaveCooldown = Mathf.Max(0, leaveCooldown - dt);
            reveal = Mathf.MoveTowards(reveal, 1, dt / .75f);
            UpdateHologram(dt);
            if (!preview) Perceive(dt);
            if (State == Mode.Patrol)
            {
                StepPatrol(dt, cam);
                if (bird.gameObject.activeSelf) { AnimateFace(dt); ApplyMaterial(); }
                projector.enabled = restBar.enabled = bracer.enabled = false; return;
            }
            SetVisible(true);
            grounded = false;
            switch (State)
            {
                case Mode.Resting: StepResting(dt); break;
                case Mode.Approach: StepApproach(dt); break;
                case Mode.Landing: StepLanding(dt); break;
                case Mode.Perched: StepPerched(dt); break;
                case Mode.Excursion: StepExcursion(dt); break;
                case Mode.Leaving: StepLeaving(dt); break;
            }
            flying = !grounded;
            // Size eases toward the hologram flight size only when the bird is far from the person, never at the arm.
            if (!grounded && State != Mode.Landing && !(State == Mode.Leaving && finalApproach))
            {
                float distance = senses.HasEye ? Vector3.Distance(World(position), senses.Eye) : 2;
                scale = Mathf.Lerp(scale, Mathf.Lerp(PerchSize, NearSize, Smooth(1.1f, 2.6f, distance)), Ease(2.5f, dt));
            }
            else if (grounded) scale = Mathf.MoveTowards(scale, PerchSize, dt * .5f);
            // Fluffed when shaking or content; a standing bird breathes (a slight, quick swell of the body).
            float shown = scale * (1 + fluff * .07f + contentment * .025f + (grounded ? .007f * Mathf.Sin(elapsed * Mathf.PI * 2 * 1.25f) : 0));
            if (grounded) position = BodyOnPerch(gripContact, gripHeading, gripNormal, bodyRotation, shown) + Vector3.up * hop;
            bird.position = World(position); bird.rotation = ParentRotation * bodyRotation; bird.localScale = Vector3.one * shown;
            voice.transform.position = head.position;
            PoseFeet(legs * (1 - Mathf.Clamp01(hop / .02f) * .3f), dt);
            if (!grounded) { tuck = Mathf.MoveTowards(tuck, 0, dt * 8); AnimateWings(dt, flapDrive, brakeDrive, Ease(30, dt)); }
            else if (takeoff < 1) { tuck = Mathf.MoveTowards(tuck, 0, dt * 8); AnimateWings(dt, .55f, .5f, Ease(14, dt)); }
            else if (balanceFlap > 0 || touchdown > .55f) { tuck = Mathf.MoveTowards(tuck, 0, dt * 8); AnimateWings(dt, .5f, .45f, Ease(16, dt)); }
            else FoldWings(dt);
            if (!preview) Touch(dt);
            AnimateInteractiveHead(dt); AnimateFace(dt);
            if ((bird.position - lastPosition).sqrMagnitude > 1) resetStreamers = true;
            lastPosition = bird.position; SimulateStreamers(dt, shown);
            ShowPerchLights();
            ApplyMaterial();
        }
        void ShowPerchLights()
        {
            bool resting = State == Mode.Resting || State == Mode.Leaving && finalApproach;
            projector.enabled = resting; restBar.enabled = State == Mode.Resting;
            if (restBar.enabled)
            {
                var centre = RestContact - Vector3.up * QuetzalMesh.GripRadius * scale; var across = Vector3.Cross(Vector3.up, gripHeading).normalized * .055f;
                restBar.SetPosition(0, centre - across); restBar.SetPosition(1, centre + across);
            }
            // A pulsing band round the forearm marks where the bird is coming to land. Once it stands there the band stays
            // only for controllers; with tracked hands the gauntlet's lit rail shows it.
            var a = senses.Arm(State == Mode.Approach ? approachSide : perchArm);
            bool coming = State == Mode.Approach && approachSite == PerchSite.Forearm || State == Mode.Landing && perchSite == PerchSite.Forearm;
            bracer.enabled = a.Tracked && (coming || Perched && perchSite == PerchSite.Forearm && !a.Articulated);
            if (bracer.enabled)
            {
                var axis = a.Forearm;
                bracer.transform.SetPositionAndRotation(a.Wrist - axis * .075f, Quaternion.FromToRotation(Vector3.up, axis));
                bracer.transform.localScale = Vector3.one * (Perched ? 1 : 1 + .12f * Mathf.Sin(elapsed * 7));
            }
        }

        // ---- Perception and decisions ----
        void Perceive(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                arms[i] = senses.Arm(i); var a = arms[i];
                bool available = PerchAllowed(i) && a.Tracked && !a.Busy;
                bool carrying = (Perched || State == Mode.Landing) && perchArm == i;
                bool aimed = available && a.Pointing && RayNear(a.PointOrigin, a.PointDirection, bird.position, .22f);
                aimedAt[i] = aimed && !carrying ? aimedAt[i] + dt : 0;
                // An arm the bird has just left must be lowered before it offers again, so it does not hop straight back;
                // a hand reaching to the bird (petting, offering a finger) is not offering its forearm.
                if (!a.Offered) offerArmed[i] = true;
                offerHeld[i] = available && a.Offered && offerArmed[i] && !carrying && !Reaching(in a) ? offerHeld[i] + dt : 0;
                // Step-up: a straight, level index finger held still against the lower breast.
                bool stepping = available && (Perched && i != perchArm || State == Mode.Resting) && a.FingerPerch && a.Velocity.magnitude < .35f &&
                    Vector3.Distance(Vector3.Lerp(a.IndexKnuckle, a.Fingertip, .45f), StepPoint) < .075f;
                stepHeld[i] = stepping ? stepHeld[i] + dt : 0;
                if (available && a.PointingGesture && !aimed && a.PointDirection.y < -.12f)
                {
                    float d = (World(new Vector3(0, .75f, 0)).y - a.PointOrigin.y) / a.PointDirection.y;
                    var point = Lab(a.PointOrigin + a.PointDirection * d);
                    if (d > .1f && d < 3 && point.x > -1.6f && point.x < 1.6f && point.z > 1.65f && point.z < 3.65f)
                    { spotHeld[i] = Vector3.Distance(spots[i], point) < .1f ? spotHeld[i] + dt : 0; spots[i] = point; }
                    else spotHeld[i] = 0;
                }
                else spotHeld[i] = 0;
                bool callable = leaveCooldown <= 0 && (State == Mode.Resting || State == Mode.Patrol || State == Mode.Leaving && takeoff >= 1 && !finalApproach);
                if (callable && (offerHeld[i] > OfferSeconds || aimedAt[i] > .65f)) { BeginApproach(i); VoiceCall(); crestPulse = 1; break; }
                if (stepHeld[i] > StepUpSeconds && PerchFrom(in a, PerchSite.Finger, out _, out _, out _)) { BeginLanding(i, PerchSite.Finger, true); break; }
                if (Perched && i != perchArm && offerHeld[i] > OfferSeconds * .8f && Vector3.Distance(bird.position, a.Wrist) < .55f &&
                    PerchFrom(in a, PerchSite.Forearm, out _, out _, out _)) { BeginLanding(i, PerchSite.Forearm, true); break; }
                if (Perched && i != perchArm && spotHeld[i] > .8f) { BeginExcursion(spots[i] + Vector3.up * .42f); break; }
            }
            highlight = Mathf.MoveTowards(highlight, Mathf.Max(aimedAt[0], aimedAt[1]) > .1f || Mathf.Max(offerHeld[0], offerHeld[1]) > .15f ? 1 : 0, dt * 3);
        }
        Vector3 StepPoint => bird.TransformPoint(new Vector3(0, -.058f, .062f));
        bool Reaching(in QuetzalArm a)
        {
            if (!bird.gameObject.activeSelf) return false;
            for (int finger = 0; finger < QuetzalArm.TouchPoints; finger++)
                if ((a.TouchMask & (1 << finger)) != 0 && Vector3.Distance(a.TouchPoint(finger), bird.position) < .2f * bird.lossyScale.x) return true;
            return false;
        }
        static bool RayNear(Vector3 origin, Vector3 direction, Vector3 point, float radius)
        {
            if (direction.sqrMagnitude < .0001f) return false;
            var ray = new Ray(origin, direction.normalized); float d = Vector3.Dot(point - origin, ray.direction);
            return d > 0 && d < 4 && Vector3.Distance(point, ray.GetPoint(d)) < radius;
        }
        // Where the bird stands on an arm, in lab space: on top of the forearm (whatever its roll) a hand's width behind
        // the wrist, on the gauntlet's perch rail when the back of the hand is up; or on top of a straight index finger.
        bool PerchFrom(in QuetzalArm a, PerchSite site, out Vector3 contact, out Vector3 heading, out Vector3 normal)
        {
            bool valid; Vector3 top;
            if (site == PerchSite.Finger)
            {
                var axis = a.IndexDirection; top = Vector3.ProjectOnPlane(Vector3.up, axis); top = top.sqrMagnitude > .01f ? top.normalized : Vector3.up;
                contact = Lab(Vector3.Lerp(a.IndexKnuckle, a.Fingertip, .45f) + top * .0105f);
                valid = a.FingerPerch && !a.Busy && a.Velocity.magnitude < 1.5f;
            }
            else
            {
                var axis = a.Forearm; top = Vector3.ProjectOnPlane(Vector3.up, axis); top = top.sqrMagnitude > .01f ? top.normalized : Vector3.up;
                var lateral = Vector3.Cross(axis, a.Dorsal); lateral = lateral.sqrMagnitude > 1e-6f ? lateral.normalized : Vector3.Cross(axis, top);
                float radius = Mathf.Lerp(.036f, .033f, Mathf.Abs(Vector3.Dot(top, lateral)));
                contact = Lab(a.Wrist - axis * .075f + top * radius);
                valid = a.Tracked && !a.Busy && a.Slope < .78f && a.Velocity.magnitude < 1.5f;
            }
            normal = LabDirection(top).normalized;
            heading = Vector3.ProjectOnPlane(senses.HasEye ? Lab(senses.Eye) - contact : -LabDirection(senses.BodyForward), Vector3.up);
            heading = heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector3.back;
            return a.Tracked && valid;
        }
        Vector3 RestHeading()
        {
            var heading = senses.HasEye ? Vector3.ProjectOnPlane(Lab(senses.Eye) - RestContact, Vector3.up) : Vector3.back;
            return heading.sqrMagnitude > 1e-4f ? heading.normalized : Vector3.back;
        }
        Vector3 RestApproach() { var h = RestHeading(); return RestContact - h * .3f + Vector3.up * .22f; }

        // ---- Posture ----
        static Quaternion GripFrame(Vector3 heading, Vector3 normal)
        {
            var forward = Vector3.ProjectOnPlane(heading, normal);
            return Quaternion.LookRotation(forward.sqrMagnitude > 1e-6f ? forward : Vector3.Cross(Vector3.right, normal), normal);
        }
        Vector3 Leg => new Vector3(QuetzalMesh.FootHip.x, QuetzalMesh.FootHip.y * (1 - .5f * crouch), QuetzalMesh.FootHip.z);
        // The body placed so that its hip sits on the legs standing on the grip.
        Vector3 BodyOnPerch(Vector3 contact, Vector3 heading, Vector3 normal, Quaternion body, float size) =>
            contact + GripFrame(heading, normal) * Leg * size - body * QuetzalMesh.Hip * size;
        // Upright, facing the person, with the leans and gestures of the moment layered on.
        Quaternion PerchedPose(Vector3 heading)
        {
            float t = act == Act.None ? 0 : Mathf.Clamp01(actTime / actLength), envelope = Mathf.Sin(Mathf.PI * t);
            float bob = act == Act.TailBob ? Mathf.Sin(actTime * Mathf.PI * 2 * 2.6f) * 7 * envelope : 0;
            float shake = act == Act.Fluff && t > .3f && t < .8f ? Mathf.Sin(actTime * Mathf.PI * 2 * 9) * 11 * Mathf.Sin(Mathf.PI * (t - .3f) / .5f) : 0;
            // A wag swings the tail (and its streamers) from side to side.
            float wag = act == Act.Wag ? Mathf.Sin(actTime * Mathf.PI * 2 * 5.5f) * 9 * envelope : 0;
            // A slow, slight sway keeps the body alive under the still head.
            float swayPitch = Mathf.Sin(elapsed * .61f) * .9f, swayRoll = Mathf.Sin(elapsed * .47f + 1.1f) * 1.3f;
            float pitch = PerchPitch + tilt.x + petLean.x + bob + swayPitch - crouch * 9 - (act == Act.Call ? envelope * 8 : 0);
            return Quaternion.LookRotation(heading, Vector3.up) * Quaternion.Euler(-pitch, wag, tilt.y + petLean.y + shake + swayRoll);
        }
        // The tilt spring: driven toward a goal and kicked by impulses, lightly underdamped so the body wobbles back.
        void SpringTilt(Vector2 goal, float dt)
        {
            tiltVelocity += ((goal - tilt) * 150 - tiltVelocity * 12) * dt;
            tilt += tiltVelocity * dt;
            tilt = Vector2.ClampMagnitude(tilt, 32);
        }
        // Facing the person on the perch: small differences are taken up by shuffling the feet; a large one by a little
        // hop-turn, a lift with a flick of the wings that lands facing the new way.
        void TurnToward(Vector3 heading, float dt)
        {
            if (act == Act.Turn)
            {
                float t = Mathf.Clamp01(actTime / actLength);
                standHeading = Vector3.Slerp(turnFrom, heading, Smooth(.1f, .85f, t)).normalized;
                hop = Mathf.Sin(Mathf.PI * t) * .018f;
                return;
            }
            hop = Mathf.MoveTowards(hop, 0, dt * .2f);
            float angle = Vector3.Angle(standHeading, heading);
            // Turning to keep the person in view comes before any idle gesture.
            if (angle > 38 && touchZone == Zone.None && act != Act.Wag && balanceFlap <= 0)
            {
                act = Act.Turn; actTime = 0; actLength = .34f; turnFrom = standHeading;
                FlickWing(0, .35f); FlickWing(1, .35f);
            }
            else if (angle > 8) standHeading = Vector3.RotateTowards(standHeading, heading, 28 * Mathf.Deg2Rad * dt, 0).normalized;
        }
        static readonly Quaternion TuckedFeet = Quaternion.LookRotation(new Vector3(0, -1, .35f).normalized, new Vector3(0, .35f, 1).normalized);
        // Legs tucked under the belly in flight, reaching for the perch while landing, planted when standing.
        void PoseFeet(float extend, float dt)
        {
            var tucked = bird.rotation * TuckedFeet;
            var planted = ParentRotation * GripFrame(gripHeading, gripNormal);
            var rotation = extend <= 0 ? tucked : extend >= 1 ? planted : Quaternion.Slerp(tucked, planted, extend);
            // The feet hang from the hip; standing, the body is placed so that the toes close around the grip.
            feet.SetPositionAndRotation(bird.TransformPoint(QuetzalMesh.Hip), rotation);
        }

        // ---- Standing ----
        void Stand(Vector3 contact, Vector3 heading, Vector3 normal, float dt, float turn = 12)
        {
            grounded = true; gripContact = contact; gripHeading = heading; gripNormal = normal; legs = 1;
            bodyRotation = Quaternion.Slerp(bodyRotation, PerchedPose(heading), Ease(turn, dt));
        }
        void StepResting(float dt)
        {
            IdleActs(dt);
            TurnToward(RestHeading(), dt); perchHeading = standHeading;
            Stand(RestContact, perchHeading, Vector3.up, dt, 10);
            touchdown = Mathf.MoveTowards(touchdown, 0, dt * 2); crouch = Mathf.Lerp(crouch, Mathf.Max(touchdown > 0 ? Mathf.Sin(Mathf.PI * (1 - touchdown)) * .8f : 0, palmOver * .8f), Ease(20, dt));
            SpringTilt(Vector2.zero, dt);
            if (!preview && modeTime > RestSeconds && act == Act.None) { Enter(Mode.Patrol); clock = 0; resetStreamers = true; }
        }
        void StepPerched(float dt)
        {
            var a = senses.Arm(perchArm);
            bool valid = PerchFrom(in a, perchSite, out var contact, out var heading, out var normal);
            perchLost = valid ? 0 : perchLost + dt;
            steepTime = perchSite == PerchSite.Forearm && a.Slope > .75f ? steepTime + dt : 0;
            if (!preview && (!PerchAllowed(perchArm) || perchLost > .2f || steepTime > .25f || a.Velocity.magnitude > 1.5f))
            {
                // A finger that curls or drops: hop back to that forearm if it can take the bird, otherwise fly.
                if (perchSite == PerchSite.Finger && PerchAllowed(perchArm) && a.Velocity.magnitude < 1.2f && PerchFrom(in a, PerchSite.Forearm, out _, out _, out _))
                    BeginLanding(perchArm, PerchSite.Forearm, true);
                else { Flinches += a.Velocity.magnitude > 1.5f ? 1 : 0; BeginLeaving(); }
                return;
            }
            if (valid)
            {
                perchContact = Vector3.Lerp(perchContact, contact, Ease(26, dt));
                perchNormal = Vector3.Slerp(perchNormal, normal, Ease(14, dt)).normalized;
            }
            Balance(dt);
            IdleActs(dt);
            if (valid) TurnToward(heading, dt);
            perchHeading = standHeading;
            Stand(perchContact, perchHeading, perchNormal, dt, 16);
            if (greetAt >= 0 && elapsed >= greetAt) { greetAt = -1; crestPulse = .7f; }
            settled = Mathf.MoveTowards(settled, 1, dt * .5f);
            boredom += dt;
            if (!preview && boredom > 90 && act == Act.None) BeginLeaving();
        }
        // The perch's accelerations tip the body (it lags, then recovers); sharp ones make it flutter for balance.
        void Balance(float dt)
        {
            if (!hasLastContact) { lastContact = perchContact; contactVelocity = contactAccel = Vector3.zero; hasLastContact = true; }
            var v = (perchContact - lastContact) / Mathf.Max(dt, 1e-4f); lastContact = perchContact;
            var a = (v - contactVelocity) / Mathf.Max(dt, 1e-4f);
            contactVelocity = Vector3.Lerp(contactVelocity, v, Ease(20, dt));
            contactAccel = Vector3.Lerp(contactAccel, Vector3.ClampMagnitude(a, 40), Ease(10, dt));
            var right = Vector3.Cross(Vector3.up, perchHeading).normalized;
            var goal = new Vector2(Mathf.Clamp(Vector3.Dot(contactAccel, perchHeading) * 2.4f, -24, 24), Mathf.Clamp(Vector3.Dot(contactAccel, right) * 2.4f, -24, 24));
            SpringTilt(goal, dt);
            var flat = new Vector3(contactAccel.x, 0, contactAccel.z);
            if (flat.magnitude > 7 || contactVelocity.magnitude > 1) { balanceFlap = .45f; }
            touchdown = Mathf.MoveTowards(touchdown, 0, dt * 2);
            float bounce = touchdown > 0 ? Mathf.Sin(Mathf.PI * (1 - touchdown)) * .9f : 0;
            // A hand held over it makes the bird crouch low.
            crouch = Mathf.Lerp(crouch, Mathf.Clamp01(Mathf.Max(bounce + Mathf.Max(0, contactAccel.y) * .05f, palmOver * .8f)), Ease(20, dt));
        }
        // Small lives of a perched bird, one at a time, never while it is being touched or balancing.
        void IdleActs(float dt)
        {
            if (act != Act.None)
            {
                if (act == Act.Fluff && actTime < actLength * .3f && actTime + dt >= actLength * .3f) ShakeHead();
                actTime += dt;
                float t = Mathf.Clamp01(actTime / actLength);
                fluff = act == Act.Fluff ? Mathf.Sin(Mathf.PI * t) : Mathf.MoveTowards(fluff, 0, dt * 3);
                if (act == Act.Call && actTime < dt * 1.5f) crestPulse = 1;
                if (actTime >= actLength) { if (act == Act.Turn) hop = 0; act = Act.None; }
                return;
            }
            fluff = Mathf.MoveTowards(fluff, 0, dt * 3);
            if (preview || touchZone != Zone.None || balanceFlap > 0 || nibble > 0 || touchdown > .1f || fingerNear > .3f || takeoff < 1 || palmOver > .2f || peck > 0)
            { nextAct = Mathf.Max(nextAct, elapsed + 1.5f); return; }
            if (elapsed < nextAct) return;
            float r = Random.value; actSide = Random.value < .5f ? -1 : 1; actTime = 0;
            if (r < .18f) { act = Act.Fluff; actLength = .95f; }
            else if (r < .38f) { act = Act.TailBob; actLength = .8f; }
            else if (r < .5f) { act = Act.Stretch; actLength = 1.8f; }
            else if (r < .68f) { act = Act.Preen; actLength = 2.4f; }
            else if (r < .88f)
            {
                act = Act.LookAround; actLength = 1.5f;
                var around = Quaternion.Euler(Random.Range(-15f, 25f), Random.Range(-110f, 110f), 0) * ParentRotation * perchHeading;
                actLook = bird.position + around * 2;
            }
            else { act = Act.Call; actLength = 1.3f; }
            nextAct = elapsed + actLength + Random.Range(2.5f, 6f);
        }

        // ---- Flight ----
        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) { float u = 1 - t; return u * u * a + 2 * u * t * b + t * t * c; }
        void Integrate(Vector3 next, float dt)
        {
            var v = (next - position) / Mathf.Max(dt, 1e-4f);
            accel = Vector3.Lerp(accel, Vector3.ClampMagnitude((v - velocity) / Mathf.Max(dt, 1e-4f), 30), Ease(8, dt));
            velocity = v; position = next;
        }
        void StartFlight(Vector3 to, float speed, float arch, float sideways)
        {
            flightFrom = position; flightTime = 0;
            var path = to - position; float distance = path.magnitude;
            flightDuration = Mathf.Clamp(distance / speed, 1.1f, 3.6f);
            var side = Vector3.Cross(Vector3.up, path.sqrMagnitude > 1e-6f ? path.normalized : Vector3.forward).normalized;
            flightControl = (position + to) * .5f + Vector3.up * arch * (.2f + .12f * distance) + side * sideways * distance;
            // From a standing start the curve leaves upward, as a bird springs off a perch.
            if (grounded || State == Mode.Perched || State == Mode.Resting) flightControl += Vector3.up * .12f;
            var flat = Vector3.ProjectOnPlane(path, Vector3.up);
            if (flat.sqrMagnitude > 1e-4f && (State == Mode.Perched || State == Mode.Resting)) flightHeading = flat.normalized;
        }
        void FlyCurve(Vector3 to, float dt)
        {
            flightTime += dt;
            float u = Mathf.Clamp01(flightTime / flightDuration), s = Mathf.Lerp(u, u * u * (3 - 2 * u), .55f);
            Integrate(Bezier(flightFrom, flightControl, to, s), dt);
        }
        void Hover(Vector3 at, float dt)
        {
            hoverTime += dt;
            var bob = new Vector3(Mathf.Sin(hoverTime * 1.3f) * .015f, Mathf.Sin(hoverTime * 2.6f) * .012f, Mathf.Sin(hoverTime * .9f) * .012f);
            Integrate(Vector3.Lerp(position, at + bob, Ease(5, dt)), dt);
        }
        // Body along the flight path, climbing or diving with it and banking into turns; upright in the hover. Wings
        // beat in bursts with short glides at cruise, glide on descents and beat continuously when slow.
        void FlightPose(float dt, Vector3 face, float extraPitch = 0)
        {
            float speed = velocity.magnitude;
            hoverBlend = Mathf.Lerp(hoverBlend, 1 - Smooth(.25f, .95f, speed), Ease(6, dt));
            var flat = new Vector3(velocity.x, 0, velocity.z);
            if (flat.sqrMagnitude > .01f) flightHeading = Vector3.Slerp(flightHeading, flat.normalized, Ease(8, dt)).normalized;
            face.y = 0; face = face.sqrMagnitude > 1e-6f ? face.normalized : flightHeading;
            var heading = Vector3.Slerp(flightHeading, face, hoverBlend * .9f);
            if (heading.sqrMagnitude < 1e-6f) heading = flightHeading;
            float climb = Mathf.Clamp(Mathf.Atan2(velocity.y, Mathf.Max(flat.magnitude, .1f)) * Mathf.Rad2Deg, -28, 32) * (1 - hoverBlend);
            var right = Vector3.Cross(Vector3.up, heading).normalized;
            float bank = Mathf.Clamp(-Vector3.Dot(accel, right) * 5, -40, 40) * (1 - hoverBlend);
            var goal = Quaternion.LookRotation(heading.normalized, Vector3.up) * Quaternion.Euler(-(climb + hoverBlend * 58 + extraPitch), 0, bank);
            bodyRotation = Quaternion.Slerp(bodyRotation, goal, Ease(10, dt));
            bool gliding = velocity.y < -.3f && speed > .7f;
            float bound = Mathf.Repeat(elapsed, 1.05f) / 1.05f, burst = bound < .7f ? 1 : .15f;
            float slow = Mathf.Max(1 - Smooth(.5f, 1.3f, speed), Mathf.Clamp01(velocity.y * 1.5f));
            flapDrive = Mathf.Lerp(flapDrive, gliding ? .1f : Mathf.Lerp(burst, 1, slow), Ease(10, dt));
            brakeDrive = Mathf.Lerp(brakeDrive, 0, Ease(6, dt));
            legs = Mathf.MoveTowards(legs, 0, dt * 3);
        }
        // Crouch and spring: the first quarter second of every take-off from a perch.
        bool Launching(float dt)
        {
            if (takeoff >= 1) return false;
            takeoff = Mathf.MoveTowards(takeoff, 1, dt / .26f);
            crouch = Mathf.Sin(Mathf.PI * takeoff) * .95f;
            Stand(gripContact, gripHeading, gripNormal, dt);
            if (takeoff >= 1) { crouch = 0; position = BodyOnPerch(gripContact, gripHeading, gripNormal, bodyRotation, scale); flightFrom = position; flightTime = 0; flapDrive = 1; }
            return true;
        }
        bool StandingStart => State == Mode.Perched || State == Mode.Resting || State == Mode.Landing && landHop;
        void BeginApproach(int arm)
        {
            if (State == Mode.Patrol)
            {
                // Between passes the bird is out of sight: it re-materialises on the projector and comes from there.
                if (!flying || scale < .3f) Materialize();
                else
                {
                    position = Lab(bird.position); velocity = Vector3.zero; bodyRotation = Quaternion.Inverse(ParentRotation) * bird.rotation;
                    var flat = Vector3.ProjectOnPlane(lastForward, Vector3.up); if (flat.sqrMagnitude > 1e-4f) flightHeading = flat.normalized;
                }
            }
            bool fromGround = StandingStart;
            approachSide = perchArm = arm; approachSite = PerchSite.Forearm; approachWait = hoverTime = 0;
            var a = senses.Arm(arm); PerchFrom(in a, PerchSite.Forearm, out var contact, out var heading, out _);
            StartFlight(ApproachPoint(contact, heading), 1.4f, 1, arm == 0 ? -.2f : .2f);
            takeoff = fromGround ? 0 : 1; finalApproach = false;
            Enter(Mode.Approach); boredom = 0; if (fromGround) VoiceFlutter();
        }
        // Behind the arm (seen from the person) and a little above it, so the bird lands facing the person.
        static Vector3 ApproachPoint(Vector3 contact, Vector3 heading) => contact - heading * .3f + Vector3.up * .17f;
        void StepApproach(float dt)
        {
            var a = senses.Arm(approachSide);
            if (!PerchAllowed(approachSide) || !a.Tracked || a.Busy) { BeginLeaving(); return; }
            bool valid = PerchFrom(in a, approachSite, out var contact, out var heading, out _);
            targetContact = contact;
            if (Launching(dt)) return;
            var waypoint = ApproachPoint(contact, heading);
            if (flightTime < flightDuration) { FlyCurve(waypoint, dt); FlightPose(dt, heading); }
            else
            {
                Hover(waypoint, dt); FlightPose(dt, heading); approachWait += dt;
                if (valid && approachWait > .1f) { BeginLanding(approachSide, approachSite, false); return; }
                if (approachWait > 6) { BeginLeaving(); return; }
            }
            if (valid && flightTime > flightDuration * .92f) BeginLanding(approachSide, approachSite, false);
        }
        void BeginLanding(int arm, PerchSite site, bool hop)
        {
            bool fromGround = StandingStart;
            for (int i = 0; i < 2; i++) { offerHeld[i] = stepHeld[i] = 0; offerArmed[i] = i == arm && site == PerchSite.Forearm; }
            perchArm = arm; perchSite = site; landHop = hop;
            landFrom = position; landVelocity = fromGround ? Vector3.zero : velocity; landRotation = bodyRotation; landScale = scale; landTime = 0;
            var a = senses.Arm(arm); PerchFrom(in a, site, out var contact, out var heading, out var normal);
            var target = BodyOnPerch(contact, heading, normal, PerchedPose(heading), PerchSize);
            float distance = Vector3.Distance(position, target);
            landDuration = hop ? Mathf.Clamp(.3f + distance * 1.2f, .35f, .6f) : Mathf.Clamp(.45f + distance * .9f, .55f, .95f);
            // A hop springs up and over; a flight landing comes in with the speed it had.
            if (hop) landVelocity = (target - position) * .4f / landDuration + Vector3.up * .9f;
            takeoff = hop && fromGround ? 0 : 1;
            hasLastContact = false; touchZone = Zone.None; act = Act.None;
            Enter(Mode.Landing);
        }
        // Lands on a moving grip along a Hermite arc that ends at rest on it: the body pitches into a flare, the wings
        // brake over the back and the legs reach forward for the perch.
        bool LandStep(Vector3 contact, Vector3 heading, Vector3 normal, float dt)
        {
            landTime += dt;
            float t = Mathf.Clamp01(landTime / landDuration), t2 = t * t, t3 = t2 * t, T = landDuration;
            var finalRotation = PerchedPose(heading);
            var final = BodyOnPerch(contact, heading, normal, finalRotation, PerchSize);
            Integrate((2 * t3 - 3 * t2 + 1) * landFrom + (t3 - 2 * t2 + t) * T * landVelocity + (-2 * t3 + 3 * t2) * final, dt);
            scale = Mathf.Lerp(landScale, PerchSize, Smooth(0, .8f, t));
            float flare = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 1.12f)) * (landHop ? .35f : 1);
            var goal = Quaternion.Slerp(landRotation, finalRotation, Smooth(0, .8f, t)) * Quaternion.Euler(-flare * 24, 0, 0);
            bodyRotation = t >= 1 ? finalRotation : Quaternion.Slerp(bodyRotation, goal, Ease(18, dt));
            gripContact = contact; gripHeading = heading; gripNormal = normal;
            legs = Smooth(.12f, .62f, t);
            hoverBlend = Mathf.Lerp(hoverBlend, 1, Ease(5, dt));
            flapDrive = 1; brakeDrive = Smooth(.08f, .6f, t) * (landHop ? .55f : 1);
            if (t < 1) return false;
            velocity = accel = Vector3.zero; touchdown = 1; crouch = .2f; legs = 1; act = Act.None; nextAct = elapsed + 3;
            if (Random.value < .4f) ShakeHead(.6f);
            return true;
        }
        void StepLanding(float dt)
        {
            var a = senses.Arm(perchArm);
            if (!PerchAllowed(perchArm) || !a.Tracked || a.Busy) { BeginLeaving(); return; }
            bool valid = PerchFrom(in a, perchSite, out var contact, out var heading, out var normal);
            targetContact = contact;
            if (Launching(dt)) return;
            if (!valid && landTime > .05f)
            {
                // The perch moved away mid-landing: hover beside the arm again (or give up on a finger).
                if (perchSite == PerchSite.Forearm) { approachSide = perchArm; approachSite = PerchSite.Forearm; flightTime = flightDuration = 1; approachWait = 0; Enter(Mode.Approach); }
                else BeginLeaving();
                return;
            }
            if (!LandStep(contact, heading, normal, dt)) return;
            perchContact = contact; perchHeading = standHeading = heading; perchNormal = normal; perchLost = steepTime = 0; boredom = 0;
            tiltVelocity.x -= 70; Ripple(FootContact); if (!landHop) greetAt = elapsed + .7f;
            if (landHop && perchSite == PerchSite.Finger) StepUps++;
            Enter(Mode.Perched); grounded = true; VoiceChirp();
        }
        // shooed: a short hop off into the air beside the arm; it hovers a moment and comes back to land.
        void BeginExcursion(Vector3 target, bool shooed = false)
        {
            excursionTarget = target; hoverTime = 0; shooFlight = shooed;
            StartFlight(target, shooed ? 1.8f : 1.3f, 1.2f, 0); takeoff = shooed ? 1 : 0;
            if (shooed) { flightDuration = Mathf.Min(flightDuration, 1.1f); VoiceFlutter(); }
            Enter(Mode.Excursion); leaveCooldown = 3; if (!shooed) VoiceChirp();
        }
        void StepExcursion(float dt)
        {
            if (Launching(dt)) return;
            var face = senses.HasEye ? Lab(senses.Eye) - position : -flightHeading;
            if (flightTime < flightDuration) { FlyCurve(excursionTarget, dt); FlightPose(dt, excursionTarget - position); return; }
            Hover(excursionTarget, dt); FlightPose(dt, Vector3.Lerp(excursionTarget - position, face, Smooth(1.5f, 2.5f, hoverTime)));
            if (hoverTime < 2.8f) return;
            var a = senses.Arm(perchArm);
            if (perchArm >= 0 && PerchAllowed(perchArm) && a.Tracked && !a.Busy) BeginApproach(perchArm);
            else BeginLeaving();
        }
        void BeginLeaving()
        {
            bool fromGround = StandingStart;
            if (State == Mode.Patrol) position = Lab(bird.position);
            for (int i = 0; i < 2; i++) offerHeld[i] = aimedAt[i] = spotHeld[i] = stepHeld[i] = 0;
            touchZone = Zone.None; act = Act.None; perchLost = steepTime = 0; finalApproach = false;
            if (fromGround) { gripContact = perchContact; gripHeading = perchHeading; gripNormal = perchNormal; }
            StartFlight(RestApproach(), 1.5f, 1.3f, perchArm == 0 ? -.25f : .25f);
            takeoff = fromGround ? 0 : 1;
            if (!fromGround && velocity.sqrMagnitude > .01f) flightControl = position + velocity * flightDuration * .35f + Vector3.up * .2f;
            Enter(Mode.Leaving); leaveCooldown = 4; hoverBlend = Mathf.Max(hoverBlend, .4f);
            if (fromGround) VoiceFlutter();
        }
        void StepLeaving(float dt)
        {
            if (modeTime > 9) { Materialize(); reveal = 1; grounded = true; return; }
            if (Launching(dt)) return;
            if (!finalApproach)
            {
                FlyCurve(RestApproach(), dt); FlightPose(dt, RestHeading());
                if (flightTime >= flightDuration)
                {
                    finalApproach = true; landHop = false; landFrom = position; landVelocity = velocity; landRotation = bodyRotation; landScale = scale; landTime = 0; landDuration = .8f;
                }
                return;
            }
            perchHeading = standHeading = RestHeading();
            if (!LandStep(RestContact, perchHeading, Vector3.up, dt)) return;
            perchArm = -1; perchContact = RestContact; perchNormal = Vector3.up; finalApproach = false;
            Enter(Mode.Resting); grounded = true;
        }

        // ---- Touch ----
        Zone Classify(Vector3 world, out float depth)
        {
            var p = bird.InverseTransformPoint(world); depth = float.MaxValue;
            if (head)
            {
                var h = head.InverseTransformPoint(world);
                // The head and its crest, with a finger's width of margin.
                float d = new Vector3(h.x / .05f, (h.y - .015f) / .055f, (h.z - .03f) / .075f).magnitude;
                if (d < 1.15f) { depth = d; return h.z > .08f ? Zone.Bill : h.y > .035f ? Zone.Crown : h.y < -.01f ? Zone.Chin : Zone.Crown; }
            }
            var body = new Vector3(p.x / .075f, p.y / .075f, (p.z + .035f) / .19f);
            if (body.magnitude < 1.15f) { depth = body.magnitude; return Mathf.Abs(p.x) > .05f ? p.x < 0 ? Zone.LeftWing : Zone.RightWing : p.y > 0 ? Zone.Back : Zone.Breast; }
            for (int i = 0; i < streamers.Length; i++) foreach (var point in streamers[i].points)
                if (Vector3.Distance(world, point) < .045f) { depth = .9f; return Zone.Tail; }
            return Zone.None;
        }
        void Touch(float dt)
        {
            Zone next = Zone.None; float best = float.MaxValue; int which = -1; Vector3 point = Vector3.zero;
            handPointCount = forearmCount = 0;
            for (int arm = 0; arm < 2; arm++)
            {
                var a = senses.Arm(arm);
                if (!a.Tracked) continue;
                if ((a.Wrist - a.Elbow).sqrMagnitude > .0001f) { forearmFrom[forearmCount] = a.Wrist; forearmTo[forearmCount] = a.Elbow; forearmCount++; }
                for (int finger = 0; finger < QuetzalArm.TouchPoints; finger++)
                    if ((a.TouchMask & (1 << finger)) != 0) handPoints[handPointCount++] = a.TouchPoint(finger);
                if (a.Busy) continue;
                for (int finger = 0; finger < QuetzalArm.TouchPoints; finger++)
                {
                    if ((a.TouchMask & (1 << finger)) == 0) continue;
                    // The hand the bird stands on is not itself a petting contact.
                    if ((Perched || State == Mode.Landing) && arm == perchArm) continue;
                    var p = a.TouchPoint(finger); var zone = Classify(p, out var depth);
                    if (zone != Zone.None && depth < best) { next = zone; best = depth; which = arm; point = p; }
                }
            }
            bool standing = Perched || State == Mode.Resting;
            annoyance = Mathf.MoveTowards(annoyance, 0, dt * .25f);
            PalmOver(dt, standing);
            PendingPeck(dt, standing);
            for (int i = 0; i < 2; i++) wingHold[i] = Mathf.MoveTowards(wingHold[i], 0, dt * 3);
            if (next == Zone.None || !standing)
            {
                touchLost += dt;
                if (touchLost > .12f)
                {
                    if (touchZone != Zone.None) EndTouch(touchZone);
                    touchZone = Zone.None; touchArm = -1;
                }
                contentment = Mathf.MoveTowards(contentment, 0, dt * .12f);
                rub = Mathf.MoveTowards(rub, 0, dt * 2);
                petLean = Vector2.Lerp(petLean, Vector2.zero, Ease(4, dt)); return;
            }
            bool fresh = touchZone == Zone.None || touchArm != which || touchZone != next;
            float speed = senses.Arm(which).Velocity.magnitude;
            if (speed > 1.5f)
            {
                // A swipe: startled straight off the arm (it comes back when it has calmed down).
                if (elapsed - lastTap > .5f) { lastTap = elapsed; Flinches++; Shoo(point); }
                return;
            }
            if (fresh)
            {
                Ripple(point);
                if (elapsed - lastTap > .25f)
                {
                    Taps++; lastTap = elapsed;
                    if (next == Zone.Bill) { Pecks++; VoiceTap(); }
                    else if (speed > .45f) Flinch(next, point, speed);
                    else Pet();
                }
                if (next == Zone.LeftWing || next == Zone.RightWing) FlickWing(next == Zone.LeftWing ? 0 : 1, .9f);
                if (next == Zone.Tail && act == Act.None) { act = Act.TailBob; actTime = 0; actLength = .8f; }
                strokeTime = 0;
            }
            touchLost = 0; touchZone = next; touchArm = which; touchPoint = point; boredom = 0;
            bool stroking = speed > .02f && speed < .45f;
            if (stroking) strokeTime += dt;
            // While stroked it purrs now and then; a finger at the bill is nibbled instead.
            if (next != Zone.Bill && stroking && elapsed > nextCoo && contentment > .55f) { VoiceCoo(); nextCoo = elapsed + 2.4f; }
            contentment = Mathf.Clamp01(contentment + dt * (next == Zone.Bill ? .1f : .45f)); petGlow = 1;
            bow = Mathf.MoveTowards(bow, next == Zone.Crown || next == Zone.Back ? .7f : 0, dt * 3);
            chin = Mathf.MoveTowards(chin, next == Zone.Chin ? .8f : 0, dt * 3);
            // A scratch on the crown or chin that goes on makes it rub its head against the finger.
            rub = Mathf.MoveTowards(rub, (next == Zone.Crown || next == Zone.Chin) && strokeTime > .6f ? 1 : 0, dt * 2);
            if (next == Zone.Bill) nibble = .7f;
            // A finger on a wing holds it slightly open.
            if (next == Zone.LeftWing) wingHold[0] = .5f;
            if (next == Zone.RightWing) wingHold[1] = .5f;
            // Leans into the stroking hand.
            var local = Lab(point) - position; var right = Vector3.Cross(Vector3.up, perchHeading).normalized;
            var goal = new Vector2(Mathf.Clamp(-Vector3.Dot(local, perchHeading) * 90, -8, 8), Mathf.Clamp(-Vector3.Dot(local, right) * 110, -10, 10)) * contentment;
            petLean = Vector2.Lerp(petLean, next == Zone.Bill ? Vector2.zero : goal, Ease(3, dt));
        }
        void Pet() { Pets++; VoiceTrill(); nextCoo = elapsed + 2; crestPulse = .3f; }
        // A poke: the body jolts away from the finger and springs back, the wings flick, the crest flares, a squeak; it
        // may peck back at the finger a moment later. Pokes add up to annoyance, and enough of it sends the bird off.
        void Flinch(Zone zone, Vector3 at, float speed)
        {
            Flinches++; duck = .8f; crestPulse = 1; contentment *= .4f;
            annoyance += speed > 1 ? 2 : 1;
            var away = Lab(bird.position) - Lab(at); var right = Vector3.Cross(Vector3.up, perchHeading).normalized;
            tiltVelocity += new Vector2(Vector3.Dot(away, perchHeading) > 0 ? -110 : 110, Vector3.Dot(away, right) > 0 ? -90 : 90) * Mathf.Clamp01(speed);
            FlickWing(0, .5f); FlickWing(1, .5f);
            if (zone == Zone.LeftWing || zone == Zone.RightWing || zone == Zone.Tail) VoiceChirp(); else VoiceSqueak();
            if (annoyance >= 3) Shoo(at); else if (pendingPeck < 0) pendingPeck = elapsed + Random.Range(.3f, .5f);
        }
        void Shoo(Vector3 at)
        {
            Shoos++; annoyance = 0; pendingPeck = -1; duck = 1; crestPulse = 1; VoiceSqueak();
            var away = Vector3.ProjectOnPlane(Lab(bird.position) - Lab(at), Vector3.up); away = away.sqrMagnitude > 1e-4f ? away.normalized : -perchHeading;
            var target = position + away * .35f + Vector3.up * .3f;
            BeginExcursion(target, true);
        }
        // A retaliatory peck: if the poking finger is still at its head, a quick lunge with the bill.
        void PendingPeck(float dt, bool standing)
        {
            peck = Mathf.MoveTowards(peck, 0, dt);
            if (pendingPeck < 0 || elapsed < pendingPeck) return;
            pendingPeck = -1;
            if (!standing) return;
            for (int i = 0; i < 2; i++)
            {
                var a = senses.Arm(i); if (!a.HasFingertip || (Perched || State == Mode.Landing) && i == perchArm) continue;
                if (Vector3.Distance(a.Fingertip, head.position) < .12f * bird.lossyScale.x) { peckTarget = a.Fingertip; peck = PeckSeconds; Pecks++; VoiceTap(); crestPulse = .6f; return; }
            }
        }
        const float PeckSeconds = .28f;
        Vector3 peckTarget;
        // A palm held flat over the bird's head makes it crouch and flatten; when the hand rests gently on its back
        // instead, it settles.
        void PalmOver(float dt, bool standing)
        {
            bool over = false;
            for (int i = 0; standing && i < 2; i++)
            {
                var a = senses.Arm(i);
                if (!a.Tracked || a.Busy || (a.TouchMask & (1 << QuetzalArm.PalmPoint)) == 0 || Perched && i == perchArm) continue;
                var toPalm = a.Palm - head.position;
                if (toPalm.y > .03f && toPalm.y < .22f && new Vector2(toPalm.x, toPalm.z).magnitude < .12f && a.PalmFacing.y < -.5f && a.Velocity.magnitude < .6f) over = true;
            }
            palmOver = Mathf.MoveTowards(palmOver, over ? 1 : 0, dt * (over ? 2.5f : 1.5f));
        }
        void EndTouch(Zone zone)
        {
            // A long stroke down the back ends with a wag of the tail; a good stroke elsewhere with a head shake.
            if (zone == Zone.Back && strokeTime > .8f && act == Act.None) { act = Act.Wag; actTime = 0; actLength = .7f; }
            else if (zone != Zone.Bill && contentment > .25f && Random.value < .5f) { ShakeHead(.3f); }
            strokeTime = 0;
        }
        void FlickWing(int side, float amount) { wingFlickTime[side] = 0; wingFlickAmount[side] = Mathf.Max(amount, WingFlick(side)); }
        float WingFlick(int side)
        {
            float t = wingFlickTime[side];
            return wingFlickAmount[side] * (t < .07f ? t / .07f : Mathf.Exp(-(t - .07f) * 6));
        }
        // Streamer collision: fingertips are spheres, forearms capsules.
        Vector3 PushOutOfHands(Vector3 p)
        {
            const float tip = .018f, arm = .045f;
            for (int i = 0; i < handPointCount; i++)
            {
                var d = p - handPoints[i]; float m = d.sqrMagnitude;
                if (m < tip * tip && m > 1e-10f) p = handPoints[i] + d * (tip / Mathf.Sqrt(m));
            }
            for (int i = 0; i < forearmCount; i++)
            {
                var axis = forearmTo[i] - forearmFrom[i]; float t = Mathf.Clamp01(Vector3.Dot(p - forearmFrom[i], axis) / axis.sqrMagnitude);
                var closest = forearmFrom[i] + axis * t; var d = p - closest; float m = d.sqrMagnitude;
                if (m < arm * arm && m > 1e-10f) p = closest + d * (arm / Mathf.Sqrt(m));
            }
            return p;
        }

        // ---- Head, face and wings ----
        // Chooses what the bird attends to and how (see DigitalQuetzal.Gaze.cs for how the head then moves).
        void AnimateInteractiveHead(float dt)
        {
            bool standing = Perched || State == Mode.Resting;
            bool airborne = !grounded && State != Mode.Landing && !(State == Mode.Approach && flightTime >= flightDuration) && !(State == Mode.Excursion && flightTime >= flightDuration);
            var ahead = bird.position + ParentRotation * (standing ? perchHeading : flightHeading) * 2;
            var target = senses.HasEye ? senses.Eye : ahead;
            int focus = senses.HasEye ? 0 : 1; bool monocular = true, continuous = false, jabbing = false;
            float interest = .35f, yawLimit = 110, neckShare = .35f;
            Looking = senses.HasEye ? "Person" : "Ahead";
            float nearest = .3f; int nearArm = -1;
            for (int i = 0; i < 2; i++)
            {
                var a = senses.Arm(i); if (!a.HasFingertip || !a.Tracked || a.Busy || (Perched || State == Mode.Landing) && i == perchArm) continue;
                float distance = Vector3.Distance(a.Fingertip, head.position);
                if (distance < nearest) { nearest = distance; nearArm = i; }
            }
            float wasNear = fingerNear;
            fingerNear = Mathf.MoveTowards(fingerNear, nearArm >= 0 && standing ? 1 : 0, dt * 3);
            if (wasNear <= 0 && fingerNear > 0) cockSide = Random.value < .5f ? -1 : 1;
            // A fingertip coming close is inspected eye by eye, in quick fixations; right at the bill it gets nibbled.
            if (nearArm >= 0 && standing)
            {
                target = senses.Arm(nearArm).Fingertip; focus = 10 + nearArm; interest = .85f; Looking = "Finger";
                if (nearest < .045f) nibble = Mathf.Max(nibble, .3f);
            }
            if (State == Mode.Approach || State == Mode.Landing) { target = World(targetContact); focus = 2; monocular = false; interest = .2f; Looking = "Perch"; }
            if (State == Mode.Leaving) { target = finalApproach ? World(RestContact) : ahead; focus = finalApproach ? 2 : 3; monocular = false; interest = .2f; Looking = "Ahead"; }
            if (State == Mode.Excursion && !shooFlight) { target = World(excursionTarget - Vector3.up * .42f); focus = 4; interest = .6f; Looking = "Spot"; }
            // Resting, it notices an arm being offered before it answers.
            if (State == Mode.Resting)
                for (int i = 0; i < 2; i++) if (offerHeld[i] > .2f) { target = senses.Arm(i).Wrist; focus = 30 + i; interest = .7f; Looking = "Offer"; }
            // A hand held over it is watched warily, one eye turned up to it.
            if (palmOver > .3f) for (int i = 0; i < 2; i++) { var a = senses.Arm(i); if (a.Tracked && i != perchArm) { target = a.Palm; focus = 40 + i; interest = .55f; Looking = "Palm"; } }
            // In flight the head rides level along the path, following it smoothly.
            if (airborne && State != Mode.Excursion) { target = Vector3.Lerp(ahead, target, State == Mode.Approach ? Smooth(.5f, 1, flightTime / flightDuration) : 0); continuous = true; }
            if (State == Mode.Landing) continuous = true;   // the eyes stay locked on the perch all the way in
            if (act == Act.LookAround) { target = actLook; focus = 5; interest = .5f; Looking = "Around"; }
            if (act == Act.Preen)
            {
                // Preening: bill to the shoulder and along the wing, many short fixations with a nibble at each.
                target = bird.TransformPoint(new Vector3(actSide * .058f, .012f, -.03f - .04f * Mathf.PingPong(actTime * .8f, 1)));
                focus = 6; monocular = false; interest = 1; yawLimit = 165; neckShare = .5f; jabbing = true; Looking = "Preening";
            }
            if (touchZone != Zone.None)
            {
                // Stroked, it watches the hand calmly with long fixations; a finger at the bill is nibbled face on.
                target = touchPoint; focus = 7; interest = .1f; Looking = "Touch";
                monocular = touchZone != Zone.Bill;
            }
            if (nibble > 0 && standing) { monocular = false; jabbing = true; }
            if (act == Act.Call) monocular = false;   // calls with the bill raised toward the listener
            if (peck > 0) { target = peckTarget; focus = 50; monocular = false; Looking = "Peck"; }
            LookPoint = target;
            Gaze(dt, target, focus, monocular, interest, continuous, yawLimit);
            cock = Mathf.MoveTowards(cock, fingerNear > .2f && touchZone == Zone.None ? cockSide : 0, dt * 2.5f);
            PoseHead(dt, neckShare, jabbing, standing);
            // Bill: opens in pulses to call, at the end of each nibble or preen jab, and in alarm.
            float t = act == Act.None ? 0 : Mathf.Clamp01(actTime / actLength);
            float open = act == Act.Call ? Mathf.Max(0, Mathf.Sin(t * Mathf.PI * 6)) * .9f : 0;
            open = Mathf.Max(open, jab * .75f, duck * .6f, peck > 0 ? Mathf.Sin(Mathf.PI * (1 - peck / PeckSeconds)) : 0);
            beak = Mathf.MoveTowards(beak, open, dt * 14);
        }
        void AnimateFace(float dt)
        {
            blinkTime += dt;
            if (elapsed > nextBlink) { blinkTime = 0; nextBlink = elapsed + Random.Range(2, 5); }
            float shut = blinkTime < .14f ? Mathf.Sin(blinkTime / .14f * Mathf.PI) : 0;
            float eyeHeight = Mathf.Max(.04f, 1 - Mathf.Max(shut, contentment * .5f, palmOver * .35f));
            foreach (var eye in eyes) eye.localScale = new Vector3(1, eyeHeight, 1);
            // The crest rises with attention, calls, alarm and a shake; it lies flat when content or under a hand.
            bool calling = act == Act.Call || State == Mode.Approach && modeTime < 1;
            float attention = Mathf.Max(highlight, fingerNear * .6f, calling ? 1 : 0, fluff, duck, State == Mode.Landing ? .3f : 0);
            crestRaise = Mathf.MoveTowards(crestRaise, Mathf.Clamp01(attention * (1 - contentment * .6f) * (1 - palmOver * .8f)), dt * 3);
            // Each crest row is a spring: it follows the mood with a little lag and overshoot, a pulse flicks the front
            // rows up, and the head's own motion makes the whole crest bounce.
            if (crestPulse > 0) { for (int i = 0; i < crestSpeed.Length; i++) crestSpeed[i] += 320 * crestPulse * (1 - i / (float)crestSpeed.Length); crestPulse = 0; }
            for (int i = 0; i < crestFeathers.Length; i++)
            {
                float goal = QuetzalMesh.CrestAngle(i, crestRaise) + Mathf.Sin(elapsed * 2 + i * .7f) * 1.5f;
                crestSpeed[i] += ((goal - crestAngle[i]) * (200 - i * 10) - crestSpeed[i] * 13) * dt;
                crestAngle[i] += crestSpeed[i] * dt;
                crestFeathers[i].localRotation = Quaternion.Euler(crestAngle[i], 0, 0);
            }
            duck = Mathf.MoveTowards(duck, 0, dt * 1.8f);
            bow = Mathf.Max(bow, palmOver * .5f);
            if (touchZone == Zone.None) { bow = Mathf.MoveTowards(bow, palmOver * .5f, dt); chin = Mathf.MoveTowards(chin, 0, dt); }
            petGlow = Mathf.MoveTowards(petGlow, 0, dt * .8f);
            nibble = Mathf.MoveTowards(nibble, 0, dt); balanceFlap = Mathf.MoveTowards(balanceFlap, 0, dt);
            for (int i = 0; i < 2; i++) wingFlickTime[i] += dt;
        }
        // Folded: the arm runs back along the flank with its upper surface turned out and up, so the long golden-green
        // coverts drape over the side and meet over the back like a cape; the hand continues it with the primaries
        // closed, their dark tips lying over the tail. A touched wing flicks open; a stretch reaches one wing down and
        // back; a shake loosens both.
        void FoldWings(float dt)
        {
            tuck = Mathf.MoveTowards(tuck, 1, dt * 2.4f);
            float t = act == Act.None ? 0 : Mathf.Clamp01(actTime / actLength), envelope = Mathf.Sin(Mathf.PI * t);
            float stretch = act == Act.Stretch ? Smooth(0, .3f, t) * (1 - Smooth(.75f, 1, t)) : 0;
            float loose = act == Act.Fluff ? envelope * .5f : 0;
            float leftReach = actSide < 0 ? stretch : 0, rightReach = actSide > 0 ? stretch : 0;
            float leftLift = Mathf.Max(WingFlick(0), wingHold[0], loose), rightLift = Mathf.Max(WingFlick(1), wingHold[1], loose);
            PoseWings(FoldedArm(-1, leftLift, leftReach), FoldedArm(1, rightLift, rightReach), FoldedHand(-1, leftLift, leftReach), FoldedHand(1, rightLift, rightReach),
                Ease(12, dt), 1, 1, 1 - Mathf.Max(leftReach, leftLift), 1 - Mathf.Max(rightReach, rightLift));
        }
        // The shipped wing poses (0.24.3/0.24.4). A wing frame turns the wing so that its span (mesh +x of the right wing,
        // -x of the left) points toward tip and its leading edge (mesh +z) toward leading.
        static Quaternion WingFrame(Vector3 tip, int side, Vector3 leading)
        {
            var x = tip.normalized * side;
            if (x.sqrMagnitude >= Vector3.kEpsilon) leading -= x * (Vector3.Dot(leading, x) / x.sqrMagnitude);
            var forward = leading.normalized;
            return Quaternion.LookRotation(forward, Vector3.Cross(forward, x));
        }
        // Folded: the span runs back along the body and the leading edge points up, so the feathers hang flat against the
        // flank. A stretch reaches the wing down and back; a flick (a touch, a startle) lifts it out from the body.
        static Quaternion FoldedArm(int side, float lift, float reach)
        {
            var arm = WingFrame(new Vector3(side * .05f, -.24f, -1), side, new Vector3(0, 1, .25f));
            arm = Quaternion.Slerp(arm, WingFrame(new Vector3(side * .45f, -.8f, -.55f), side, new Vector3(0, .25f, 1)), reach);
            return Quaternion.Slerp(arm, WingFrame(new Vector3(side * .75f, -.15f, -.65f), side, new Vector3(0, .45f, 1)), lift);
        }
        static Quaternion FoldedHand(int side, float lift, float reach)
        {
            var hand = Quaternion.Euler(0, -40 * side, -4 * side);
            hand = Quaternion.Slerp(hand, Quaternion.Euler(0, 6 * side, 0), reach);
            return Quaternion.Slerp(hand, Quaternion.Euler(0, -20 * side, 0), lift);
        }
        public Vector3 StreamerPoint(int index, float along)
        {
            if (streamers == null || index < 0 || index >= streamers.Length) return bird ? bird.position : transform.position;
            var points = streamers[index].points; float f = Mathf.Clamp01(along) * (points.Length - 1);
            int i = Mathf.Min((int)f, points.Length - 2); return Vector3.Lerp(points[i], points[i + 1], f - i);
        }
        public Vector3 StreamerTip(int index) => StreamerPoint(index, 1);
    }
}
