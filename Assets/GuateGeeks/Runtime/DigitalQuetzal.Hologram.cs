using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // The quetzal is a projection (LabQuetzal shader), driven as in 0.24.3/0.24.4: it materialises outward from its own
    // centre in three quarters of a second (a 0.35 s glitch burst as it appears), glitches briefly every 5 to 12 seconds,
    // its flight-feather undersides turn from pale grey (open wings) to dark green-grey (folded), a finger touching it
    // glows, and a ring of light spreads over its plumage from each new touch and each landing.
    public sealed partial class DigitalQuetzal
    {
        static readonly Color OpenUnderside = new Color(.73f, .75f, .76f, 1), FoldedUnderside = new Color(.13f, .16f, .15f, 1);
        float glitch, glitchAmount, glitchUntil, nextGlitch = 6, rippleAge = 10, touchGlow;
        Vector3 rippleWorld;
        void UpdateHologram(float dt)
        {
            if (elapsed > nextGlitch)
            {
                glitchUntil = elapsed + .08f + .12f * Random.value; glitchAmount = .45f + .55f * Random.value;
                nextGlitch = elapsed + 5 + 7 * Random.value;
            }
            glitch = Mathf.MoveTowards(glitch, elapsed < glitchUntil ? glitchAmount : 0, dt * 14);
            rippleAge = Mathf.Min(rippleAge + dt, 10);
            bool touching = touchZone != Zone.None;
            touchGlow = Mathf.MoveTowards(touchGlow, touching ? 1 : 0, dt * (touching ? 8 : 3));
        }
        void Glitch(float amount, float seconds)
        {
            glitchAmount = Mathf.Max(amount, elapsed < glitchUntil ? glitchAmount : 0); glitchUntil = Mathf.Max(glitchUntil, elapsed + seconds);
        }
        void Ripple(Vector3 world) { rippleWorld = world; rippleAge = 0; }
        void ApplyMaterial()
        {
            float size = bird.localScale.x;
            float radius = reveal >= .999f ? 1000 : (Mathf.Min(reveal * reveal, 1) * 1.55f - .05f) * Mathf.Max(size, .6f);
            plumage.SetFloat("_Reveal", radius); plumage.SetVector("_RevealCenter", bird.position);
            plumage.SetFloat("_Glitch", glitch);
            plumage.SetFloat("_Highlight", Mathf.Max(highlight, petGlow * .4f));
            plumage.SetColor("_Underside", Color.Lerp(OpenUnderside, FoldedUnderside, Mathf.Clamp01(wingFold)));
            plumage.SetVector("_TouchPoint", new Vector4(touchPoint.x, touchPoint.y, touchPoint.z, touchGlow));
            plumage.SetVector("_TouchRipple", new Vector4(rippleWorld.x, rippleWorld.y, rippleWorld.z, rippleAge));
        }
    }
}
