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

    // Production 0.24.4 restricts UI presses to the index finger. Other fingers remain available to the
    // quetzal's touch sensing, but cannot accidentally type or activate a neighbouring control.
    public sealed class MultiFingerTouch
    {
        public const int Fingers = 5;
        public const int IndexFinger = 1;
        public const float Refractory = .12f;
        public static readonly string[] Names = { "pulgar", "índice", "medio", "anular", "meñique" };
        readonly DirectTouchState[] states = { new DirectTouchState(), new DirectTouchState(), new DirectTouchState(), new DirectTouchState(), new DirectTouchState() };
        float lastPress = float.NegativeInfinity;
        public int LastFinger { get; private set; } = -1;
        public void Reset() { foreach (var s in states) s.Reset(); lastPress = float.NegativeInfinity; LastFinger = -1; }
        public void ResetFinger(int finger) => states[finger].Reset();
        // targets[i] is what finger i touches (null when none), fronts[i] its distance in front of that surface.
        // Returns the finger that presses this frame, or -1.
        public int Step(object[] targets, float[] fronts, float now, float delta)
        {
            for (int f = 0; f < Fingers; f++) if (f != IndexFinger) states[f].Reset();
            if (!states[IndexFinger].Step(targets[IndexFinger], fronts[IndexFinger], targets[IndexFinger] != null, delta)) return -1;
            if (now - lastPress < Refractory) { states[IndexFinger].Consume(); return -1; }
            lastPress = now; LastFinger = IndexFinger; return IndexFinger;
        }
    }
}
