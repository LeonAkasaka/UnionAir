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

        [Test]
        public void Save_WritesSettingsEvenWhenIgnoreMaintenanceFails()
        {
            var projectRoot = Path.Combine(
                Path.GetTempPath(),
                "UnionAirProjectSettingsIgnoreTests-" + Guid.NewGuid().ToString("N"));
            var integrationDirectory = Path.Combine(projectRoot, ".unionair");
            var settingsPath = Path.Combine(integrationDirectory, "settings.json");
            Directory.CreateDirectory(Path.Combine(integrationDirectory, ".gitignore"));
            try
            {
                UnionAirProjectSettingsDocument document;
                string parseError;
                Assert.IsTrue(UnionAirProjectSettingsParser.TryParse(
                    Valid, KnownCustom, out document, out parseError), parseError);

                string persistenceError;
                string ignoreWarning;
                Assert.IsTrue(UnionAirProjectSettings.TryWriteDocument(
                    settingsPath,
                    projectRoot,
                    document,
                    out persistenceError,
                    out ignoreWarning));

                Assert.IsNull(persistenceError);
                StringAssert.Contains(".unionair/.gitignore", ignoreWarning);
                Assert.IsTrue(File.Exists(settingsPath));
                Assert.AreEqual(
                    UnionAirProjectSettingsParser.Serialize(document),
                    File.ReadAllText(settingsPath, new UTF8Encoding(false)));
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [TestCase(-1)]
        [TestCase(65536)]
        public void PublicPortSetter_RejectsInvalidConfiguredPorts(int port)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { UnionAirSettings.Port = port; });
        }

        [Test]
        public void SessionSnapshot_RoundTripsAValidDirtyWorkingDocument()
        {
            UnionAirProjectSettingsDocument original;
            string error;
            Assert.IsTrue(UnionAirProjectSettingsParser.TryParse(
                Valid, KnownCustom, out original, out error), error);

            var encoded = UnionAirProjectSettingsSnapshotCodec.Encode(
                UnionAirProjectSettingsState.Valid,
                original,
                null,
                true,
                "disk unavailable");

            UnionAirProjectSettingsState state;
            UnionAirProjectSettingsDocument restored;
            string loadError;
            bool savePending;
            string saveError;
            Assert.IsTrue(UnionAirProjectSettingsSnapshotCodec.TryDecode(
                encoded,
                KnownCustom,
                out state,
                out restored,
                out loadError,
                out savePending,
                out saveError));
            Assert.AreEqual(UnionAirProjectSettingsState.Valid, state);
            Assert.AreEqual(original.Port, restored.Port);
            Assert.AreEqual(original.AutoStart, restored.AutoStart);
            CollectionAssert.AreEquivalent(
                original.EnabledCategories,
                restored.EnabledCategories);
            Assert.IsTrue(savePending);
            Assert.AreEqual("disk unavailable", saveError);
            Assert.IsNull(loadError);
        }

        [TestCase(UnionAirProjectSettingsState.Missing, null)]
        [TestCase(UnionAirProjectSettingsState.Invalid, "invalid document")]
        public void SessionSnapshot_RoundTripsNonDocumentSourceState(
            UnionAirProjectSettingsState expectedState,
            string expectedError)
        {
            var encoded = UnionAirProjectSettingsSnapshotCodec.Encode(
                expectedState, null, expectedError, false, null);

            UnionAirProjectSettingsState state;
            UnionAirProjectSettingsDocument document;
            string loadError;
            bool savePending;
            string saveError;
            Assert.IsTrue(UnionAirProjectSettingsSnapshotCodec.TryDecode(
                encoded,
                KnownCustom,
                out state,
                out document,
                out loadError,
                out savePending,
                out saveError));
            Assert.AreEqual(expectedState, state);
            Assert.AreEqual(expectedError, loadError);
            Assert.IsNull(document);
            Assert.IsFalse(savePending);
            Assert.IsNull(saveError);
        }

        [Test]
        public void SessionSnapshot_RestoresMemoryInsteadOfAChangedDiskDocument()
        {
            UnionAirProjectSettingsDocument memoryDocument;
            string error;
            Assert.IsTrue(UnionAirProjectSettingsParser.TryParse(
                Valid, KnownCustom, out memoryDocument, out error), error);
            var encoded = UnionAirProjectSettingsSnapshotCodec.Encode(
                UnionAirProjectSettingsState.Valid,
                memoryDocument,
                null,
                false,
                null);
            var changedOnDisk = Valid.Replace("\"port\":0", "\"port\":43210");
            Assert.AreNotEqual(
                UnionAirProjectSettingsParser.Serialize(memoryDocument),
                changedOnDisk);

            UnionAirProjectSettingsState state;
            UnionAirProjectSettingsDocument restored;
            string loadError;
            bool savePending;
            string saveError;
            Assert.IsTrue(UnionAirProjectSettingsSnapshotCodec.TryDecode(
                encoded,
                KnownCustom,
                out state,
                out restored,
                out loadError,
                out savePending,
                out saveError));
            Assert.AreEqual(0, restored.Port);
        }

        [Test]
        public void SavePolicy_KeepsFailureAndUsesTheRequiredRetrySchedule()
        {
            var attempts = 0;
            string error;
            Assert.IsFalse(UnionAirProjectSettingsSavePolicy.TryWrite(delegate
            {
                attempts++;
                throw new IOException("locked");
            }, out error));
            Assert.AreEqual(1, attempts);
            Assert.AreEqual("locked", error);

            Assert.AreEqual(11d, UnionAirProjectSettingsSavePolicy.NextRetryTime(10d, 0));
            Assert.AreEqual(12d, UnionAirProjectSettingsSavePolicy.NextRetryTime(10d, 1));
            Assert.AreEqual(15d, UnionAirProjectSettingsSavePolicy.NextRetryTime(10d, 2));
            Assert.AreEqual(20d, UnionAirProjectSettingsSavePolicy.NextRetryTime(10d, 3));
            Assert.AreEqual(40d, UnionAirProjectSettingsSavePolicy.NextRetryTime(10d, 4));
            Assert.AreEqual(40d, UnionAirProjectSettingsSavePolicy.NextRetryTime(10d, 20));

            Assert.IsTrue(UnionAirProjectSettingsSavePolicy.TryWrite(
                delegate { attempts++; },
                out error));
            Assert.AreEqual(2, attempts);
            Assert.IsNull(error);
        }

        [Test]
        public void WorkingDocument_FirstMissingChangeMigratesEveryLegacyValue()
        {
            var legacy = new UnionAirProjectSettingsDocument
            {
                Port = 8765,
                AutoStart = true,
                CustomHandlers = true,
                AllowSceneChanges = true
            };
            legacy.EnabledCategories.Add("assetWrite");
            var captures = 0;

            var document = UnionAirProjectSettingsDocumentModel.BeginChange(
                UnionAirProjectSettingsState.Missing,
                null,
                delegate
                {
                    captures++;
                    return legacy;
                });

            Assert.AreSame(legacy, document);
            Assert.AreEqual(1, captures);
            Assert.AreEqual(8765, document.Port);
            Assert.IsTrue(document.AutoStart);
            Assert.IsTrue(document.CustomHandlers);
            Assert.IsTrue(document.AllowSceneChanges);
            CollectionAssert.AreEquivalent(
                new[] { "assetWrite" },
                document.EnabledCategories);
        }

        [Test]
        public void WorkingDocument_FirstInvalidChangeStartsFromFailClosedValues()
        {
            var document = UnionAirProjectSettingsDocumentModel.BeginChange(
                UnionAirProjectSettingsState.Invalid,
                null,
                delegate
                {
                    Assert.Fail("Invalid settings must not capture legacy values.");
                    return null;
                });

            Assert.AreEqual(0, document.Port);
            Assert.IsFalse(document.AutoStart);
            Assert.IsFalse(document.CustomHandlers);
            Assert.IsFalse(document.AllowSceneChanges);
            CollectionAssert.IsEmpty(document.EnabledCategories);
        }

        [Test]
        public void WorkingDocument_ValidSourceKeepsTheCurrentDocument()
        {
            var original = new UnionAirProjectSettingsDocument
            {
                Port = 8765,
                AutoStart = true
            };

            var document = UnionAirProjectSettingsDocumentModel.BeginChange(
                UnionAirProjectSettingsState.Valid,
                original,
                delegate
                {
                    Assert.Fail("A valid working document must not recapture legacy values.");
                    return null;
                });

            Assert.AreSame(original, document);
        }

        [Test]
        public void WorkingDocument_SettersProduceACompleteSerializableDocument()
        {
            var document = UnionAirProjectSettingsDocumentModel.BeginChange(
                UnionAirProjectSettingsState.Invalid,
                null,
                delegate { return null; });

            UnionAirProjectSettingsDocumentModel.SetPort(document, 43123);
            UnionAirProjectSettingsDocumentModel.SetAutoStart(document, true);
            UnionAirProjectSettingsDocumentModel.SetCategoryEnabled(
                document, "assetWrite", true);
            UnionAirProjectSettingsDocumentModel.SetCustomHandlersEnabled(
                document, true);
            UnionAirProjectSettingsDocumentModel.SetCategoryEnabled(
                document, "custom:toolActions", true);
            UnionAirProjectSettingsDocumentModel.SetAllowSceneChanges(document, true);

            var serialized = UnionAirProjectSettingsParser.Serialize(document);
            UnionAirProjectSettingsDocument restored;
            string error;
            Assert.IsTrue(UnionAirProjectSettingsParser.TryParse(
                serialized, KnownCustom, out restored, out error), error);
            Assert.AreEqual(43123, restored.Port);
            Assert.IsTrue(restored.AutoStart);
            Assert.IsTrue(restored.CustomHandlers);
            Assert.IsTrue(restored.AllowSceneChanges);
            CollectionAssert.AreEquivalent(
                new[] { "assetWrite", "custom:toolActions" },
                restored.EnabledCategories);
        }

        [Test]
        public void WorkingDocument_CustomCategoryRequiresTheMasterToggle()
        {
            var document = new UnionAirProjectSettingsDocument
            {
                CustomHandlers = false
            };

            var error = Assert.Throws<InvalidOperationException>(delegate
            {
                UnionAirProjectSettingsDocumentModel.SetCategoryEnabled(
                    document, "custom:toolActions", true);
            });

            StringAssert.Contains("Enable Custom Handlers", error.Message);
            Assert.IsFalse(document.CustomHandlers);
            CollectionAssert.IsEmpty(document.EnabledCategories);
        }

        [Test]
        public void WorkingDocument_DisablingCustomHandlersRemovesCustomRequests()
        {
            var document = new UnionAirProjectSettingsDocument
            {
                CustomHandlers = true
            };
            document.EnabledCategories.Add("assetWrite");
            document.EnabledCategories.Add("custom:toolActions");

            UnionAirProjectSettingsDocumentModel.SetCustomHandlersEnabled(
                document, false);

            Assert.IsFalse(document.CustomHandlers);
            CollectionAssert.AreEquivalent(
                new[] { "assetWrite" },
                document.EnabledCategories);
        }

        [Test]
        public void WorkingDocument_DisableAllSensitiveApisPreservesServerSettings()
        {
            var document = new UnionAirProjectSettingsDocument
            {
                Port = 43123,
                AutoStart = true,
                CustomHandlers = true,
                AllowSceneChanges = true
            };
            document.EnabledCategories.Add("assetWrite");
            document.EnabledCategories.Add("custom:toolActions");

            UnionAirProjectSettingsDocumentModel.DisableAllSensitiveApis(document);

            Assert.AreEqual(43123, document.Port);
            Assert.IsTrue(document.AutoStart);
            Assert.IsFalse(document.CustomHandlers);
            Assert.IsFalse(document.AllowSceneChanges);
            CollectionAssert.IsEmpty(document.EnabledCategories);
        }
    }
}
