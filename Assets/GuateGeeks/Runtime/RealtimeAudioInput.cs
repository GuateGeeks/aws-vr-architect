using System;
using UnityEngine;
using Unity.WebRTC;

namespace GuateGeeks.AwsVr
{
    // Microphone write cursor -> bounded PCM queue -> WebRTC on Unity's audio thread.
    // Do not play the microphone through an AudioSource: its playback cursor can overtake
    // Android capture and read unwritten samples; silent sources can also be virtualized.
    [RequireComponent(typeof(AudioListener))]
    public sealed class RealtimeAudioInput : MonoBehaviour
    {
        public const int SampleRate=48000, FrameSamples=480;
        readonly object gate=new object();
        readonly float[][] frames=new float[128][];
        AudioStreamTrack track;
        AudioClip clip;
        float[] capture;
        int cursor,head,tail,count,captured,sent;
        float peak,level;
        bool overflow;
        public int CapturedSamples {get {lock(gate)return captured;}}
        public int SentSamples {get {lock(gate)return sent;}}
        public float Peak {get {lock(gate)return peak;}}
        public float Level {get {lock(gate)return level;}}
        public bool Overflow {get {lock(gate)return overflow;}}
        public bool Drained {get {lock(gate)return count==0;}}
        void Awake(){for(int i=0;i<frames.Length;i++)frames[i]=new float[FrameSamples];}
        public void Attach(AudioStreamTrack value){lock(gate)track=value;}
        public void Begin(AudioClip value)
        {
            if(!value || value.frequency!=SampleRate)throw new ArgumentException("Se requiere captura PCM a 48 kHz.");
            Cancel();clip=value;capture=new float[FrameSamples*clip.channels];cursor=0;
            lock(gate){captured=sent=0;peak=level=0;overflow=false;}
        }
        // Called on the main thread. AudioClip.GetData handles reads across the clip's wrap point.
        public void ReadAvailable(int writePosition)
        {
            if(!clip || writePosition<0)return;
            int available=(writePosition-cursor+clip.samples)%clip.samples;
            while(available>=FrameSamples) {
                if(!clip.GetData(capture,cursor))throw new InvalidOperationException("No se pudo leer la captura del micrófono.");
                lock(gate) {
                    if(count==frames.Length){overflow=true;return;}
                    var frame=frames[tail];float energy=0;
                    for(int i=0;i<FrameSamples;i++) {
                        float sample=0;for(int channel=0;channel<clip.channels;channel++)sample+=capture[i*clip.channels+channel];
                        sample/=clip.channels;frame[i]=sample;energy+=sample*sample;peak=Math.Max(peak,Math.Abs(sample));
                    }
                    level=(float)Math.Sqrt(energy/FrameSamples);captured+=FrameSamples;
                    tail=(tail+1)%frames.Length;count++;
                }
                cursor=(cursor+FrameSamples)%clip.samples;available-=FrameSamples;
            }
        }
        void OnAudioFilterRead(float[] output,int channels)
        {
            // Never include the listener mix (assistant speech/UI sounds) in model input.
            lock(gate) {
                while(track!=null && count>0) {
                    track.SetData(frames[head],1,SampleRate);
                    sent+=FrameSamples;head=(head+1)%frames.Length;count--;
                }
            }
        }
        public void FinishCapture(){clip=null;capture=null;}
        public void Cancel(){lock(gate){head=tail=count=0;level=0;}FinishCapture();}
        public void Detach(){lock(gate){track=null;head=tail=count=0;}FinishCapture();}
        void OnDestroy(){Detach();}
    }
}
