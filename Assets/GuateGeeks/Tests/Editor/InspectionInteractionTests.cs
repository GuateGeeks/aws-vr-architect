using System.Linq;
using NUnit.Framework;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class InspectionInteractionTests
    {
        [Test] public void IncrementalEntriesDeduplicateByIdentityNotMessageAndKeepLatestRevision()
        {
            var a=new AwsCloudApi.InspectionEntry {id="a",text="same",timestamp=1};
            var b=new AwsCloudApi.InspectionEntry {id="b",text="same",timestamp=2};
            var changed=new AwsCloudApi.InspectionEntry {id="a",text="changed",timestamp=3};
            var entries=InspectionTools.Merge(new[]{a,b},new[]{changed,b});
            Assert.AreEqual(2,entries.Length);Assert.AreEqual("changed",entries[0].text);
            Assert.AreEqual(100,InspectionTools.Merge(new AwsCloudApi.InspectionEntry[0],Enumerable.Range(0,200).Select(i=>new AwsCloudApi.InspectionEntry {id=i.ToString(),timestamp=i})).Length);
        }
        [Test] public void JsonFormattingPreservesEscapesLargeNumbersAndTruncatedInput()
        {
            const string json="{\"n\":12345678901234567890,\"message\":\"a, { \\\"quote\\\"\"}";
            string formatted=InspectionTools.Json(json);
            Assert.That(formatted,Does.Contain("12345678901234567890"));Assert.That(formatted,Does.Contain("a, { \\\"quote\\\""));
            Assert.That(formatted,Does.Contain("\n"));Assert.AreEqual("{\"cut\":",InspectionTools.Json("{\"cut\":"));
        }
        [Test] public void SearchCombinesSeverityAndExactCorrelation()
        {
            var entry=new AwsCloudApi.InspectionEntry {text="Pedido recibido",level="ERROR",eventId="event-1"};
            Assert.IsTrue(InspectionTools.Matches(entry,"pedido","ERROR","event-1"));
            Assert.IsFalse(InspectionTools.Matches(entry,"pedido","INFO","event-1"));
            Assert.IsFalse(InspectionTools.Matches(entry,"pedido","ERROR","event"));
        }
        [Test] public void TouchMustApproachDwellAndWithdrawAndNeverFiresOnTrackingRecovery()
        {
            var touch=new DirectTouchState(); var button=new object();
            Assert.IsFalse(touch.Step(button,0,true,1),"Appearing inside a button cannot activate it");
            Assert.IsFalse(touch.Step(button,.03f,true,.1f));
            Assert.IsFalse(touch.Step(button,0,true,.04f));Assert.IsTrue(touch.Step(button,0,true,.05f));
            Assert.IsFalse(touch.Step(button,0,true,1));
            touch.Reset();Assert.IsFalse(touch.Step(button,0,true,1));
            touch.Step(button,.03f,true,.1f); Assert.IsFalse(touch.Step(new object(),0,true,1),"Replacement UI must rearm");
            touch.Step(button,.03f,true,.1f);Assert.IsFalse(touch.Step(button,-.1f,true,1));
        }
    }
}
