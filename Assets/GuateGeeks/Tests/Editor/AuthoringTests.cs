using System;
using System.IO;
using NUnit.Framework;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class AuthoringTests
    {
        [Test] public void InsertedLineDoesNotMarkUnchangedCodeAsReplaced()
        {
            var diff=CodePresentation.Difference("a\nb\nc", "a\ninserted\nb\nc");
            Assert.That(diff,Does.Contain("+ — / 2  inserted"));
            Assert.That(diff,Does.Not.Contain("\n− "));Assert.That(diff,Does.Contain("2 / 3  b"));
        }
        [Test] public void HighlightEscapesMarkupAndColorsPython()
        {
            var text=CodePresentation.Highlight("return '<size=99>x</size>' # comment");
            Assert.That(text,Does.Contain("<noparse><</noparse>size=99>"));Assert.That(text,Does.Not.Contain("<size=99>"));
            Assert.That(text,Does.Contain("<color=#C5A0FF>return</color>"));
        }
        [Test] public void ExpectedOutputInvalidatesTestTrustAndCasesSurviveSave()
        {
            string path=Path.Combine(Path.GetTempPath(),"atlas-cases-"+Guid.NewGuid().ToString("N"));
            try {
                var draft=new LambdaCodeDraft{nodeId="fn",expectedOutput="42",testCases=new[]{new LambdaCodeDraft.TestCase{name="double",eventJson="{\"amount\":21}",expectedOutput="42"}}};
                draft.testedHash=draft.TestHash;Assert.IsTrue(draft.Tested);draft.expectedOutput="43";Assert.IsFalse(draft.Tested);
                draft.Save(path,"id");var loaded=LambdaCodeDraft.Load(path,"id","fn");Assert.AreEqual("42",loaded.testCases[0].expectedOutput);
            }finally{if(Directory.Exists(path))Directory.Delete(path,true);}
        }
        [Test] public void RetrievalUsesCurrentLinksAndDropsDeletedComponents()
        {
            var graph=Architecture.Preset(0);string fn=graph.nodes[1].id;
            var entries=IntegrationKnowledge.Retrieve(graph,fn);Assert.AreEqual(2,entries.Length);
            Assert.That(entries[1].example+entries[0].example,Does.Contain("TARGETS"));
            graph.Remove(fn);Assert.IsEmpty(IntegrationKnowledge.Retrieve(graph,fn));
        }
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
