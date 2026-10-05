using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class AwsCloudApiTests
    {
        [Serializable] class Contract
        {
            public Architecture graph;
            public CloudReply session, catalog, validation, created, pending, ready, accepted, rollback;
        }
        sealed class FakeTransport : ICloudTransport
        {
            public readonly Queue<CloudReply> Replies = new Queue<CloudReply>();
            public readonly List<(string method, string url, string body)> Sent = new List<(string, string, string)>();
            public bool Aborted;
            public IEnumerator Send(string method, string url, string auth, string body, Action<CloudReply> done)
            {
                Assert.That(auth, Does.StartWith("Basic ")); Sent.Add((method, url, body));
                Assert.IsNotEmpty(Replies, "Unexpected request: " + method + " " + url);
                done(Replies.Dequeue()); yield break;
            }
            public void Abort() { Aborted = true; }
        }
        Contract fixture;
        FakeTransport transport;
        AwsCloudApi api;
        static CloudConnection Settings() => new CloudConnection { endpoint = "https://example.invalid/prod", username = "contract", password = "Test42", deploymentId = "1" };
        static void Run(IEnumerator routine)
        {
            while (routine.MoveNext()) if (routine.Current is IEnumerator nested) Run(nested);
        }
        [SetUp] public void Setup()
        {
            fixture = JsonUtility.FromJson<Contract>(File.ReadAllText(Path.Combine(Application.dataPath, "GuateGeeks/Tests/Editor/Fixtures/backend-contract.json")));
            transport = new FakeTransport(); api = new AwsCloudApi(Settings(), transport, _ => null);
        }
        void Connect()
        {
            transport.Replies.Enqueue(fixture.session); transport.Replies.Enqueue(fixture.catalog);
            Run(api.Connect((ok, message) => Assert.IsTrue(ok, message)));
        }
        List<DeploymentEvent> Deploy(params CloudReply[] replies)
        {
            transport.Replies.Enqueue(fixture.validation);
            foreach (var reply in replies) transport.Replies.Enqueue(reply);
            var events = new List<DeploymentEvent>(); Run(api.Deploy(fixture.graph, false, events.Add)); return events;
        }
        [Test] public void CodeWritesNeverRetryAfterAmbiguousResponse()
        {
            Connect();transport.Replies.Enqueue(new CloudReply{Code=0});
            Run(api.CodeOperation("publish",new AwsCloudApi.CodeRequest{confirmed=true,revisionId="r1",stackId="stack",resourceId="fn",source=LambdaCodeDraft.Starter},(r,e)=>{Assert.IsNull(r);Assert.That(e,Does.Contain("AWS puede seguir trabajando"));}));
            Assert.AreEqual(1,transport.Sent.Count(r=>r.method=="POST"));
            Assert.That(transport.Sent.Last().body,Does.Contain("\"revisionId\":\"r1\""));
        }
        [Test] public void RealLambdaContractCreatesPollsAndInvokesWithoutFakeMetrics()
        {
            Connect(); var before = JsonUtility.ToJson(fixture.graph);
            var events = Deploy(fixture.created, fixture.pending, fixture.ready);
            Assert.IsTrue(events.Last().Success);
            Assert.IsFalse(events.First(e => e.Message.StartsWith("CREATE_IN_PROGRESS")).Finished);
            Assert.That(events.Count(e => e.State == ResourceState.Provisioning && e.ResourceId != null), Is.GreaterThanOrEqualTo(6));
            Assert.AreEqual(before, JsonUtility.ToJson(fixture.graph));
            api.EventResourceId = "node1";
            transport.Replies.Enqueue(fixture.ready); transport.Replies.Enqueue(fixture.accepted);
            Run(api.Invoke((ok, message) => { Assert.IsTrue(ok); Assert.That(message, Does.Contain("aún no confirmado")); Assert.That(message, Does.Not.Contain("142")); }));
            Assert.That(transport.Sent.Last().body, Does.Contain("node1"));
            Assert.That(transport.Sent.Single(r => r.url.EndsWith("/deployments")).body, Does.Contain("\"deploymentId\":\"1\""));
        }
        [Test] public void AmbiguousCreationReadsSameSlotWithoutRepeatingPost()
        {
            Connect(); var events = Deploy(new CloudReply { Code = 0 }, fixture.pending, fixture.ready);
            Assert.IsTrue(events.Last().Success);
            Assert.AreEqual(1, transport.Sent.Count(r => r.method == "POST" && r.url.EndsWith("/deployments")));
            Assert.That(transport.Sent.Last().url, Does.EndWith("/deployments/1"));
        }
        [Test] public void CustomEventUsesDefaultEntrypointAndPreservesPayloadAndStackGuard()
        {
            Connect();Deploy(fixture.created,fixture.ready);
            api.EventResourceId="node2"; // A previously selected target must not affect this request.
            transport.Replies.Enqueue(fixture.ready);transport.Replies.Enqueue(fixture.accepted);
            string payload="{\"message\":\"pedido nuevo\",\"amount\":42}";
            Run(api.InvokeEvent("",payload,(ok,message)=>Assert.IsTrue(ok,message)));
            var sent=transport.Sent.Last();
            Assert.That(sent.body,Does.Contain("\"resourceId\":\""+AwsCloudApi.DefaultEventSource(fixture.graph).id+"\""));
            Assert.That(sent.body,Does.Contain("\"stackId\":\""+api.StackId+"\""));
            Assert.That(sent.body,Does.Contain("pedido nuevo"));
        }
        [Test] public void CancelledVoiceEventDoesNotSendAfterPreflight()
        {
            Connect();Deploy(fixture.created,fixture.ready);
            transport.Replies.Enqueue(fixture.ready);
            Run(api.InvokeEvent("","{}",(ok,message)=>{Assert.IsFalse(ok);Assert.That(message,Does.Contain("cancelado"));},()=>false));
            Assert.IsFalse(transport.Sent.Any(r=>r.method=="POST" && r.url.EndsWith("/events")));
        }
        [Test] public void AnotherDesignInSharedSlotIsNeverMarkedReady()
        {
            Connect(); var ready = new CloudReply { Code = 200, Json = fixture.ready.Json.Replace(JsonUtility.FromJson<AwsCloudApi.DeploymentStatus>(fixture.ready.Json).graphHash, "different-design") };
            Assert.IsFalse(Deploy(new CloudReply { Code = 0 }, ready).Last().Success);
        }
        [Test] public void RollbackAndConflictAreFailures()
        {
            Connect(); Assert.IsFalse(Deploy(fixture.created, fixture.rollback).Last().Success);
            Assert.IsFalse(Deploy(new CloudReply { Code = 409, Json = "{\"message\":\"slot occupied\"}" }).Last().Success);
        }
        [Test] public void UnauthorizedSessionStopsAndThrottlingRetriesOnlyReads()
        {
            transport.Replies.Enqueue(new CloudReply { Code = 429 });
            transport.Replies.Enqueue(new CloudReply { Code = 401 });
            Run(api.Connect((ok, message) => { Assert.IsFalse(ok); Assert.That(message, Does.Contain("401")); }));
            Assert.IsFalse(api.Connected); Assert.AreEqual(2, transport.Sent.Count);
            Assert.IsTrue(transport.Sent.All(r => r.method == "GET"));
        }
        [Test] public void UnsupportedGraphNeverRequestsCreation()
        {
            Connect(); fixture.graph.nodes[0].setting = 1;
            var events = new List<DeploymentEvent>(); Run(api.Deploy(fixture.graph, false, events.Add));
            Assert.IsFalse(events.Last().Success); Assert.AreEqual(2, transport.Sent.Count);
            fixture.graph.nodes[0].setting = 0;
            Run(api.Deploy(fixture.graph, true, events.Add)); Assert.AreEqual(2, transport.Sent.Count);
        }
        [Test] public void BootstrapIsConsumedEvenWhenExpiredAndRejectsCleartextEndpoint()
        {
            string path = Path.GetTempFileName();
            try
            {
                var settings = Settings(); settings.expiresAt = 1;
                File.WriteAllText(path, JsonUtility.ToJson(settings));
                Assert.Throws<ArgumentException>(() => CloudConnection.Consume(path)); Assert.IsFalse(File.Exists(path));
                settings.endpoint = "http://example.invalid";
                Assert.Throws<ArgumentException>(() => new AwsCloudApi(settings, transport));
                settings = Settings(); settings.expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;
                File.WriteAllText(path, JsonUtility.ToJson(settings));
                Assert.AreEqual("1", CloudConnection.Consume(path).deploymentId); Assert.IsFalse(File.Exists(path));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        [Test] public void StoppingObservationOnlyAbortsLocalTransport()
        {
            Connect(); api.StopWatching(); Assert.IsTrue(transport.Aborted); Assert.IsTrue(api.Connected);
            Assert.IsFalse(transport.Sent.Any(r => r.method == "DELETE"));
            api.Disconnect(); Assert.IsFalse(api.Connected);
        }
        [TestCase("CREATE_COMPLETE", ResourceState.Ready)]
        [TestCase("ROLLBACK_COMPLETE", ResourceState.Failed)]
        [TestCase("CREATE_FAILED", ResourceState.Failed)]
        [TestCase("DELETE_IN_PROGRESS", ResourceState.Failed)]
        [TestCase(null, ResourceState.Provisioning)]
        public void MapsActualResourceStates(string state, ResourceState expected) => Assert.AreEqual(expected, AwsCloudApi.MapState(state));

        [Test] public void CleanupBindsConfirmedIdentityAndDoesNotRepeatAmbiguousDelete()
        {
            Connect();
            var state = JsonUtility.FromJson<AwsCloudApi.DeploymentStatus>(fixture.ready.Json);
            transport.Replies.Enqueue(fixture.ready);
            transport.Replies.Enqueue(new CloudReply { Code = 0 });
            transport.Replies.Enqueue(fixture.ready);
            Run(api.CleanSlot("1", state.stackId, _ => {}, (ok, message) => Assert.IsFalse(ok)));
            Assert.AreEqual(1, transport.Sent.Count(r => r.method == "DELETE"));
            Assert.That(transport.Sent.Single(r => r.method == "DELETE").url, Does.Contain("stackId=" + Uri.EscapeDataString(state.stackId)));
        }
        [Test] public void CleanupNeverDeletesReplacementAndClearsCompletedDeploymentIdentity()
        {
            Connect(); Assert.IsTrue(Deploy(fixture.ready).Last().Success);
            string id = api.StackId;
            transport.Replies.Enqueue(fixture.ready);
            Run(api.CleanSlot("1", "older-stack", _ => {}, (ok, message) => Assert.IsFalse(ok)));
            Assert.IsFalse(transport.Sent.Any(r => r.method == "DELETE"));
            transport.Replies.Enqueue(fixture.ready);
            transport.Replies.Enqueue(new CloudReply { Code = 202, Json = "{\"status\":\"DELETE_IN_PROGRESS\"}" });
            transport.Replies.Enqueue(new CloudReply { Code = 404 });
            Run(api.CleanSlot("1", id, _ => {}, (ok, message) => Assert.IsTrue(ok)));
            Assert.IsNull(api.StackId);
        }
        [Test] public void InspectionChecksReturnedResourceIdentityAndEscapesQuery()
        {
            Connect();
            transport.Replies.Enqueue(new CloudReply { Code = 200, Json = "{\"stackId\":\"wrong\",\"resourceId\":\"node1\",\"entries\":[]}" });
            Run(api.Inspect("2", "arn:stack/2", "node1", "logs", "a+b=", (page, error) => Assert.IsNull(page)));
            Assert.That(transport.Sent.Last().url, Does.Contain("/2/logs?stackId=arn%3Astack%2F2&resourceId=node1&cursor=a%2Bb%3D"));
        }
        [Test] public void InspectionPagesKeepLongAndMultilineDataReadable()
        {
            var pages = ArchitectureLab.InspectionTextPages(new string('x', 1000));
            Assert.AreEqual(3, pages.Count);
            Assert.AreEqual(1000, string.Join("", pages).Replace("\n", "").Length);
            Assert.IsTrue(pages.All(p => p.Split('\n').Length <= 12));
            Assert.IsTrue(pages.All(p => p.Split('\n').All(l => l.Length <= 38)));
        }

        [Test] public void RefreshingEmptySlotAfterStoppedDeletionAllowsReuse()
        {
            Connect(); Assert.IsTrue(Deploy(fixture.ready).Last().Success); Assert.IsNotNull(api.StackId);
            transport.Replies.Enqueue(new CloudReply { Code = 404 });
            Run(api.ReadSlot("1", (state, error) => Assert.AreEqual("ABSENT", state.status)));
            Assert.IsNull(api.StackId);
            var replacement = new CloudReply { Code = 200, Json = fixture.ready.Json.Replace("contract-stack-1", "replacement-stack") };
            Assert.IsTrue(Deploy(replacement).Last().Success);
            Assert.AreEqual("replacement-stack", api.StackId);
        }
    }
}
