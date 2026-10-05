using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class RealtimeVoiceStateTests
    {
        GameObject owner;RealtimeVoice voice;
        T Field<T>(string name)=>(T)typeof(RealtimeVoice).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(voice);
        void Event(string json){Field<Queue<RealtimeProtocol.Event>>("events").Enqueue(RealtimeProtocol.Parse(json));typeof(RealtimeVoice).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(voice,null);}
        [SetUp] public void Setup(){owner=new GameObject("Voice state test");voice=owner.AddComponent<RealtimeVoice>();typeof(RealtimeVoice).GetProperty("Connected").SetValue(voice,true);typeof(RealtimeVoice).GetProperty("Listening").SetValue(voice,true);}
        [TearDown] public void Cleanup(){Object.DestroyImmediate(owner);}
        [Test] public void SpeechInterruptsOldResponseWithoutStoppingContinuousCapture()
        {
            voice.Ask("first");Event("{\"type\":\"response.created\",\"response\":{\"id\":\"old\"}}");
            Field<HashSet<string>>("pendingTools").Add("obsolete");
            Event("{\"type\":\"input_audio_buffer.speech_started\"}");
            Assert.IsTrue(voice.Listening);Assert.IsFalse(voice.Responding);Assert.IsFalse(voice.ToolPending("obsolete"));
            Event("{\"type\":\"input_audio_buffer.committed\"}");Assert.IsTrue(voice.Responding);
            Event("{\"type\":\"response.created\",\"response\":{\"id\":\"new\"}}");
            Event("{\"type\":\"response.done\",\"response\":{\"id\":\"old\",\"status\":\"cancelled\"}}");Assert.IsTrue(voice.Responding);
            Event("{\"type\":\"response.output_audio_transcript.delta\",\"response_id\":\"new\",\"delta\":\"Current answer\"}");
            Assert.AreEqual("Current answer",voice.Caption);
            Event("{\"type\":\"response.done\",\"response\":{\"id\":\"new\",\"status\":\"completed\"}}");
            Assert.IsTrue(voice.Listening);Assert.IsFalse(voice.Responding);
        }
        [Test] public void PrivacyPauseAndManualDisconnectRejectLateSpeechTurns()
        {
            voice.SetCaptureSuspended(true);Assert.IsFalse(voice.Listening);
            Event("{\"type\":\"input_audio_buffer.committed\"}");Assert.IsFalse(voice.Responding);
            voice.SetCaptureSuspended(false);Event("{\"type\":\"input_audio_buffer.committed\"}");Assert.IsTrue(voice.Responding);
            voice.Disconnect();Event("{\"type\":\"input_audio_buffer.committed\"}");
            Assert.IsFalse(voice.Connected);Assert.IsFalse(voice.Listening);Assert.IsFalse(voice.Responding);
        }
        [Test] public void ToolContinuationKeepsCaptionUntilTheNextUserTurn()
        {
            voice.Ask("read context");Event("{\"type\":\"response.created\",\"response\":{\"id\":\"first\"}}");
            Event("{\"type\":\"response.output_audio_transcript.delta\",\"response_id\":\"first\",\"delta\":\"Checking the design.\"}");
            Field<HashSet<string>>("pendingTools").Add("context");voice.CompleteTool("context","{}");
            Event("{\"type\":\"response.created\",\"response\":{\"id\":\"continuation\"}}");
            Assert.AreEqual("Checking the design.",voice.Caption);
            voice.Ask("next");Assert.IsEmpty(voice.Caption);
        }
        [Test] public void FocusLossIgnoresLateAutomaticSpeechCommit()
        {
            typeof(RealtimeVoice).GetMethod("OnApplicationFocus",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(voice,new object[]{false});
            Event("{\"type\":\"input_audio_buffer.committed\"}");
            Assert.IsFalse(voice.Listening);Assert.IsFalse(voice.Responding);
        }
        [Test] public void CompletedResponseRecoversCallsAndContinuesAfterAllResultsOnce()
        {
            voice.Ask("fetch items");Event("{\"type\":\"response.created\",\"response\":{\"id\":\"lookup\"}}");
            int dispatched=0;voice.ToolCall=(id,name,args)=>dispatched++;
            Event("{\"type\":\"response.function_call_arguments.done\",\"response_id\":\"lookup\",\"call_id\":\"one\",\"name\":\"get_context\",\"arguments\":\"{}\"}");
            Event("{\"type\":\"response.done\",\"response\":{\"id\":\"lookup\",\"status\":\"completed\",\"output\":[{\"type\":\"function_call\",\"call_id\":\"one\",\"name\":\"get_context\",\"arguments\":\"{}\"},{\"type\":\"function_call\",\"call_id\":\"two\",\"name\":\"read_component\",\"arguments\":\"{}\"}]}}");
            Assert.AreEqual(2,dispatched);Assert.IsFalse(voice.Responding);
            voice.CompleteTool("one","{}");Assert.IsFalse(voice.Responding);
            voice.CompleteTool("two","{}");Assert.IsTrue(voice.Responding);
            Assert.AreEqual(1,Field<Queue<int>>("requestedTurns").Count);
            voice.CompleteTool("two","{}");Assert.AreEqual(1,Field<Queue<int>>("requestedTurns").Count);
            Event("{\"type\":\"response.created\",\"response\":{\"id\":\"silent\"}}");
            Event("{\"type\":\"response.done\",\"response\":{\"id\":\"silent\",\"status\":\"completed\"}}");
            Assert.IsTrue(voice.Responding,"Silent tool completion should automatically request an audible answer");
            Event("{\"type\":\"response.created\",\"response\":{\"id\":\"recovery\"}}");
            Event("{\"type\":\"response.done\",\"response\":{\"id\":\"recovery\",\"status\":\"completed\"}}");
            Assert.IsFalse(voice.Responding,"Recovery must not loop indefinitely");
        }
        [Test] public void SpeechAndPlaybackEventsExposeStateAndMeasureFirstAudioOnce()
        {
            int started=0,committed=0;voice.SpeechStarted=()=>started++;voice.InputCommitted=()=>committed++;
            Event("{\"type\":\"input_audio_buffer.speech_started\"}");Assert.IsTrue(voice.UserSpeaking);Assert.AreEqual(1,started);
            Event("{\"type\":\"input_audio_buffer.speech_stopped\"}");Event("{\"type\":\"input_audio_buffer.committed\"}");
            Assert.IsFalse(voice.UserSpeaking);Assert.AreEqual(1,committed);Assert.AreEqual(1,voice.AudioTurns);
            Event("{\"type\":\"output_audio_buffer.started\"}");Assert.IsTrue(voice.Speaking);Assert.GreaterOrEqual(voice.LastFirstAudioMs,0);
            Event("{\"type\":\"output_audio_buffer.stopped\"}");Assert.IsFalse(voice.Speaking);Assert.IsTrue(voice.Listening);
        }
    }
}
