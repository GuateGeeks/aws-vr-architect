using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // Synthesised "glass" interface sounds: short, high, quiet partials with fast decay.
    // Each kind is rate-limited so many panels opening together read as one cue.
    public sealed class LabFeedback : MonoBehaviour
    {
        public const int Select = 0, Success = 1, Error = 2, Hover = 3, Open = 4, Close = 5, Wake = 6;
        public static LabFeedback Current { get; private set; }
        public static float Clock { get; private set; }
        public bool ReducedMotion { get; private set; }
        public bool Muted { get; private set; }
        const int Rate = 22050;
        AudioSource source;
        AudioClip[] clips;
        readonly float[] volume = { 1, .9f, .9f, .28f, .45f, .4f, .7f };
        readonly float[] lastPlayed = new float[7];
        void Awake()
        {
            Current = this; Clock = 0;
            source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false; source.volume = .13f;
            clips = new[] {
                Bell(new[] { 1480f, 2220f, 3330f }, new[] { 1f, .45f, .2f }, .07f, 55),          // select: glassy click
                Arpeggio(new[] { 880f, 1108.7f, 1318.5f, 1760f }, .055f, .32f),                     // success: rising major chime
                Pulses(196, 2, .085f, .045f),                                                        // error: two low pulses
                Bell(new[] { 3400f, 5100f }, new[] { 1f, .3f }, .022f, 150),                          // hover: tiny tick
                Chirp(520, 1900, .15f, .55f),                                                         // open: upward shimmer
                Chirp(1500, 420, .11f, .45f),                                                         // close: downward
                Swell(new[] { 440f, 659.3f, 880f }, .42f)                                             // assistant wake
            };
        }
        static AudioClip Make(string name, float[] samples) { var clip = AudioClip.Create(name, samples.Length, 1, Rate, false); clip.SetData(samples, 0); return clip; }
        static AudioClip Bell(float[] partials, float[] gains, float duration, float decay)
        {
            int count = Mathf.CeilToInt(duration * Rate); var s = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / Rate, env = Mathf.Exp(-t * decay) * Mathf.Min(1, i / 40f), v = 0;
                for (int k = 0; k < partials.Length; k++) v += Mathf.Sin(2 * Mathf.PI * partials[k] * t) * gains[k];
                s[i] = v * env * .55f;
            }
            return Make("Glass tone", s);
        }
        static AudioClip Arpeggio(float[] notes, float step, float tail)
        {
            int count = Mathf.CeilToInt((step * notes.Length + tail) * Rate); var s = new float[count];
            for (int n = 0; n < notes.Length; n++)
            {
                int start = Mathf.RoundToInt(n * step * Rate);
                for (int i = start; i < count; i++)
                {
                    float t = (float)(i - start) / Rate, env = Mathf.Exp(-t * 14) * Mathf.Min(1, (i - start) / 60f);
                    s[i] += (Mathf.Sin(2 * Mathf.PI * notes[n] * t) + .25f * Mathf.Sin(2 * Mathf.PI * notes[n] * 2.76f * t)) * env * .32f;
                }
            }
            return Make("Success chime", s);
        }
        static AudioClip Pulses(float frequency, int pulses, float length, float gap)
        {
            int count = Mathf.CeilToInt(pulses * (length + gap) * Rate); var s = new float[count];
            for (int p = 0; p < pulses; p++)
            {
                int start = Mathf.RoundToInt(p * (length + gap) * Rate), n = Mathf.RoundToInt(length * Rate);
                for (int i = 0; i < n && start + i < count; i++)
                {
                    float t = (float)i / Rate, env = Mathf.Sin(Mathf.PI * i / n);
                    s[start + i] = (Mathf.Sin(2 * Mathf.PI * frequency * t) + .33f * Mathf.Sin(2 * Mathf.PI * frequency * 3 * t)) * env * .6f;
                }
            }
            return Make("Error pulses", s);
        }
        static AudioClip Chirp(float from, float to, float duration, float level)
        {
            int count = Mathf.CeilToInt(duration * Rate); var s = new float[count]; float phase = 0; uint noise = 1234567;
            for (int i = 0; i < count; i++)
            {
                float u = (float)i / count, f = Mathf.Lerp(from, to, u * u * (3 - 2 * u));
                phase += 2 * Mathf.PI * f / Rate; noise = noise * 1664525 + 1013904223;
                float grain = ((noise >> 9) / 8388608f - 1) * .08f;
                s[i] = (Mathf.Sin(phase) * .7f + Mathf.Sin(phase * 2.01f) * .2f + grain) * Mathf.Sin(Mathf.PI * u) * level;
            }
            return Make("Panel sweep", s);
        }
        static AudioClip Swell(float[] chord, float duration)
        {
            int count = Mathf.CeilToInt(duration * Rate); var s = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / Rate, u = (float)i / count, env = Mathf.Sin(Mathf.PI * Mathf.Pow(u, .6f)) * (1 - u * .4f), v = 0;
                for (int k = 0; k < chord.Length; k++) v += Mathf.Sin(2 * Mathf.PI * chord[k] * t * (1 + .002f * k)) / chord.Length;
                s[i] = v * env * .6f;
            }
            return Make("Assistant wake", s);
        }
        void Update() { if (!ReducedMotion) Clock += Time.unscaledDeltaTime; Shader.SetGlobalFloat("_LabAnimationTime", Clock); }
        public void ToggleMotion() => ReducedMotion = !ReducedMotion;
        public void ToggleAudio() => Muted = !Muted;
        public void Play(int kind = 0)
        {
            if (Muted || !source || clips == null || kind < 0 || kind >= clips.Length) return;
            float now = Time.unscaledTime;
            if (now - lastPlayed[kind] < (kind == Hover ? .045f : .08f) && lastPlayed[kind] > 0) return;
            lastPlayed[kind] = now; source.PlayOneShot(clips[kind], volume[kind]);
        }
        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (clips != null) foreach (var clip in clips) if (clip) Destroy(clip);
        }
    }
}
