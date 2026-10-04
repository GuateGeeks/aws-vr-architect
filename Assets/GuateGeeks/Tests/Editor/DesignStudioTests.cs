using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class DesignStudioTests
    {
        [Test] public void ConnectionsRespectConfigurationAndPlatformFanout()
        {
            var g = new Architecture(); var bucket = g.Add(ServiceKind.S3, Vector3.zero);
            var queue = g.Add(ServiceKind.SQS, Vector3.right); queue.setting = 1;
            Assert.IsFalse(g.Connect(bucket.id, queue.id, out var reason)); Assert.That(reason, Does.Contain("FIFO"));
            queue.setting = 0; Assert.IsTrue(g.Connect(bucket.id, queue.id, out _));
            var fn = g.Add(ServiceKind.Lambda, Vector3.up);
            Assert.IsFalse(g.Connect(bucket.id, fn.id, out reason)); Assert.That(reason, Does.Contain("plataforma"));
            Assert.IsTrue(g.Connect(queue.id, fn.id, out _));
            var fn2 = g.Add(ServiceKind.Lambda, Vector3.one);
            Assert.IsFalse(g.Connect(queue.id, fn2.id, out _));
            var monitor = g.Add(ServiceKind.CloudWatch, Vector3.one);
            Assert.IsTrue(g.Connect(bucket.id, monitor.id, out _), "An observation is not a second notification");
            queue.setting = 1; Assert.IsTrue(g.Validate().Any(s => s.Contains("FIFO")), "Changing an existing node must also be validated");
        }
        [Test] public void DefinitionIdentityIgnoresLayoutOrderAndRuntimeStateOnly()
        {
            var g = Architecture.Preset(0); var copy = g.Copy();
            copy.nodes.Reverse(); copy.links.Reverse(); foreach (var n in copy.nodes) { n.position *= 2; n.state = ResourceState.Ready; }
            Assert.AreEqual(DesignSemantics.Definition(g), DesignSemantics.Definition(copy));
            copy.nodes[1].setting = 3; Assert.AreNotEqual(DesignSemantics.Definition(g), DesignSemantics.Definition(copy));
        }
        [Test] public void LibraryPreservesDraftsWithoutLeakingRuntimeStateAndSkipsCorruption()
        {
            string directory = Path.Combine(Path.GetTempPath(), "gg-studio-test-" + Guid.NewGuid().ToString("N"));
            try {
                var library = new DesignLibrary(directory); var graph = new Architecture(); var node = graph.Add(ServiceKind.ApiGateway, Vector3.one); node.state = ResourceState.Ready;
                var saved = library.Save("../Draft", graph);
                Assert.AreEqual(1, Directory.GetFiles(directory).Length, "Display names are never file paths");
                File.WriteAllText(Path.Combine(directory, "corrupt.json"), "{ invalid");
                var restored = library.Read().Single(); Assert.AreEqual(saved.id, restored.id); Assert.AreEqual("../Draft", restored.name);
                Assert.AreEqual(Vector3.one, restored.architecture.nodes[0].position); Assert.AreEqual(ResourceState.Draft, restored.architecture.nodes[0].state);
                Assert.IsNotEmpty(restored.architecture.Validate(), "An incomplete design remains editable");
                graph.nodes[0].position = Vector3.zero; Assert.AreEqual(Vector3.one, restored.architecture.nodes[0].position);
                Assert.Throws<ArgumentException>(() => library.Save(" ", graph));
            } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
