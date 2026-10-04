using UnityEngine;

namespace GuateGeeks.AwsVr
{
    public sealed class LabFeedback : MonoBehaviour
    {
        public static LabFeedback Current { get; private set; }
        public static float Clock { get; private set; }
        public bool ReducedMotion { get; private set; }
        public bool Muted { get; private set; }
        AudioSource source;
        AudioClip select, success, error;
        void Awake()
        {
            Current = this; Clock = 0;
            source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false; source.volume = .13f;
            select = Tone(660, .055f); success = Tone(1040, .17f); error = Tone(190, .12f);
        }
        static AudioClip Tone(float frequency, float duration)
        {
            int count = Mathf.CeilToInt(duration * 22050); var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / count;
                samples[i] = Mathf.Sin(2 * Mathf.PI * frequency * i / 22050) * Mathf.Sin(t * Mathf.PI) * (1 - t);
            }
            var clip = AudioClip.Create("Synthesized interface tone", count, 1, 22050, false); clip.SetData(samples, 0); return clip;
        }
        void Update() { if (!ReducedMotion) Clock += Time.unscaledDeltaTime; Shader.SetGlobalFloat("_LabAnimationTime", Clock); }
        public void ToggleMotion() => ReducedMotion = !ReducedMotion;
        public void ToggleAudio() => Muted = !Muted;
        public void Play(int kind = 0) { if (!Muted && source) source.PlayOneShot(kind == 1 ? success : kind == 2 ? error : select); }
        void OnDestroy()
        {
            if (Current == this) Current = null;
            Destroy(select); Destroy(success); Destroy(error);
        }
    }
}
