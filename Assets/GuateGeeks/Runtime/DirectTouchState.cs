namespace GuateGeeks.AwsVr
{
    // Requires an approach from the front, then contact dwell and withdrawal before another press.
    public sealed class DirectTouchState
    {
        object target;
        bool armed, fired;
        float contact;
        public void Reset() { target=null; armed=fired=false; contact=0; }
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
}
