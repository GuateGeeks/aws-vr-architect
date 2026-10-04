using System;
using System.IO;
using NUnit.Framework;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class AuthoringTests
    {
        [Test] public void DraftValidationAndTestingBindToExactSourceAndEvent()
        {
            var draft=new LambdaCodeDraft{nodeId="fn"};draft.validatedHash=LambdaCodeDraft.Hash(draft.source);draft.testedHash=LambdaCodeDraft.Hash(draft.source+"\n"+draft.eventJson);
            Assert.IsTrue(draft.Validated);Assert.IsTrue(draft.Tested);draft.eventJson="{}";Assert.IsFalse(draft.Tested);Assert.IsTrue(draft.Validated);
            draft.Edit(draft.source+"\n# local edit");Assert.IsFalse(draft.Validated);Assert.That(draft.Difference(),Does.Contain("# local edit"));
            Assert.Throws<ArgumentException>(()=>draft.Edit(new string('á',4097)));
        }
        [Test] public void SavedDraftRestoresExactWhitespaceButNeverRestoresValidationTrust()
        {
            string path=Path.Combine(Path.GetTempPath(),"atlas-draft-"+Guid.NewGuid().ToString("N"));
            try {
                var draft=new LambdaCodeDraft{nodeId="fn",source="def handler(event, context):\n    return event\n"};draft.validatedHash=LambdaCodeDraft.Hash(draft.source);
                draft.Save(path,"test");draft.Edit(draft.source+"\n# saved again");draft.Save(path,"test");var loaded=LambdaCodeDraft.Load(path,"test","fn");
                Assert.AreEqual(draft.source,loaded.source);Assert.IsFalse(loaded.Validated);Assert.AreEqual(LambdaCodeDraft.Starter,LambdaCodeDraft.Load(path,"test","other").source);
            }finally{if(Directory.Exists(path))Directory.Delete(path,true);}
        }
        [Test] public void DiagnosticsCorrelateOnlyRequestedEventAndRedactBoundedEvidence()
        {
            var report=EventDiagnostics.Summarize("fn","event1",new AwsCloudApi.InspectionPage{cursor="more",entries=new[]{
                new AwsCloudApi.InspectionEntry{eventId="other",level="ERROR",text="ignore"},
                new AwsCloudApi.InspectionEntry{eventId="event1",stage="delivered",text="ok"},
                new AwsCloudApi.InspectionEntry{eventId="event1",level="ERROR",text="Bearer private-token "+new string('x',1000)}}});
            Assert.AreEqual(2,report.records);Assert.AreEqual(1,report.errors);Assert.AreEqual(1,report.deliveries);Assert.IsTrue(report.partial);
            Assert.That(string.Join(" ",report.sample),Does.Not.Contain("private-token"));Assert.LessOrEqual(report.sample[0].Length,701);
        }
        [Test] public void EmptyEvidenceNeverClaimsFailureOrSuccess()
        {
            var report=EventDiagnostics.Summarize("fn","e",new AwsCloudApi.InspectionPage{entries=Array.Empty<AwsCloudApi.InspectionEntry>()});
            Assert.AreEqual(0,report.errors);Assert.That(report.message,Does.Contain("no demuestra un fallo"));
        }
    }
}
