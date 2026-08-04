using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class EndpointDiscoveryTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "UnionAir-EndpointDiscoveryTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Test]
        public void Publish_WritesOneBomlessUtf8LineAndIgnoreRules()
        {
            string error;
            Assert.IsTrue(UnionAirEndpointDiscovery.TryPublish(_root, 49152, out error), error);

            var directory = Path.Combine(_root, ".unionair");
            var bytes = File.ReadAllBytes(Path.Combine(directory, "endpoint.txt"));
            CollectionAssert.AreEqual(
                new UTF8Encoding(false).GetBytes("http://localhost:49152/api/\n"),
                bytes);

            var ignore = File.ReadAllText(Path.Combine(directory, ".gitignore"));
            StringAssert.Contains(".gitignore", ignore);
            StringAssert.Contains("endpoint.txt", ignore);
            StringAssert.Contains("*.tmp", ignore);
            StringAssert.DoesNotContain("settings.json", ignore);
        }

        [Test]
        public void Publish_AtomicallyReplacesThePreviousEndpoint()
        {
            string error;
            Assert.IsTrue(UnionAirEndpointDiscovery.TryPublish(_root, 49152, out error), error);
            Assert.IsTrue(UnionAirEndpointDiscovery.TryPublish(_root, 49153, out error), error);

            Assert.AreEqual(
                "http://localhost:49153/api/\n",
                File.ReadAllText(Path.Combine(_root, ".unionair", "endpoint.txt")));
            Assert.IsEmpty(Directory.GetFiles(Path.Combine(_root, ".unionair"), "*.tmp"));
        }

        [Test]
        public void RemoveOwned_DoesNotDeleteAnotherInstancesEndpoint()
        {
            string error;
            bool removed;
            Assert.IsTrue(UnionAirEndpointDiscovery.TryPublish(_root, 49152, out error), error);

            Assert.IsTrue(UnionAirEndpointDiscovery.TryRemoveOwned(
                _root, "http://localhost:49153/api/", out removed, out error), error);
            Assert.IsFalse(removed);
            Assert.IsTrue(File.Exists(Path.Combine(_root, ".unionair", "endpoint.txt")));

            Assert.IsTrue(UnionAirEndpointDiscovery.TryRemoveOwned(
                _root, "http://localhost:49152/api/", out removed, out error), error);
            Assert.IsTrue(removed);
            Assert.IsFalse(File.Exists(Path.Combine(_root, ".unionair", "endpoint.txt")));
        }

        [Test]
        public void ClearStale_RemovesAnUnownedCrashRecord()
        {
            string error;
            Assert.IsTrue(UnionAirEndpointDiscovery.TryPublish(_root, 49152, out error), error);
            Assert.IsTrue(UnionAirEndpointDiscovery.TryClearStale(_root, out error), error);
            Assert.IsFalse(File.Exists(Path.Combine(_root, ".unionair", "endpoint.txt")));
        }

        [Test]
        public void Publish_RejectsANonConcretePort()
        {
            string error;
            Assert.IsFalse(UnionAirEndpointDiscovery.TryPublish(_root, 0, out error));
            StringAssert.Contains("invalid port", error);
        }

        [Test]
        public void Publish_WritesEndpointWhenIgnoreMaintenanceFails()
        {
            var directory = Path.Combine(_root, ".unionair");
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.Combine(directory, ".gitignore"));

            string error;
            string warning;
            Assert.IsTrue(
                UnionAirEndpointDiscovery.TryPublish(
                    _root, 49152, out error, out warning),
                error);

            Assert.IsNull(error);
            StringAssert.Contains(".unionair/.gitignore", warning);
            Assert.AreEqual(
                "http://localhost:49152/api/\n",
                File.ReadAllText(Path.Combine(directory, "endpoint.txt")));
        }

        [Test]
        public void IgnoreRules_PreserveUserContentAndExistingCrLf()
        {
            const string existing = "user-rule\r\nendpoint.txt\r\n";

            bool changed;
            var updated = UnionAirEndpointDiscovery.AddRequiredIgnoreRules(
                existing, out changed);

            Assert.IsTrue(changed);
            StringAssert.StartsWith(existing, updated);
            Assert.AreEqual(
                "user-rule\r\nendpoint.txt\r\n.gitignore\r\n*.tmp\r\n",
                updated);
        }

        [Test]
        public void IgnoreRules_DoNotRewriteACompleteFile()
        {
            const string existing = "user-rule\n.gitignore\nendpoint.txt\n*.tmp\n";

            bool changed;
            var updated = UnionAirEndpointDiscovery.AddRequiredIgnoreRules(
                existing, out changed);

            Assert.IsFalse(changed);
            Assert.AreEqual(existing, updated);
        }
    }
}
