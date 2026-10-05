using System;
using System.Reflection;
using System.Collections.Concurrent;
using NUnit.Framework;
using UnityEngine;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class CollaborationTests
    {
        NetworkCollabSession session;
        RoomGrant grant;
        [SetUp] public void Setup() {
            grant=new RoomGrant{userId="me",roomId="ABCDEF12",station=0,websocketUrl="wss://test.example/rooms"};
            session=new NetworkCollabSession(grant);
            typeof(NetworkCollabSession).GetField("connected",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(session,true);
        }
        [TearDown] public void Cleanup()=>session.Dispose();
        void Deliver(RoomMessage value) {
            var queue=(ConcurrentQueue<string>)typeof(NetworkCollabSession).GetField("received",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);
            queue.Enqueue(JsonUtility.ToJson(value));
            session.Pump();
        }
        RoomMessage State(int revision=0,int version=1)=>new RoomMessage{type="snapshot",roomId=grant.roomId,revision=revision,roomVersion=version,hostId="me",graph=Architecture.Preset(0),
            members=new[]{new RoomMember{userId="me",name="ME",station=0,role="facilitator"},new RoomMember{userId="other",name="OTHER",station=1,role="editor"}},locks=Array.Empty<RoomLease>()};
        [Test] public void ClaimWaitsForServerAndOnlyAcceptsLiveOwnLease() {
            var state=State();Deliver(state);
            Assert.IsFalse(session.Claim("node1"),"Sending a request is not a granted lease");
            state=State(0,2);state.locks=new[]{new RoomLease{objectId="node1",owner="me",token="grant",expiresAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+20}};Deliver(state);
            Assert.IsTrue(session.Claim("node1"));Assert.AreEqual("node1",session.LocalHold);
            Assert.AreEqual("grant",session.OwnLeases()[0].token);
        }
        [Test] public void ForeignLeaseAndOutOfOrderSnapshotsCannotGrantOwnership() {
            var state=State(2,4);state.locks=new[]{new RoomLease{objectId="node1",owner="other",token="foreign",expiresAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+20}};Deliver(state);
            Assert.AreEqual("other",session.LockOwner("node1").Id);Assert.IsFalse(session.Claim("node1"));
            Deliver(State(2,3));Assert.AreEqual("other",session.LockOwner("node1").Id);
            Deliver(State(1,5));Assert.AreEqual(2,session.Revision);
        }
        [Test] public void PresenceDoesNotChangeTheGraphOrPersonalConversation() {
            Deliver(State());var graph=JsonUtility.ToJson(session.Graph);
            Deliver(new RoomMessage{type="presence",userId="other",pointing=true,focusId="node1",pose=new RoomPose{head=new Vector3(1,1.6f,2),hand=new Vector3(1,1,2),rotation=Quaternion.identity,pointAt=Vector3.one}});
            session.Tick(1,null);Assert.IsTrue(session.Peers[0].Pointing);Assert.AreEqual(graph,JsonUtility.ToJson(session.Graph));
        }
        [Test] public void DisconnectCannotFallBackToWritableLocalSession() {
            Deliver(State());Assert.IsTrue(session.CanWrite);
            session.Dispose();Assert.IsFalse(session.CanWrite);Assert.IsFalse(session.Claim("node1"));Assert.AreEqual("DESCONECTADA",session.Mode);
        }
        [Test] public void MicrophoneGateClearsQueuedFramesAndDefaultsToNormalSoloCapture() {
            var owner=new GameObject("Gate test",typeof(AudioListener));
            try {
                var input=owner.AddComponent<RealtimeAudioInput>();
                var type=typeof(RealtimeAudioInput);
                Assert.IsTrue((bool)type.GetField("transmit",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(input));
                type.GetField("count",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(input,3);
                input.SetTransmissionEnabled(false);Assert.IsTrue(input.Drained);
                Assert.IsFalse((bool)type.GetField("transmit",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(input));
            } finally {UnityEngine.Object.DestroyImmediate(owner);}
        }
        [Test] public void DelayedOperationReceiptUsesNewestStateWithoutLosingAcknowledgment() {
            Deliver(State(2,4)); RoomMessage receipt=null; session.Snapshot += value=>receipt=value;
            var older=State(1,3);older.requestId="my-operation";older.accepted=true;Deliver(older);
            Assert.IsNotNull(receipt);Assert.AreEqual("my-operation",receipt.requestId);Assert.IsTrue(receipt.accepted);
            Assert.AreEqual(2,receipt.revision);Assert.AreEqual(2,session.Revision);Assert.AreEqual(4,session.LastSnapshot.roomVersion);
        }
        [Test] public void MutedMicrophoneQueuesSilenceAndOpeningGateRestoresCapture() {
            var owner=new GameObject("Capture gate",typeof(AudioListener));
            var clip=AudioClip.Create("Synthetic capture",1920,1,RealtimeAudioInput.SampleRate,false);
            try {
                var samples=new float[1920];for(int i=0;i<samples.Length;i++)samples[i]=.25f;clip.SetData(samples,0);
                var input=owner.AddComponent<RealtimeAudioInput>();
                typeof(RealtimeAudioInput).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(input,null);
                input.Begin(clip);input.SetTransmissionEnabled(false);input.ReadAvailable(480);
                var frames=(float[][])typeof(RealtimeAudioInput).GetField("frames",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(input);
                Assert.That(frames[0],Is.All.EqualTo(0f),"Muted speech must never enter the WebRTC queue");
                input.SetTransmissionEnabled(true);input.ReadAvailable(960);
                Assert.That(frames[1],Is.All.EqualTo(.25f),"Push to talk must preserve speech while held");
            } finally {UnityEngine.Object.DestroyImmediate(owner);UnityEngine.Object.DestroyImmediate(clip);}
        }
    }
}
