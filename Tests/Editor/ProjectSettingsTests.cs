using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class ProjectSettingsTests
    {
        private static readonly HashSet<string> KnownCustom =
            new HashSet<string> { "toolActions" };

        private const string Valid =
            "{\"schemaVersion\":1,\"server\":{\"port\":0,\"autoStart\":true}," +
            "\"api\":{\"enabledCategories\":[\"assetWrite\"],\"customHandlers\":false}," +
            "\"playMode\":{\"allowSceneChanges\":false}}";

        [Test]
        public void Parse_ReadsTheCompleteV1Document()
        {
            UnionAirProjectSettingsDocument document;
            string error;
            Assert.IsTrue(UnionAirProjectSettingsParser.TryParse(
                Valid, KnownCustom, out document, out error), error);
            Assert.AreEqual(0, document.Port);
            Assert.IsTrue(document.AutoStart);
            CollectionAssert.AreEquivalent(new[] { "assetWrite" }, document.EnabledCategories);
            Assert.IsFalse(document.CustomHandlers);
            Assert.IsFalse(document.AllowSceneChanges);
        }

        [TestCase("{not-json", "Expected a JSON string")]
        [TestCase("{\"schemaVersion\":2,\"server\":{\"port\":0,\"autoStart\":true},\"api\":{\"enabledCategories\":[],\"customHandlers\":false},\"playMode\":{\"allowSceneChanges\":false}}", "Unsupported schemaVersion")]
        [TestCase("{\"schemaVersion\":1,\"extra\":true,\"server\":{\"port\":0,\"autoStart\":true},\"api\":{\"enabledCategories\":[],\"customHandlers\":false},\"playMode\":{\"allowSceneChanges\":false}}", "Unknown field")]
        [TestCase("{\"schemaVersion\":1,\"schemaVersion\":1,\"server\":{},\"api\":{},\"playMode\":{}}", "Duplicate JSON field")]
        [TestCase("{\"schemaVersion\":1,\"server\":{\"port\":0},\"api\":{\"enabledCategories\":[],\"customHandlers\":false},\"playMode\":{\"allowSceneChanges\":false}}", "server.autoStart")]
        public void Parse_RejectsMalformedUnsupportedUnknownAndDuplicateFields(string json, string expected)
        {
            UnionAirProjectSettingsDocument document;
            string error;
            Assert.IsFalse(UnionAirProjectSettingsParser.TryParse(
                json, KnownCustom, out document, out error));
            StringAssert.Contains(expected, error);
        }

        [TestCase(-1)]
        [TestCase(65536)]
        public void Parse_RejectsAnInvalidPort(int port)
        {
            var json = Valid.Replace("\"port\":0", "\"port\":" + port);
            UnionAirProjectSettingsDocument document;
            string error;
            Assert.IsFalse(UnionAirProjectSettingsParser.TryParse(
                json, KnownCustom, out document, out error));
            StringAssert.Contains("server.port", error);
        }

        [Test]
        public void Parse_RejectsWrongValueTypes()
        {
            var json = Valid.Replace("\"autoStart\":true", "\"autoStart\":\"true\"");
            UnionAirProjectSettingsDocument document;
            string error;
            Assert.IsFalse(UnionAirProjectSettingsParser.TryParse(
                json, KnownCustom, out document, out error));
            StringAssert.Contains("server.autoStart", error);
        }

        [Test]
        public void Parse_RejectsUtf8Bom()
        {
            UnionAirProjectSettingsDocument document;
            string error;
            Assert.IsFalse(UnionAirProjectSettingsParser.TryParse(
                "\uFEFF" + Valid, KnownCustom, out document, out error));
            StringAssert.Contains("without a byte-order mark", error);
        }

        [TestCase("read", "always-enabled")]
        [TestCase("misspelled", "Unknown category")]
        [TestCase("custom:missing", "Unknown category")]
        public void Parse_RejectsReservedAndUnknownCategories(string category, string expected)
        {
            var json = Valid.Replace("assetWrite", category);
            UnionAirProjectSettingsDocument document;
            string error;
            Assert.IsFalse(UnionAirProjectSettingsParser.TryParse(
                json, KnownCustom, out document, out error));
            StringAssert.Contains(expected, error);
        }

        [Test]
        public void Parse_RejectsDuplicateCategories()
        {
            var json = Valid.Replace("[\"assetWrite\"]", "[\"assetWrite\",\"assetWrite\"]");
            UnionAirProjectSettingsDocument document;
            string error;
            Assert.IsFalse(UnionAirProjectSettingsParser.TryParse(
                json, KnownCustom, out document, out error));
            StringAssert.Contains("Duplicate category", error);
        }

        [Test]
        public void Parse_RequiresCustomHandlersForCustomCategories()
        {
            var json = Valid.Replace("assetWrite", "custom:toolActions");
            UnionAirProjectSettingsDocument document;
            string error;
            Assert.IsFalse(UnionAirProjectSettingsParser.TryParse(
                json, KnownCustom, out document, out error));
            StringAssert.Contains("customHandlers", error);
        }

        [Test]
        public void Serialize_RoundTripsWithStableCategoryOrdering()
        {
            var original = Valid.Replace(
                "[\"assetWrite\"]",
                "[\"sceneWrite\",\"assetWrite\",\"custom:toolActions\"]")
                .Replace("\"customHandlers\":false", "\"customHandlers\":true");
            UnionAirProjectSettingsDocument first;
            string error;
            Assert.IsTrue(UnionAirProjectSettingsParser.TryParse(
                original, KnownCustom, out first, out error), error);

            var serialized = UnionAirProjectSettingsParser.Serialize(first);
            UnionAirProjectSettingsDocument second;
            Assert.IsTrue(UnionAirProjectSettingsParser.TryParse(
                serialized, KnownCustom, out second, out error), error);
            CollectionAssert.AreEquivalent(first.EnabledCategories, second.EnabledCategories);
            StringAssert.IsMatch(
                "assetWrite.*custom:toolActions.*sceneWrite",
                serialized.Replace("\n", ""));
        }

        [Test]
        public void CapabilityDecision_RequiresRequestAndApprovalAndHonorsLocalDenial()
        {
            var approved = new HashSet<string> { "category:assetWrite" };
            var denied = new HashSet<string>();
            Assert.IsTrue(UnionAirProjectSettingsDecision.IsEffective(
                true, approved, denied, "category:assetWrite"));
            Assert.IsFalse(UnionAirProjectSettingsDecision.IsEffective(
                false, approved, denied, "category:assetWrite"));

            denied.Add("category:assetWrite");
            Assert.IsFalse(UnionAirProjectSettingsDecision.IsEffective(
                true, approved, denied, "category:assetWrite"));
        }

        [Test]
        public void Pending_OnlyReturnsNewlyRequestedCapabilities()
        {
            var approved = new HashSet<string> { "category:assetWrite" };
            var pending = UnionAirProjectSettingsDecision.Pending(
                new[] { "category:assetWrite", "category:sceneWrite" },
                approved,
                new HashSet<string>());
            CollectionAssert.AreEqual(new[] { "category:sceneWrite" }, pending);
        }

        [Test]
        public void Pending_TreatsApprovalAndRefusalAsReviewedDecisions()
        {
            var requested = new[]
            {
                "category:assetWrite",
                "category:sceneWrite",
                "customHandlers"
            };
            var pending = UnionAirProjectSettingsDecision.Pending(
                requested,
                new HashSet<string> { "category:assetWrite" },
                new HashSet<string> { "category:sceneWrite" });
            CollectionAssert.AreEqual(new[] { "customHandlers" }, pending);
        }

        [Test]
        public void Pending_PromptsOnlyForCapabilitySetGrowth()
        {
            var approved = new HashSet<string>
            {
                "category:assetWrite",
                "category:sceneWrite"
            };
            var denied = new HashSet<string>();

            CollectionAssert.IsEmpty(UnionAirProjectSettingsDecision.Pending(
                new[] { "category:assetWrite" }, approved, denied));
            CollectionAssert.AreEqual(
                new[] { "category:build" },
                UnionAirProjectSettingsDecision.Pending(
                    new[] { "category:assetWrite", "category:build" },
                    approved,
                    denied));
        }

        [Test]
        public void ProjectScopeKey_NormalizesEquivalentProjectPaths()
        {
            var root = Path.Combine(Path.GetTempPath(), "UnionAirScope");
            Assert.AreEqual(
                UnionAirProjectSettings.ProjectScopeKey(root),
                UnionAirProjectSettings.ProjectScopeKey(root + Path.DirectorySeparatorChar));
            Assert.AreNotEqual(
                UnionAirProjectSettings.ProjectScopeKey(root),
                UnionAirProjectSettings.ProjectScopeKey(root + "-other"));
        }

        [Test]
        public void Loader_ReportsMissingValidAndInvalidWithoutPartialApplication()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "UnionAirProjectSettingsTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "settings.json");
            try
            {
                UnionAirProjectSettingsDocument document;
                string error;
                Assert.AreEqual(
                    UnionAirProjectSettingsState.Missing,
                    UnionAirProjectSettingsLoader.Load(
                        path, KnownCustom, out document, out error));
                Assert.IsNull(document);

                File.WriteAllText(path, Valid, new UTF8Encoding(false));
                Assert.AreEqual(
                    UnionAirProjectSettingsState.Valid,
                    UnionAirProjectSettingsLoader.Load(
                        path, KnownCustom, out document, out error),
                    error);
                Assert.AreEqual(0, document.Port);

                File.WriteAllText(
                    path,
                    Valid.Replace("\"port\":0", "\"port\":70000"),
                    new UTF8Encoding(false));
                Assert.AreEqual(
                    UnionAirProjectSettingsState.Invalid,
                    UnionAirProjectSettingsLoader.Load(
                        path, KnownCustom, out document, out error));
                Assert.IsNull(document);
                StringAssert.Contains("server.port", error);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Serialize_UsesUtf8WithoutBomWhenWrittenByAtomicHelper()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "UnionAirProjectSettingsBomTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "settings.json");
            try
            {
                UnionAirProjectSettingsDocument document;
                string error;
                Assert.IsTrue(UnionAirProjectSettingsParser.TryParse(
                    Valid, KnownCustom, out document, out error), error);
                UnionAirEndpointDiscovery.WriteAtomicText(
                    path, UnionAirProjectSettingsParser.Serialize(document));
                var bytes = File.ReadAllBytes(path);
                Assert.That(bytes.Length, Is.GreaterThan(3));
                Assert.IsFalse(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
