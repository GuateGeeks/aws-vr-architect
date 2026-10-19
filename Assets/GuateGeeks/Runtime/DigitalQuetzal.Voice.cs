using System;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // The quetzal's sounds as shipped in 0.24.3/0.24.4, synthesised at start exactly as the production code did
    // (Recovery/0.24.3-qa/QUETZAL-REFERENCE.md, "Sounds"): seven clips at 22050 Hz played with PlayOneShot on one 3D
    // source (volume 0.5, logarithmic rolloff from 0.6 m to 10 m, no doppler).
    // - call (1.0): two whistled, falling notes, when it is called and when it notices a pointing hand;
    // - chirp (0.7): two quick notes, rising then falling, on touchdown, an excursion and a tap on a wing or the tail;
    // - trill (0.5): six rapid notes, when petted;
    // - coo (0.45): two soft low notes, while contentedly stroked;
    // - squeak (0.6): a sharp rising-falling squeak, when startled or shooed;
    // - tap (0.6): the click of its bill, when it pecks;
    // - flutter (0.55): four wingbeats, when it takes off from the arm.
    public sealed partial class DigitalQuetzal
    {
        const int VoiceRate = 22050;
        AudioSource voice;
        AudioClip call, chirp, trill, coo, squeak, tap, flutter;
        float lastSound = -1;

        void BuildVoice()
        {
            var go = new GameObject("Quetzal voice"); go.transform.SetParent(transform, false);
            voice = go.AddComponent<AudioSource>(); voice.playOnAwake = false; voice.spatialBlend = 1;
            voice.rolloffMode = AudioRolloffMode.Logarithmic; voice.minDistance = .6f; voice.maxDistance = 10; voice.dopplerLevel = 0; voice.volume = .5f;
            call = Clip("Quetzal call", Call()); chirp = Clip("Quetzal chirp", Chirp()); trill = Clip("Quetzal trill", Trill());
            coo = Clip("Quetzal coo", Coo()); squeak = Clip("Quetzal squeak", Squeak()); tap = Clip("Quetzal tap", Tap()); flutter = Clip("Quetzal flutter", Flutter());
        }
        AudioClip Clip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, VoiceRate, false); clip.SetData(samples, 0); owned.Add(clip); return clip;
        }
        bool Quiet => mutedPreview || LabFeedback.Current && LabFeedback.Current.Muted;
        void Say(AudioClip clip, float volume)
        {
            // One sound per frame at most (the shipped code played each event once; some events here fire from loops).
            if (Quiet || !clip || elapsed - lastSound < .03f) return;
            voice.transform.position = head ? head.position : bird.position;
            voice.PlayOneShot(clip, volume); lastSound = elapsed;
        }
        void VoiceCall() => Say(call, 1);
        void VoiceChirp() => Say(chirp, .7f);
        void VoiceTrill() => Say(trill, .5f);
        void VoiceCoo() => Say(coo, .45f);
        void VoiceSqueak() => Say(squeak, .6f);
        void VoiceTap() => Say(tap, .6f);
        void VoiceFlutter() => Say(flutter, .55f);

        // Every sound by name (VoiceRate samples per second), for auditioning outside the headset.
        public static System.Collections.Generic.IEnumerable<(string name, float[] samples)> SoundBank()
        {
            yield return ("call", Call()); yield return ("chirp", Chirp()); yield return ("trill", Trill()); yield return ("coo", Coo());
            yield return ("squeak", Squeak()); yield return ("tap", Tap()); yield return ("flutter", Flutter());
        }
        public static int SoundRate => VoiceRate;
        static float[] Samples(float seconds) => new float[(int)Math.Floor(seconds * VoiceRate + .5f)];
        static int Round(float x) => (int)Math.Round(x, MidpointRounding.AwayFromZero);
        // A whistled note gliding from one pitch to another: smoothstep glide, fast attack and long decay, a 26 Hz vibrato
        // of 1.2%, and two quiet overtones.
        static void Whistle(float[] s, float start, float length, float from, float to, float level)
        {
            int first = Round(start * VoiceRate), count = Round(length * VoiceRate);
            float phase = 0;
            for (int i = 0; i < count && first + i < s.Length; i++)
            {
                float u = i / (float)count, ease = Mathf.Clamp01(u * u * (3 - 2 * u));
                float envelope = Mathf.Min(1, 9 * u) * Mathf.Pow(1 - u, 1.4f);
                float t = i / (float)VoiceRate;
                float f = (from + (to - from) * ease) * (1 + .012f * Mathf.Sin(t * 163.363f));
                phase += f * 6.28319f / VoiceRate;
                s[first + i] += (Mathf.Sin(phase) + .16f * Mathf.Sin(2 * phase) + .05f * Mathf.Sin(3.01f * phase)) * envelope * level;
            }
        }
        static float[] Call()
        {
            var s = Samples(.75f);
            Whistle(s, 0, .26f, 1380, 1060, .5f);
            Whistle(s, .38f, .3f, 1420, 1080, .55f);
            return s;
        }
        static float[] Chirp()
        {
            var s = Samples(.2f);
            Whistle(s, 0, .08f, 1700, 2350, .4f);
            Whistle(s, .1f, .07f, 1600, 1250, .35f);
            return s;
        }
        static float[] Trill()
        {
            var s = Samples(.32f);
            for (int i = 0; i < 6; i++) Whistle(s, i * .048f, .044f, i % 2 == 0 ? 1950 : 2200, i % 2 == 0 ? 2050 : 2120, .22f);
            return s;
        }
        static float[] Coo()
        {
            var s = Samples(.5f);
            Whistle(s, 0, .18f, 990, 900, .3f);
            Whistle(s, .22f, .24f, 1050, 920, .32f);
            return s;
        }
        static float[] Squeak()
        {
            var s = Samples(.18f);
            Whistle(s, 0, .07f, 2100, 3000, .42f);
            Whistle(s, .075f, .065f, 2850, 2300, .34f);
            return s;
        }
        // Deterministic noise: xorshift32, as -1..1.
        struct XorShift
        {
            uint x;
            public XorShift(uint seed) { x = seed; }
            public float Next() { x ^= x << 13; x ^= x >> 17; x ^= x << 5; return (x >> 8) / (float)(1 << 23) - 1; }
        }
        // Bill click: a burst of noise and a short 3.2 kHz ring.
        static float[] Tap()
        {
            var s = Samples(.05f); var random = new XorShift(0x92d68ca2);
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)VoiceRate;
                s[i] = random.Next() * Mathf.Exp(-900 * t) * .5f + Mathf.Sin(20106.2f * t) * Mathf.Exp(-120 * t) * .25f;
            }
            return s;
        }
        // Four wingbeats, 75 ms apart: soft low-passed puffs of air, each 15% quieter.
        static float[] Flutter()
        {
            var s = Samples(.36f); var random = new XorShift(0x05416865);
            for (int beat = 0; beat < 4; beat++)
            {
                int first = Round(beat * .075f * VoiceRate); const int n = 1323; float low = 0, loud = 1 - .15f * beat;
                for (int j = 0; j < n && first + j < s.Length; j++)
                {
                    low += (random.Next() - low) * .12f;
                    s[first + j] += Mathf.Sin(Mathf.PI * j / n) * low * loud * 1.6f;
                }
            }
            return s;
        }
    }
}
