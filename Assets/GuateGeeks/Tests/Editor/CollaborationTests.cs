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
        [Test] public void UnrelatedSnapshotDoesNotResendPendingClaim() {
            Deliver(State()); Assert.IsFalse(session.Claim("node1"));
            Deliver(State(0,2)); Assert.IsFalse(session.Claim("node1"));
            var queue=(ConcurrentQueue<string>)typeof(NetworkCollabSession).GetField("outgoing",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);
            Assert.AreEqual(1,queue.Count,"A heartbeat must not create a duplicate ownership request");
        }
        [Test] public void MissingClaimResponseRetriesSameRequestAfterBoundedWait() {
            Deliver(State());session.Claim("node1");
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var sent=(System.Collections.Generic.Dictionary<string,float>)typeof(NetworkCollabSession).GetField("claimSentAt",flags).GetValue(session);
            sent["node1"]=Time.unscaledTime-2;session.Claim("node1");
            var queue=(ConcurrentQueue<string>)typeof(NetworkCollabSession).GetField("outgoing",flags).GetValue(session);
            Assert.IsTrue(queue.TryDequeue(out var first));Assert.IsTrue(queue.TryDequeue(out var retry));
            Assert.AreEqual(JsonUtility.FromJson<RoomCommand>(first).requestId,JsonUtility.FromJson<RoomCommand>(retry).requestId);
        }
        [Test] public void ReleaseCannotReuseLeaseBeforeServerConfirmsRelease() {
            var state=State(); state.locks=new[]{new RoomLease{objectId="node1",owner="me",token="grant",expiresAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+20}};
            Deliver(state);session.Release("node1");Assert.IsFalse(session.Claim("node1"));
            state.roomVersion++;Deliver(state);Assert.IsFalse(session.Claim("node1"),"An unrelated snapshot still contains the lease being released");
            Deliver(State(0,3));Assert.IsFalse(session.Claim("node1"),"A new grab requests a fresh lease");
        }
        [Test] public void MovementPreviewRequiresOwnerAndIncreasingSequenceAndCannotRevertCommit() {
            var state=State();state.locks=new[]{new RoomLease{objectId="node1",owner="other",token="grant",expiresAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+20}};Deliver(state);
            RoomMessage Preview(int sequence,string token="grant")=>new RoomMessage{type="presence",userId="other",pose=new RoomPose{objectId="node1",leaseToken=token,revision=0,moveSequence=sequence,objectPosition=new Vector3(sequence*.1f,1.3f,2.4f)}};
            Deliver(Preview(2));Assert.IsTrue(session.TryGetObjectPosition("node1",out var position));Assert.AreEqual(.2f,position.x);
            Deliver(Preview(1));Deliver(Preview(3,"wrong"));session.TryGetObjectPosition("node1",out position);Assert.AreEqual(.2f,position.x);
            state.revision=1;state.roomVersion=2;Deliver(state);Deliver(Preview(4));Assert.IsFalse(session.TryGetObjectPosition("node1",out position));
        }
        [Test] public void UnrelatedCommitKeepsRemoteDragPreviewUntilItsOwnDrop() {
            var state=State();var id=state.graph.nodes[0].id;
            state.locks=new[]{new RoomLease{objectId=id,owner="other",token="grant",expiresAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+20}};Deliver(state);
            var position=state.graph.nodes[0].position+new Vector3(.2f,0,0);
            Deliver(new RoomMessage{type="presence",userId="other",pose=new RoomPose{objectId=id,leaseToken="grant",revision=0,moveSequence=1,objectPosition=position}});
            state.revision=1;state.roomVersion=2;state.graph.nodes[1].name="Unrelated edit";Deliver(state);
            Assert.IsTrue(session.TryGetObjectPosition(id,out var preview));Assert.AreEqual(position,preview);
            state.revision=2;state.roomVersion=3;state.graph.nodes[0].position=position;Deliver(state);
            Assert.IsFalse(session.TryGetObjectPosition(id,out preview),"The committed drop replaces its preview");
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
        [Test] public void TeammateAndroidArmKeepsBoneLengthsAndReachesTheHand() {
            const float upper=TeammateAndroid.UpperArm,lower=TeammateAndroid.Forearm;
            var shoulder=new Vector3(.185f,1.45f,0);var pole=new Vector3(.55f,-1,-.45f);
            var hand=shoulder+new Vector3(.02f,-.13f,.37f);
            var elbow=TeammateAndroid.SolveElbow(shoulder,hand,pole,upper,lower,out var wrist);
            Assert.Less(Vector3.Distance(wrist,hand),1e-4f,"A hand within reach is reached exactly");
            Assert.AreEqual(upper,Vector3.Distance(shoulder,elbow),1e-4f);Assert.AreEqual(lower,Vector3.Distance(elbow,wrist),1e-4f);
            Assert.Greater(Vector3.Dot(elbow-(shoulder+wrist)*.5f,pole),0,"The elbow bends toward the pole (down and out)");
            var far=shoulder+new Vector3(0,0,2);
            elbow=TeammateAndroid.SolveElbow(shoulder,far,pole,upper,lower,out wrist);
            Assert.AreEqual(upper,Vector3.Distance(shoulder,elbow),1e-3f);Assert.AreEqual(lower,Vector3.Distance(elbow,wrist),1e-3f);
            Assert.Greater(Vector3.Dot((wrist-shoulder).normalized,Vector3.forward),.999f,"Out of reach, the arm stretches toward the hand");
            elbow=TeammateAndroid.SolveElbow(shoulder,shoulder,pole,upper,lower,out wrist);
            Assert.IsFalse(float.IsNaN(elbow.x)||float.IsNaN(wrist.x),"A hand at the shoulder never produces NaN");
        }
    }
}
