using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class RealtimeAudioInputTests
    {
        [UnityTest] public IEnumerator ReadsOnlyRecordedFramesAndHandlesMicrophoneWrap()
        {
            var owner=new GameObject("PCM capture test");owner.SetActive(false);
            var input=owner.AddComponent<RealtimeAudioInput>();owner.SetActive(true);input.enabled=false;
            var clip=AudioClip.Create("ring",1920,1,48000,false);var samples=new float[1920];
            for(int i=0;i<samples.Length;i++)samples[i]=.25f;clip.SetData(samples,0);
            input.Begin(clip);input.ReadAvailable(479);Assert.AreEqual(0,input.CapturedSamples);
            input.ReadAvailable(1440);Assert.AreEqual(1440,input.CapturedSamples);
            input.ReadAvailable(480);Assert.AreEqual(2400,input.CapturedSamples,"Read exactly the two newly recorded frames across wrap.");
            Assert.AreEqual(.25f,input.Peak,.001f);Assert.AreEqual(.25f,input.Level,.001f);
            Assert.AreEqual(0,input.SentSamples,"Reading samples is not proof of transmission.");
            input.Cancel();Assert.IsTrue(input.Drained);input.ReadAvailable(960);Assert.AreEqual(2400,input.CapturedSamples);
            Object.Destroy(owner);Object.Destroy(clip);yield return null;
        }
        [UnityTest] public IEnumerator SilenceIsReportedAndQueueOverflowCannotGrowUnbounded()
        {
            var owner=new GameObject("PCM bounded queue test");var input=owner.AddComponent<RealtimeAudioInput>();input.enabled=false;
            var clip=AudioClip.Create("silence",96000,1,48000,false);
            input.Begin(clip);input.ReadAvailable(72000);Assert.IsTrue(input.Overflow);
            Assert.AreEqual(128*480,input.CapturedSamples);Assert.AreEqual(0,input.Peak);
            input.Begin(clip);Assert.IsFalse(input.Overflow);Assert.AreEqual(0,input.CapturedSamples);Assert.IsTrue(input.Drained);
            Object.Destroy(owner);Object.Destroy(clip);yield return null;
        }
    }
}
