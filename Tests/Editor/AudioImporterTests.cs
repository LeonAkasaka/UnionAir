using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class AudioImporterTests
    {
        private const string TestDirectory = "Assets/UnionAirAudioImporterTests";
        private const string TestAssetPath = TestDirectory + "/tone.wav";

        [Test]
        public void Parser_ReadsGlobalDefaultAndPlatformSettings()
        {
            const string json =
                "{\"forceToMono\":true,\"normalize\":false,\"defaultSampleSettings\":{" +
                "\"loadType\":\"Streaming\",\"compressionFormat\":\"ADPCM\",\"quality\":0.25," +
                "\"preloadAudioData\":false,\"sampleRateSetting\":\"OverrideSampleRate\",\"sampleRateOverride\":22050," +
                "\"conversionMode\":0},\"platformOverrides\":[{\"platform\":\"Android\"," +
                "\"override\":true,\"sampleSettings\":{\"compressionFormat\":\"Vorbis\"}}]}";

            AudioImporterUpdateRequest request;
            string error;
            Assert.IsTrue(AudioImporterUpdateParser.TryParse(json, out request, out error), error);
            Assert.IsTrue(request.HasForceToMono);
            Assert.IsTrue(request.ForceToMono);
            Assert.IsTrue(request.HasNormalize);
            Assert.IsFalse(request.Normalize);
            Assert.AreEqual(AudioClipLoadType.Streaming, request.DefaultSampleSettings.LoadType);
            Assert.AreEqual(AudioCompressionFormat.ADPCM, request.DefaultSampleSettings.CompressionFormat);
            Assert.IsFalse(request.DefaultSampleSettings.PreloadAudioData);
            Assert.AreEqual(22050, request.DefaultSampleSettings.SampleRateOverride);
            Assert.AreEqual(1, request.PlatformOverrides.Count);
            Assert.AreEqual("Android", request.PlatformOverrides[0].Platform);
            Assert.IsTrue(request.PlatformOverrides[0].Override);
            Assert.AreEqual(
                AudioCompressionFormat.Vorbis,
                request.PlatformOverrides[0].SampleSettings.CompressionFormat);
        }

        [TestCase("{\"unknown\":true}", "Unknown field 'unknown'")]
        [TestCase("{\"forceToMono\":\"true\"}", "'forceToMono' must be a JSON boolean")]
        [TestCase("{\"defaultSampleSettings\":{\"quality\":1.1}}", "quality must be between 0 and 1")]
        [TestCase("{\"defaultSampleSettings\":{\"conversionMode\":1}}", "supports only 0")]
        [TestCase("{\"platformOverrides\":[{\"platform\":\"Android\",\"override\":false,\"sampleSettings\":{\"quality\":0.5}}]}", "not allowed when override is false")]
        public void Parser_RejectsInvalidRequests(string json, string expected)
        {
            AudioImporterUpdateRequest request;
            string error;
            Assert.IsFalse(AudioImporterUpdateParser.TryParse(json, out request, out error));
            StringAssert.Contains(expected, error);
        }

        [Test]
        public void Parser_RejectsDuplicatePlatformsCaseInsensitively()
        {
            const string json =
                "{\"platformOverrides\":[" +
                "{\"platform\":\"Android\",\"override\":false}," +
                "{\"platform\":\"android\",\"override\":false}]}";

            AudioImporterUpdateRequest request;
            string error;
            Assert.IsFalse(AudioImporterUpdateParser.TryParse(json, out request, out error));
            StringAssert.Contains("Duplicate platform override", error);
        }

        [Test]
        public void SamplePatch_PreservesFieldsThatWereNotSupplied()
        {
            AudioImporterUpdateRequest request;
            string error;
            Assert.IsTrue(
                AudioImporterUpdateParser.TryParse(
                    "{\"defaultSampleSettings\":{\"quality\":0.75}}", out request, out error),
                error);
            var original = new AudioImporterSampleSettings
            {
                loadType = AudioClipLoadType.CompressedInMemory,
                compressionFormat = AudioCompressionFormat.Vorbis,
                quality = 0.2f,
                preloadAudioData = true,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
                sampleRateOverride = 0,
                conversionMode = 0
            };

            var applied = request.DefaultSampleSettings.Apply(original);

            Assert.AreEqual(AudioClipLoadType.CompressedInMemory, applied.loadType);
            Assert.AreEqual(AudioCompressionFormat.Vorbis, applied.compressionFormat);
            Assert.AreEqual(0.75f, applied.quality);
            Assert.IsTrue(applied.preloadAudioData);
            Assert.AreEqual(AudioSampleRateSetting.PreserveSampleRate, applied.sampleRateSetting);
        }

        [Test]
        public void SamplePatch_ClearsOverrideWhenLeavingOverrideSampleRate()
        {
            AudioImporterUpdateRequest request;
            string error;
            Assert.IsTrue(
                AudioImporterUpdateParser.TryParse(
                    "{\"defaultSampleSettings\":{\"sampleRateSetting\":\"PreserveSampleRate\"}}",
                    out request,
                    out error),
                error);
            var original = new AudioImporterSampleSettings
            {
                sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate,
                sampleRateOverride = 44100
            };

            var applied = request.DefaultSampleSettings.Apply(original);

            Assert.AreEqual(AudioSampleRateSetting.PreserveSampleRate, applied.sampleRateSetting);
            Assert.AreEqual(0, applied.sampleRateOverride);
        }

        [Test]
        public void PlatformCatalog_UsesTheAudioImporterCodecCompatibilityModel()
        {
            AudioImporterPlatformCatalog.Entry standalone;
            Assert.IsTrue(AudioImporterPlatformCatalog.TryFind("Standalone", out standalone));
            CollectionAssert.AreEqual(
                new[] { AudioCompressionFormat.PCM, AudioCompressionFormat.Vorbis, AudioCompressionFormat.ADPCM },
                standalone.CompressionFormats);

            AudioImporterPlatformCatalog.Entry webGl;
            Assert.IsTrue(AudioImporterPlatformCatalog.TryFind("WebGL", out webGl));
            CollectionAssert.AreEqual(new[] { AudioCompressionFormat.AAC }, webGl.CompressionFormats);

            AudioImporterPlatformCatalog.Entry ios;
            Assert.IsTrue(AudioImporterPlatformCatalog.TryFind("iOS", out ios));
            Assert.IsFalse(AudioImporterPlatformCatalog.TryFind("iPhone", out ios));
        }

        [Test]
        public void Handler_ReadsUpdatesAndClearsARealAudioImporter()
        {
            CreateWaveAsset();
            try
            {
                var guid = AssetDatabase.AssetPathToGUID(TestAssetPath);
                Assert.IsNotEmpty(guid);
                var handler = new AudioImporterHandler();

                var read = new FakeResponse();
                handler.HandleGet(read, guid);
                Assert.AreEqual(200, read.StatusCode);
                StringAssert.Contains("\"defaultSampleSettings\"", read.Body);
                StringAssert.Contains("\"audioClip\":{", read.Body);

                var update = new FakeRequest("PATCH")
                    .WithJsonBody(
                        "{\"forceToMono\":true,\"normalize\":false," +
                        "\"defaultSampleSettings\":{\"compressionFormat\":\"ADPCM\"," +
                        "\"loadType\":\"CompressedInMemory\"}," +
                        "\"platformOverrides\":[{\"platform\":\"Android\",\"override\":true," +
                        "\"sampleSettings\":{\"compressionFormat\":\"Vorbis\",\"quality\":0.6}}]}");
                var updated = new FakeResponse();
                handler.HandleUpdate(update, updated, guid);

                Assert.AreEqual(200, updated.StatusCode, updated.Body);
                StringAssert.Contains("\"reimported\":true", updated.Body);
                StringAssert.Contains("\"diagnostics\":[", updated.Body);
                var importer = AssetImporter.GetAtPath(TestAssetPath) as AudioImporter;
                Assert.IsNotNull(importer);
                Assert.IsTrue(importer.forceToMono);
                Assert.AreEqual(AudioCompressionFormat.ADPCM, importer.defaultSampleSettings.compressionFormat);
                Assert.IsTrue(importer.ContainsSampleSettingsOverride(BuildTargetGroup.Android));
                Assert.AreEqual(
                    AudioCompressionFormat.Vorbis,
                    importer.GetOverrideSampleSettings(BuildTargetGroup.Android).compressionFormat);

                var clear = new FakeRequest("PATCH")
                    .WithJsonBody(
                        "{\"platformOverrides\":[{\"platform\":\"Android\",\"override\":false}]}");
                var cleared = new FakeResponse();
                handler.HandleUpdate(clear, cleared, guid);

                Assert.AreEqual(200, cleared.StatusCode, cleared.Body);
                importer = AssetImporter.GetAtPath(TestAssetPath) as AudioImporter;
                Assert.IsNotNull(importer);
                Assert.IsFalse(importer.ContainsSampleSettingsOverride(BuildTargetGroup.Android));

                var unchanged = new FakeResponse();
                handler.HandleUpdate(clear, unchanged, guid);

                Assert.AreEqual(200, unchanged.StatusCode, unchanged.Body);
                StringAssert.Contains("\"reimported\":false", unchanged.Body);
                StringAssert.Contains("\"diagnostics\":[]", unchanged.Body);
            }
            finally
            {
                AssetDatabase.DeleteAsset(TestDirectory);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void Handler_RestoresImporterSettingsWhenReimportThrows()
        {
            CreateWaveAsset();
            try
            {
                var guid = AssetDatabase.AssetPathToGUID(TestAssetPath);
                var importer = AssetImporter.GetAtPath(TestAssetPath) as AudioImporter;
                Assert.IsNotNull(importer);
                var originalForceToMono = importer.forceToMono;
                var originalDefault = importer.defaultSampleSettings;
                var changedForceToMono = originalForceToMono ? "false" : "true";
                var changedQuality = originalDefault.quality < 0.5f ? 0.75f : 0.25f;
                var handler = new AudioImporterHandler(
                    value => throw new InvalidOperationException("Forced reimport failure."));
                var update = new FakeRequest("PATCH")
                    .WithJsonBody(
                        "{\"forceToMono\":" + changedForceToMono +
                        ",\"defaultSampleSettings\":{\"quality\":" +
                        changedQuality.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        "},\"platformOverrides\":[{\"platform\":\"Android\",\"override\":true," +
                        "\"sampleSettings\":{\"compressionFormat\":\"Vorbis\"}}]}");
                var response = new FakeResponse();

                handler.HandleUpdate(update, response, guid);

                Assert.AreEqual(500, response.StatusCode, response.Body);
                StringAssert.Contains("Original importer settings were restored", response.Body);
                AssertOriginalSettings(originalForceToMono, originalDefault);

                AssetDatabase.ImportAsset(TestAssetPath, ImportAssetOptions.ForceSynchronousImport);
                AssertOriginalSettings(originalForceToMono, originalDefault);
            }
            finally
            {
                AssetDatabase.DeleteAsset(TestDirectory);
                AssetDatabase.Refresh();
            }
        }

        private static void AssertOriginalSettings(
            bool forceToMono,
            AudioImporterSampleSettings defaultSettings)
        {
            var importer = AssetImporter.GetAtPath(TestAssetPath) as AudioImporter;
            Assert.IsNotNull(importer);
            Assert.AreEqual(forceToMono, importer.forceToMono);
            Assert.IsTrue(AudioImporterSettings.Equal(defaultSettings, importer.defaultSampleSettings));
            Assert.IsFalse(importer.ContainsSampleSettingsOverride(BuildTargetGroup.Android));
        }

        private static void CreateWaveAsset()
        {
            if (!AssetDatabase.IsValidFolder(TestDirectory))
                AssetDatabase.CreateFolder("Assets", "UnionAirAudioImporterTests");

            const int sampleRate = 8000;
            const int sampleCount = 800;
            using (var stream = File.Create(TestAssetPath))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + sampleCount * 2);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });
                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(sampleCount * 2);
                for (int i = 0; i < sampleCount; i++) writer.Write((short)0);
            }

            AssetDatabase.ImportAsset(TestAssetPath, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
