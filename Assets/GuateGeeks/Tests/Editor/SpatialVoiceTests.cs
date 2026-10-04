using NUnit.Framework;
using UnityEngine;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class SpatialVoiceTests
    {
        [Test] public void DownwardRaysFromBelowTheHologramsHitTheVisibleTable()
        {
            var workspace=new GameObject("Workspace");
            try {
                var point=new Vector3(.8f,ArchitectureLab.VoiceTableHeight,2.8f);
                foreach(float height in new[]{1.1f,1.3f,1.7f}) {
                    var origin=new Vector3(0,height,.3f);var ray=new Ray(origin,point-origin);
                    Assert.IsTrue(ArchitectureLab.TryVoiceTableDestination(workspace.transform,ray,out var destination));
                    Assert.Less(Vector3.Distance(new Vector3(.8f,1.5f,2.8f),destination),.0001f);
                }
                workspace.transform.SetPositionAndRotation(new Vector3(3,1,-2),Quaternion.Euler(0,40,0));
                var transformed=new Ray(workspace.transform.TransformPoint(new Vector3(0,1.2f,.3f)),workspace.transform.TransformDirection(point-new Vector3(0,1.2f,.3f)));
                Assert.IsTrue(ArchitectureLab.TryVoiceTableDestination(workspace.transform,transformed,out var local));Assert.Less(Vector3.Distance(new Vector3(.8f,1.5f,2.8f),local),.0001f);
                Assert.IsFalse(ArchitectureLab.TryVoiceTableDestination(workspace.transform,new Ray(Vector3.zero,Vector3.up),out _));
            } finally {Object.DestroyImmediate(workspace);}
        }
        [Test] public void DestinationSurvivesLoweredHandDuringSemanticWaitButNotTheNextUtterance()
        {
            var context=new SpatialVoiceContext();var point=new Vector3(.8f,1.5f,2.8f);
            context.Observe("right",null,point,0);context.Observe("right",null,point,.4f);context.BeginSpeech(.5f);
            context.Observe("right",null,null,1);context.Freeze(8);
            Assert.IsTrue(context.Read(8).hasLocation);Assert.AreEqual(point,context.Read(8).location);
            context.BeginSpeech(9);context.Freeze(10);Assert.IsFalse(context.Read(10).hasLocation);
        }
        [Test] public void LivePreviewDoesNotReuseThePreviousFrozenDestination()
        {
            var context=new SpatialVoiceContext();var first=new Vector3(0,1.5f,2);var next=new Vector3(1,1.5f,3);
            context.Observe("right",null,first,0);context.Observe("right",null,first,.4f);context.Freeze(.5f);
            context.Observe("right",null,next,1);context.Observe("right",null,next,1.4f);
            Assert.AreEqual(first,context.Read(1.5f).location);Assert.AreEqual(next,context.Preview(1.5f).location);
        }
        [Test] public void RetainedConflictingDestinationsRemainAmbiguous()
        {
            var context=new SpatialVoiceContext();context.BeginSpeech(0);
            foreach(string hand in new[]{"left","right"}) {
                var point=new Vector3(hand=="left"?0:1,1.5f,2);
                context.Observe(hand,null,point,0);context.Observe(hand,null,point,.4f);context.Observe(hand,null,null,.5f);
            }
            context.Freeze(4);Assert.IsFalse(context.Read(4).hasLocation);Assert.IsTrue(context.Read(4).ambiguous);
        }
        [Test] public void SpeechKeepsOrderedStableTargetsAfterThePointerMoves()
        {
            var context=new SpatialVoiceContext();context.Observe("right","api",null,0);context.Observe("right","api",null,.4f);
            context.BeginSpeech(.5f);context.Observe("right","lambda",null,.6f);context.Observe("right","lambda",null,1);
            context.Freeze(1.1f);context.Observe("right","other",null,1.2f);context.Observe("right","other",null,1.6f);
            var snapshot=context.Read(1.7f);CollectionAssert.AreEqual(new[]{"api","lambda"},snapshot.gestureNodeIds);
            Assert.AreEqual("lambda",snapshot.pointers[0].nodeId);
            Assert.IsEmpty(context.Read(28).pointers);Assert.IsEmpty(context.Read(28).gestureNodeIds);
        }
        [Test] public void JitterAndConflictingHandsNeverInventASingleTargetOrDestination()
        {
            var context=new SpatialVoiceContext();context.Observe("left","a",null,0);Assert.IsEmpty(context.Read(.1f).pointers);
            context.Observe("left","a",null,.4f);context.Observe("right","b",null,.1f);context.Observe("right","b",null,.4f);
            Assert.IsTrue(context.Read(.5f).ambiguous);
            context.Observe("left",null,new Vector3(0,1.5f,2),1);context.Observe("right",null,new Vector3(1,1.5f,2),1);
            context.Observe("left",null,new Vector3(0,1.5f,2),1.4f);context.Observe("right",null,new Vector3(1,1.5f,2),1.4f);
            Assert.IsFalse(context.Read(1.5f).hasLocation);context.Clear();Assert.IsEmpty(context.Read(2).pointers);
        }
        [Test] public void VisualScalePersistsButDoesNotChangeAwsDefinition()
        {
            var graph=Architecture.Preset(0);string definition=DesignSemantics.Definition(graph);
            graph.nodes[1].viewScale=.6f;Assert.AreEqual(.6f,graph.Copy().nodes[1].viewScale);
            Assert.AreEqual(definition,DesignSemantics.Definition(graph));Assert.IsTrue(DesignLibrary.Readable(graph));
            graph.nodes[1].viewScale=float.NaN;Assert.IsFalse(DesignLibrary.Readable(graph));Assert.IsNotEmpty(graph.Validate());
        }
        [Test] public void BothConversationModesKeepApplicationOwnedResponseAndInterruption()
        {
            string natural=RealtimeProtocol.TurnDetectionJson(true),fast=RealtimeProtocol.TurnDetectionJson(false);
            Assert.That(natural,Does.Contain("semantic_vad").And.Contain("\"eagerness\":\"low\""));
            Assert.That(fast,Does.Contain("server_vad").And.Contain("silence_duration_ms"));
            foreach(var json in new[]{natural,fast})Assert.That(json,Does.Contain("\"create_response\":false").And.Contain("\"interrupt_response\":false"));
        }
    }
}
