using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class ArchitectureTests
    {
        [TestCase(0), TestCase(1), TestCase(2)]
        public void PresetsAreDeployable(int index) => Assert.IsEmpty(Architecture.Preset(index).Validate());
        [Test]
        public void PreviewChecksDoNotMutateTheGraph()
        {
            var g = Architecture.Preset(0);
            var monitor = g.Add(ServiceKind.CloudWatch, Vector3.one);
            var before = JsonUtility.ToJson(g);
            Assert.IsTrue(g.CanConnect(g.nodes[1].id, monitor.id, out _));
            Assert.IsFalse(g.CanConnect(g.nodes[0].id, g.nodes[1].id, out _));
            Assert.IsFalse(g.CanConnect(monitor.id, monitor.id, out _));
            Assert.AreEqual(before, JsonUtility.ToJson(g));
        }
        [Test]
        public void RejectsSelfDuplicateAndUnsupportedLinks()
        {
            var g = Architecture.Preset(0);
            Assert.IsFalse(g.Connect(g.nodes[0].id, g.nodes[0].id, out _));
            Assert.IsFalse(g.Connect(g.nodes[0].id, g.nodes[1].id, out _));
            Assert.IsFalse(g.Connect(g.nodes[0].id, g.nodes[2].id, out _));
            Assert.AreEqual(2, g.links.Count);
        }
        [Test]
        public void PreventsValidServicePairsFromMakingCycles()
        {
            var g = new Architecture(); var fn = g.Add(ServiceKind.Lambda, Vector3.zero); var queue = g.Add(ServiceKind.SQS, Vector3.one);
            Assert.IsTrue(g.Connect(fn.id, queue.id, out _)); Assert.IsFalse(g.Connect(queue.id, fn.id, out _));
        }
        [Test]
        public void DeleteRemovesIncidentEdgesAndFindsDisconnectedResources()
        {
            var g = Architecture.Preset(0); g.Remove(g.nodes[1].id);
            Assert.AreEqual(0, g.links.Count); Assert.AreEqual(2, g.Validate().Count(s => s.Contains("sin conectar")));
        }
        [Test]
        public void JsonRoundTripAndSnapshotsPreserveConfigurationIndependently()
        {
            var g = Architecture.Preset(0); g.nodes[1].setting = 2; g.region = "us-west-2";
            var copy = g.Copy(); g.nodes[1].position = Vector3.zero;
            Assert.AreEqual(2, copy.nodes[1].setting); Assert.AreEqual("us-west-2", copy.region);
            Assert.AreNotEqual(g.nodes[1].position, copy.nodes[1].position); Assert.IsEmpty(copy.Validate());
        }
        [Test]
        public void CorruptSavedGraphIsRejected()
        {
            var g = Architecture.Preset(0); g.nodes[1].setting = 99; Assert.IsNotEmpty(g.Validate());
            g.nodes[1].setting = 0; g.links[0].to = "missing"; Assert.IsNotEmpty(g.Validate());
            g.links.Clear(); g.nodes[0] = null; Assert.IsNotEmpty(g.Validate());
        }
        [Test]
        public void ReaddingDeletedServiceKeepsDisplayNamesUnique()
        {
            var g = new Architecture(); var first = g.Add(ServiceKind.Lambda, Vector3.zero);
            g.Add(ServiceKind.Lambda, Vector3.one); g.Remove(first.id); g.Add(ServiceKind.Lambda, Vector3.zero);
            Assert.AreEqual(2, g.nodes.Select(n => n.name).Distinct().Count());
        }
        [Test]
        public void ResourceLimitIsEnforced()
        {
            var g = new Architecture(); for (int i = 0; i < Architecture.MaxNodes; i++) Assert.IsNotNull(g.Add(ServiceKind.Lambda, Vector3.zero));
            Assert.IsNull(g.Add(ServiceKind.S3, Vector3.zero)); Assert.AreEqual(Architecture.MaxNodes, g.nodes.Count);
        }
        [Test]
        public void MockDeploymentRequiresSessionAndDoesNotMutateGraph()
        {
            var api = new MockCloudApi(_ => null); var graph = Architecture.Preset(0); var events = new List<DeploymentEvent>();
            var deploy = api.Deploy(graph, false, events.Add); while (deploy.MoveNext()) { }
            Assert.IsFalse(events.Last().Success);
            var connect = api.Connect((ok, _) => Assert.IsTrue(ok)); while (connect.MoveNext()) { }
            events.Clear(); deploy = api.Deploy(graph, false, events.Add); while (deploy.MoveNext()) { }
            Assert.IsTrue(events.Last().Success); Assert.AreEqual(1, events.Last().Progress);
            Assert.IsTrue(graph.nodes.All(n => n.state == ResourceState.Draft));
        }
        [Test]
        public void MockFailureCanBeRetriedSuccessfully()
        {
            var api = new MockCloudApi(_ => null); var connect = api.Connect((_, __) => { }); while (connect.MoveNext()) { }
            var events = new List<DeploymentEvent>(); var deploy = api.Deploy(Architecture.Preset(1), true, events.Add); while (deploy.MoveNext()) { }
            Assert.IsTrue(events.Last().Finished); Assert.IsFalse(events.Last().Success); Assert.AreEqual(ResourceState.Failed, events.Last().State);
            events.Clear(); deploy = api.Deploy(Architecture.Preset(1), false, events.Add); while (deploy.MoveNext()) { }
            Assert.IsTrue(events.Last().Success);
        }
        [Test]
        public void ProvisioningFollowsEdgesEvenWithReversedCreationOrder()
        {
            var g = Architecture.Preset(0); string first = g.nodes[0].id; g.nodes.Reverse();
            var api = new MockCloudApi(_ => null); var connect = api.Connect((_, __) => { }); while (connect.MoveNext()) { }
            var events = new List<DeploymentEvent>(); var deploy = api.Deploy(g, false, events.Add); while (deploy.MoveNext()) { }
            Assert.AreEqual(first, events.First().ResourceId);
        }
    }
}
