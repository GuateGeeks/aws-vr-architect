using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Reflection;
using System;
using Unity.WebRTC;
using System.Text;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class RealtimeLiveTests
    {
        // Explicit opt-in: consumes one broker session and a short paid provider response.
        [UnityTest, Category("LiveOpenAI"), Explicit("Requires a fresh broker ticket supplied after the ready marker.")]
        public IEnumerator NativeWebRtcCompletesToolAndAudioResponse()
        {
            const string path="Validation/assistant-smoke-ticket.json";
            var owner=new GameObject("Realtime live validation");var voice=owner.AddComponent<RealtimeVoice>();
            if(!UnityEngine.Object.FindAnyObjectByType<AudioListener>())owner.AddComponent<AudioListener>();
            string status="";voice.Status=value=>status=value;
            string transcript="";voice.UserTranscript=value=>transcript=value;
            float lastCommitted=-1;voice.InputCommitted=()=>lastCommitted=Time.realtimeSinceStartup;
            bool contextRead=false;int edits=0;float appliedScale=1;
            var report=new StringBuilder("# Live voice comparison\n\nSynthetic speech through native WebRTC; one sample per mode, not a latency benchmark.\n\n");
            voice.ToolCall=(id,name,args)=>{
                if(name=="get_context")contextRead=true;
                if(name=="set_component_size"){edits++;appliedScale=JsonUtility.FromJson<SizeRequest>(args).scale;File.AppendAllText("Validation/voice-correction-actions.txt","pending="+((System.Collections.Generic.HashSet<string>)typeof(RealtimeVoice).GetField("pendingTools",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(voice)).Count+"; transcript="+transcript+"; args="+args+"\n");}
                voice.CompleteTool(id,name=="get_context"?"{\"mode\":\"SIMULATION\",\"revision\":1,\"architecture\":{\"nodes\":[{\"id\":\"lambda1\",\"name\":\"Processor\",\"kind\":1,\"setting\":0}],\"links\":[]},\"componentScale\":"+appliedScale.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"deployed\":false,\"availableActions\":[\"set_component_size\"]}":name=="set_component_size"?"{\"status\":\"applied\",\"revision\":1,\"componentScale\":"+appliedScale.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}":"{\"status\":\"unsupported_in_test\"}");
            };
            try {
                File.WriteAllText("Validation/voice-correction-actions.txt","");
                File.WriteAllText("Validation/assistant-smoke-ready.txt","ready");
                float deadline=Time.realtimeSinceStartup+90;
                while(!File.Exists(path) && Time.realtimeSinceStartup<deadline)yield return null;
                Assert.IsTrue(File.Exists(path),"No fresh broker ticket arrived.");
                var ticket=JsonUtility.FromJson<AwsCloudApi.AssistantSession>(File.ReadAllText(path));File.Delete(path);
                report.AppendLine("Model: "+ticket.model+". Cost estimates include received usage, not an invoice.\n");
                voice.Begin(ticket,false);deadline=Time.realtimeSinceStartup+40;
                while(!voice.Connected && Time.realtimeSinceStartup<deadline)yield return null;
                Assert.IsTrue(voice.Connected,"Native WebRTC connection: "+status);
                // Synthesized speech fixture traverses the exact PCM reader, audio-thread sender,
                // native WebRTC and automatic VAD used by the Quest microphone.
                // No microphone is opened and no typed user request is sent by this test.
                var input=(RealtimeAudioInput)typeof(RealtimeVoice).GetField("audioInput",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(voice);
                var track=(AudioStreamTrack)typeof(RealtimeVoice).GetField("microphoneTrack",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(voice);
                for(int utterance=0;utterance<3;utterance++) {
                bool natural=utterance!=0;int updates=voice.SessionUpdates;voice.ConfigureTurnDetection(natural);
                deadline=Time.realtimeSinceStartup+10;while(voice.SessionUpdates<=updates && Time.realtimeSinceStartup<deadline)yield return null;
                Assert.Greater(voice.SessionUpdates,updates,"Turn detection update must be acknowledged.");
                var samples=ReadWave(utterance==2?"Validation/atlas-correction-fixture.wav":"Validation/atlas-voice-fixture.wav");
                var clip=AudioClip.Create("Synthetic speech test",samples.Length+48000*12,1,48000,false);clip.SetData(samples,0);
                transcript="";contextRead=false;
                int turns=voice.AudioTurns;
                input.Begin(clip);track.Enabled=true;
                float started=Time.realtimeSinceStartup;
                while(lastCommitted<started+samples.Length/48000f && Time.realtimeSinceStartup-started<clip.length-.1f) {
                    input.ReadAvailable(Math.Min(clip.samples-1,(int)((Time.realtimeSinceStartup-started)*48000)));yield return null;
                }
                float endToCommitMs=(Time.realtimeSinceStartup-started-samples.Length/48000f)*1000;
                Assert.Greater(input.SentSamples,48000,"Audio-thread sender must send the fixture.");
                Assert.Greater(voice.AudioTurns,turns,"Automatic VAD must commit without a button.");
                input.FinishCapture();
                deadline=Time.realtimeSinceStartup+55;
                while((!contextRead || voice.Responding || voice.Speaking || voice.LastFirstAudioMs<0 || string.IsNullOrEmpty(voice.Caption) || string.IsNullOrEmpty(transcript)) && Time.realtimeSinceStartup<deadline)yield return null;
                if(utterance<2)Assert.That(transcript.ToLowerInvariant(),Does.Contain("context"),"Provider must transcribe the spoken fixture. "+status);
                else {Assert.AreEqual(1,edits,"Apply only the final corrected value.");Assert.AreEqual(.75f,appliedScale);}
                Assert.IsTrue(contextRead,"Provider must complete a context tool call. "+status);
                Assert.IsNotEmpty(voice.Caption,"Expected the audio response transcript. "+status);
                Assert.GreaterOrEqual(voice.LastFirstAudioMs,0,"Expected actual output audio start.");
                Assert.Greater(voice.TotalTokens,0,"Expected provider usage.");
                Assert.IsTrue(track.Enabled,"The continuous audio track stays enabled between turns.");
                report.AppendLine($"- {(natural?"Natural":"Fast")}, {(utterance==2?"Spanish correction":"English context")}: fixture end → commit {endToCommitMs:F0} ms; commit → first audio {voice.LastFirstAudioMs:F0} ms; passed.");
                report.AppendLine("  Cost so far: "+voice.UsageCost.Summary+"; audio in/out="+voice.UsageCost.AudioInput+"/"+voice.UsageCost.AudioOutput+"; cached audio/text="+voice.UsageCost.CachedAudio+"/"+voice.UsageCost.CachedText+"; transcription tokens="+voice.UsageCost.TranscriptionTokens);
                File.WriteAllText("Validation/voice-turn-comparison.md",report.ToString());
                UnityEngine.Object.Destroy(clip);
                }
                voice.Disconnect();Assert.IsFalse(voice.Connected);
            } finally {
                if(File.Exists(path))File.Delete(path);
                File.Delete("Validation/assistant-smoke-ready.txt");
                UnityEngine.Object.Destroy(owner);
            }
        }
        [Serializable] sealed class SizeRequest {public float scale;}
        static float[] ReadWave(string path)
        {
            using(var reader=new BinaryReader(File.OpenRead(path))) {
                Assert.AreEqual("RIFF",new string(reader.ReadChars(4)));reader.ReadInt32();reader.ReadChars(4);
                while(reader.BaseStream.Position<reader.BaseStream.Length) {
                    string chunk=new string(reader.ReadChars(4));int length=reader.ReadInt32();long next=reader.BaseStream.Position+length+(length&1);
                    if(chunk=="fmt "){Assert.AreEqual(1,reader.ReadInt16());Assert.AreEqual(1,reader.ReadInt16());Assert.AreEqual(48000,reader.ReadInt32());}
                    else if(chunk=="data"){var values=new float[length/2];for(int i=0;i<values.Length;i++)values[i]=reader.ReadInt16()/32768f;return values;}
                    reader.BaseStream.Position=next;
                }
            }
            throw new InvalidDataException("No PCM fixture data.");
        }
    }
}
