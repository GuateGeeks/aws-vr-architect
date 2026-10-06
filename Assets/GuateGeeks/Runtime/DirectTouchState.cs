namespace GuateGeeks.AwsVr
{
    // Requires an approach from the front, then contact dwell and withdrawal before another press.
    public sealed class DirectTouchState
    {
        object target;
        bool armed, fired;
        float contact;
        public bool Armed => armed;
        public void Reset() { target=null; armed=fired=false; contact=0; }
        // Spend the current contact without pressing: this finger must withdraw before it can press again.
        public void Consume() { fired=true; contact=0; }
        public bool Step(object next, float frontDistance, bool inside, float delta)
        {
            if(next==null || !inside || frontDistance < -.025f) { Reset(); return false; }
            if(!ReferenceEquals(next,target)) { Reset(); target=next; }
            if(frontDistance>.018f) { armed=true; fired=false; contact=0; return false; }
            if(!armed || fired || frontDistance>.004f) { contact=0; return false; }
            contact+=delta;
            if(contact<.08f) return false;
            fired=true; return true;
        }
    }

    // Every fingertip of a hand can press (thumb, index, middle, ring and little finger), each with its own
    // approach → dwell → withdraw cycle. A flat hand can reach several buttons at once, so when more than one
    // finger completes a press in the same instant only the deepest one presses, and right after a press the
    // hand has a short refractory period. Fingers that lose that arbitration must withdraw before pressing again,
    // so a resting neighbour finger never fires a second key later.
    public sealed class MultiFingerTouch
    {
        public const int Fingers = 5;
        public const float Refractory = .12f;
        public static readonly string[] Names = { "pulgar", "índice", "medio", "anular", "meñique" };
        readonly DirectTouchState[] states = { new DirectTouchState(), new DirectTouchState(), new DirectTouchState(), new DirectTouchState(), new DirectTouchState() };
        readonly bool[] firing = new bool[Fingers];
        float lastPress = float.NegativeInfinity;
        public int LastFinger { get; private set; } = -1;
        public void Reset() { foreach (var s in states) s.Reset(); lastPress = float.NegativeInfinity; LastFinger = -1; }
        public void ResetFinger(int finger) => states[finger].Reset();
        // targets[i] is what finger i touches (null when none), fronts[i] its distance in front of that surface.
        // Returns the finger that presses this frame, or -1.
        public int Step(object[] targets, float[] fronts, float now, float delta)
        {
            int best = -1;
            for (int f = 0; f < Fingers; f++)
            {
                firing[f] = states[f].Step(targets[f], fronts[f], targets[f] != null, delta);
                if (firing[f] && (best < 0 || fronts[f] < fronts[best])) best = f;
            }
            if (best < 0) return -1;
            bool cooling = now - lastPress < Refractory;
            for (int f = 0; f < Fingers; f++) if (firing[f] && (cooling || f != best)) states[f].Consume();
            if (cooling) return -1;
            lastPress = now; LastFinger = best; return best;
        }
    }
}
