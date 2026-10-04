using System;
using NUnit.Framework;
using UnityEngine;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class AssistantTests
    {
        static AssistantProposal Proposal(Architecture graph,int revision=3)=>new AssistantProposal{baseRevision=revision,nodes=graph.nodes.ToArray(),links=graph.links.ToArray()};
        [Test] public void ProposalRejectsStaleRevisionAndInvalidGraphWithoutChangingSource()
        {
            var graph=Architecture.Preset(0);var original=JsonUtility.ToJson(graph);
            Assert.Throws<ArgumentException>(()=>Proposal(graph).Build(graph,4));
            var bad=Architecture.Preset(0);bad.links.Clear();Assert.Throws<ArgumentException>(()=>Proposal(bad).Build(graph,3));
            bad=Architecture.Preset(0);bad.nodes[0].id="../../bad";Assert.Throws<ArgumentException>(()=>Proposal(bad).Build(graph,3));
            bad=Architecture.Preset(1);bad.nodes[0].setting=1;Assert.Throws<ArgumentException>(()=>Proposal(bad).Build(graph,3));
            Assert.AreEqual(original,JsonUtility.ToJson(graph));
        }
        [Test] public void ProposalCopiesDataPreservesExistingLayoutAndProducesConcreteDiff()
        {
            var graph=Architecture.Preset(0);var next=graph.Copy();next.nodes[1].name="Procesar pedidos";next.nodes[1].setting=2;
            var built=Proposal(next).Build(graph,3);Assert.AreEqual(graph.nodes[1].position,built.nodes[1].position);
            built.nodes[0].name="changed";Assert.AreNotEqual(built.nodes[0].name,next.nodes[0].name);
            Assert.That(AssistantProposal.Difference(graph,built),Does.Contain("Procesar pedidos"));
        }
        [Test] public void ProposalNeverUsesModelCoordinatesAndRejectsRichTextNames()
        {
            var current=Architecture.Preset(0);var next=Architecture.Preset(1);next.nodes[0].position=new Vector3(float.NaN,0,0);
            var built=Proposal(next).Build(current,3);Assert.IsFalse(float.IsNaN(built.nodes[0].position.x));Assert.IsEmpty(built.Validate());
            next.nodes[0].name="<size=999>spoof</size>";Assert.Throws<ArgumentException>(()=>Proposal(next).Build(current,3));
        }
        [Test] public void ProviderMessagesAreEscapedAndDoNotIncludeInapplicableFields()
        {
            string json=RealtimeProtocol.ToolOutput("call-1","{\"status\":\"ok\"}");
            Assert.That(json,Does.Contain("function_call_output"));Assert.That(json,Does.Not.Contain("\"role\""));Assert.That(json,Does.Not.Contain("\"content\""));
            var text=RealtimeProtocol.UserText("Hola \"ATLAS\"\n");Assert.That(text,Does.Not.Contain("call_id"));Assert.That(text,Does.Contain("\\n"));
            Assert.Throws<ArgumentException>(()=>RealtimeProtocol.Parse(new string('x',131073)));
        }
        [Test] public void SecretsAreRedactedBeforeInspectionDataReachesTheAssistant()
        {
            string raw="{\"password\":\"hunter2\",\"token\":\"abc\",\"message\":\"sk-test-not-a-real-key AKIA1234567890123456 Bearer private-token\"}";
            string redacted=ArchitectureLab.RedactAssistantData(raw);
            foreach(string secret in new[]{"hunter2","abc","sk-test","AKIA123","private-token"})Assert.That(redacted,Does.Not.Contain(secret));
            Assert.That(redacted,Does.Contain("REDACTED"));
        }
    }
}
