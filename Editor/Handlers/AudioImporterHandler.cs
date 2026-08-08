using System;
using System.Collections.Generic;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>Reads and updates typed AudioImporter settings by asset GUID.</summary>
    internal sealed class AudioImporterHandler
    {
        private readonly Action<AudioImporter> _saveAndReimport;

        private sealed class PreparedPlatformUpdate
        {
            internal AudioImporterPlatformCatalog.Entry Platform;
            internal bool HadOverride;
            internal AudioImporterSampleSettings OriginalSettings;
            internal bool RequestedOverride;
            internal AudioImporterSampleSettings RequestedSettings;
            internal bool Changed;
        }

        internal AudioImporterHandler(Action<AudioImporter> saveAndReimport = null)
        {
            _saveAndReimport = saveAndReimport ?? (value => value.SaveAndReimport());
        }

        internal void HandleGet(UnionAirResponse response, string guid)
        {
            string assetPath;
            AudioImporter importer;
            if (!TryResolve(guid, response, out assetPath, out importer)) return;

            RestResponse.Send(response, AudioImporterJson.Build(guid, assetPath, importer, null));
        }

        internal void HandleUpdate(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            string assetPath;
            AudioImporter importer;
            if (!TryResolve(guid, response, out assetPath, out importer)) return;

            AudioImporterUpdateRequest update;
            string error;
            if (!AudioImporterUpdateParser.TryParse(RequestBodyReader.ReadString(request), out update, out error))
            {
                RestResponse.SendError(response, error, 400);
                return;
            }

            bool originalNormalize;
            var normalizeSupported = AudioImporterSettings.TryGetNormalize(importer, out originalNormalize);
            if (update.HasNormalize && !normalizeSupported)
            {
                RestResponse.SendError(
                    response, "The normalize setting is not available in this Unity Editor version.", 400);
                return;
            }

            var originalDefault = importer.defaultSampleSettings;
            var requestedDefault = update.DefaultSampleSettings == null
                ? originalDefault
                : update.DefaultSampleSettings.Apply(originalDefault);
            if (update.DefaultSampleSettings != null &&
                !TryValidateSampleSettings(
                    requestedDefault,
                    update.DefaultSampleSettings,
                    AudioImporterPlatformCatalog.GetDefaultFormats(),
                    "defaultSampleSettings",
                    out error))
            {
                RestResponse.SendError(response, error, 400);
                return;
            }

            var preparedPlatforms = new List<PreparedPlatformUpdate>();
            foreach (var platformUpdate in update.PlatformOverrides)
            {
                AudioImporterPlatformCatalog.Entry platform;
                if (!AudioImporterPlatformCatalog.TryFind(platformUpdate.Platform, out platform))
                {
                    var names = new List<string>();
                    foreach (var candidate in AudioImporterPlatformCatalog.List()) names.Add(candidate.Name);
                    RestResponse.SendError(
                        response,
                        "Unknown platform '" + platformUpdate.Platform + "'. Known values for this Editor: " +
                        string.Join(", ", names.ToArray()) + ".",
                        400);
                    return;
                }

                var hadOverride = importer.ContainsSampleSettingsOverride(platform.Group);
                var original = importer.GetOverrideSampleSettings(platform.Group);
                var requested = original;
                if (platformUpdate.Override)
                {
                    requested = platformUpdate.SampleSettings.Apply(original);
                    if (!TryValidateSampleSettings(
                            requested,
                            platformUpdate.SampleSettings,
                            platform.CompressionFormats,
                            "platformOverrides['" + platform.Name + "'].sampleSettings",
                            out error))
                    {
                        RestResponse.SendError(response, error, 400);
                        return;
                    }
                }

                preparedPlatforms.Add(new PreparedPlatformUpdate
                {
                    Platform = platform,
                    HadOverride = hadOverride,
                    OriginalSettings = original,
                    RequestedOverride = platformUpdate.Override,
                    RequestedSettings = requested,
                    Changed = platformUpdate.Override
                        ? !hadOverride || !AudioImporterSettings.Equal(original, requested)
                        : hadOverride
                });
            }

            var changed =
                update.HasForceToMono && importer.forceToMono != update.ForceToMono ||
                update.HasNormalize && originalNormalize != update.Normalize ||
                update.HasAmbisonic && importer.ambisonic != update.Ambisonic ||
                update.HasLoadInBackground && importer.loadInBackground != update.LoadInBackground ||
                update.DefaultSampleSettings != null &&
                !AudioImporterSettings.Equal(originalDefault, requestedDefault);
            foreach (var platform in preparedPlatforms) changed |= platform.Changed;

            if (!changed)
            {
                RestResponse.Send(response, AudioImporterJson.Build(guid, assetPath, importer, false));
                return;
            }

            var originalForceToMono = importer.forceToMono;
            var originalAmbisonic = importer.ambisonic;
            var originalBackground = importer.loadInBackground;

            foreach (var platform in preparedPlatforms)
            {
                if (!platform.Changed) continue;
                var accepted = platform.RequestedOverride
                    ? importer.SetOverrideSampleSettings(platform.Platform.Group, platform.RequestedSettings)
                    : importer.ClearSampleSettingOverride(platform.Platform.Group);
                if (accepted) continue;

                Restore(
                    importer,
                    preparedPlatforms,
                    originalDefault,
                    originalForceToMono,
                    originalNormalize,
                    normalizeSupported,
                    originalAmbisonic,
                    originalBackground);
                RestResponse.SendError(
                    response,
                    "Unity refused the sample settings override for platform '" + platform.Platform.Name +
                    "'. No settings were reimported.",
                    400);
                return;
            }

            if (update.HasForceToMono) importer.forceToMono = update.ForceToMono;
            if (update.HasAmbisonic) importer.ambisonic = update.Ambisonic;
            if (update.HasLoadInBackground) importer.loadInBackground = update.LoadInBackground;
            if (update.DefaultSampleSettings != null) importer.defaultSampleSettings = requestedDefault;
            if (update.HasNormalize && !AudioImporterSettings.TrySetNormalize(importer, update.Normalize))
            {
                Restore(
                    importer,
                    preparedPlatforms,
                    originalDefault,
                    originalForceToMono,
                    originalNormalize,
                    normalizeSupported,
                    originalAmbisonic,
                    originalBackground);
                RestResponse.SendError(response, "Unity could not update the normalize setting.", 500);
                return;
            }

            try
            {
                _saveAndReimport(importer);
            }
            catch (Exception ex)
            {
                string rollbackError;
                var restored = TryRestoreAfterReimportFailure(
                    assetPath,
                    importer,
                    preparedPlatforms,
                    originalDefault,
                    originalForceToMono,
                    originalNormalize,
                    normalizeSupported,
                    originalAmbisonic,
                    originalBackground,
                    out rollbackError);
                var message = "Audio reimport failed: " + ex.Message;
                message += restored
                    ? " Original importer settings were restored."
                    : " Rollback failed: " + rollbackError;
                RestResponse.SendError(response, message, 500);
                return;
            }

            var finalImporter = AssetImporter.GetAtPath(assetPath) as AudioImporter;
            if (finalImporter == null)
            {
                RestResponse.SendError(response, "The reimported asset no longer has an AudioImporter.", 500);
                return;
            }
            RestResponse.Send(response, AudioImporterJson.Build(guid, assetPath, finalImporter, true));
        }

        private static bool TryResolve(
            string guid,
            UnionAirResponse response,
            out string assetPath,
            out AudioImporter importer)
        {
            assetPath = AssetDatabase.GUIDToAssetPath(guid);
            importer = null;
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, "No asset found for GUID: " + guid);
                return false;
            }

            importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
            if (importer != null) return true;
            RestResponse.SendError(response, "Asset does not use an AudioImporter: " + assetPath, 400);
            return false;
        }

        private static bool TryValidateSampleSettings(
            AudioImporterSampleSettings settings,
            AudioImporterSampleSettingsPatch patch,
            UnityEngine.AudioCompressionFormat[] formats,
            string prefix,
            out string error)
        {
            if (!AudioImporterPlatformCatalog.Contains(formats, settings.compressionFormat))
            {
                error = prefix + ".compressionFormat '" + settings.compressionFormat +
                        "' is not supported. Use " + AudioImporterPlatformCatalog.Names(formats) + ".";
                return false;
            }
            if (settings.quality < 0f || settings.quality > 1f ||
                float.IsNaN(settings.quality) || float.IsInfinity(settings.quality))
            {
                error = prefix + ".quality must be between 0 and 1.";
                return false;
            }
            if (settings.sampleRateSetting == AudioSampleRateSetting.OverrideSampleRate)
            {
                if (settings.sampleRateOverride == 0 || settings.sampleRateOverride > 192000)
                {
                    error = prefix +
                            ".sampleRateOverride must be between 1 and 192000 with OverrideSampleRate.";
                    return false;
                }
            }
            else if (patch.HasSampleRateOverride && settings.sampleRateOverride != 0)
            {
                error = prefix +
                        ".sampleRateOverride must be 0 unless sampleRateSetting is OverrideSampleRate.";
                return false;
            }
            if (settings.conversionMode != 0)
            {
                error = prefix +
                        ".conversionMode supports only 0; Unity exposes no defined non-zero conversion flags.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryRestoreAfterReimportFailure(
            string assetPath,
            AudioImporter importer,
            List<PreparedPlatformUpdate> platforms,
            AudioImporterSampleSettings defaultSettings,
            bool forceToMono,
            bool normalize,
            bool normalizeSupported,
            bool ambisonic,
            bool background,
            out string error)
        {
            try
            {
                var rollbackImporter = AssetImporter.GetAtPath(assetPath) as AudioImporter ?? importer;
                if (rollbackImporter == null)
                {
                    error = "The asset no longer has an AudioImporter.";
                    return false;
                }

                Restore(
                    rollbackImporter,
                    platforms,
                    defaultSettings,
                    forceToMono,
                    normalize,
                    normalizeSupported,
                    ambisonic,
                    background);
                if (!MatchesOriginal(
                        rollbackImporter,
                        platforms,
                        defaultSettings,
                        forceToMono,
                        normalize,
                        normalizeSupported,
                        ambisonic,
                        background,
                        out error))
                    return false;

                AssetDatabase.WriteImportSettingsIfDirty(assetPath);
                error = null;
                return true;
            }
            catch (Exception rollbackException)
            {
                error = rollbackException.Message;
                return false;
            }
        }

        private static bool MatchesOriginal(
            AudioImporter importer,
            List<PreparedPlatformUpdate> platforms,
            AudioImporterSampleSettings defaultSettings,
            bool forceToMono,
            bool normalize,
            bool normalizeSupported,
            bool ambisonic,
            bool background,
            out string error)
        {
            if (!AudioImporterSettings.Equal(importer.defaultSampleSettings, defaultSettings) ||
                importer.forceToMono != forceToMono ||
                importer.ambisonic != ambisonic ||
                importer.loadInBackground != background)
            {
                error = "Unity did not restore the original global/default importer settings.";
                return false;
            }

            if (normalizeSupported)
            {
                bool restoredNormalize;
                if (!AudioImporterSettings.TryGetNormalize(importer, out restoredNormalize) ||
                    restoredNormalize != normalize)
                {
                    error = "Unity did not restore the original normalize setting.";
                    return false;
                }
            }

            foreach (var platform in platforms)
            {
                if (!platform.Changed) continue;
                var hasOverride = importer.ContainsSampleSettingsOverride(platform.Platform.Group);
                if (hasOverride != platform.HadOverride ||
                    hasOverride && !AudioImporterSettings.Equal(
                        importer.GetOverrideSampleSettings(platform.Platform.Group),
                        platform.OriginalSettings))
                {
                    error = "Unity did not restore the original override for platform '" +
                            platform.Platform.Name + "'.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static void Restore(
            AudioImporter importer,
            List<PreparedPlatformUpdate> platforms,
            AudioImporterSampleSettings defaultSettings,
            bool forceToMono,
            bool normalize,
            bool normalizeSupported,
            bool ambisonic,
            bool background)
        {
            foreach (var platform in platforms)
            {
                if (!platform.Changed) continue;
                if (platform.HadOverride)
                    importer.SetOverrideSampleSettings(platform.Platform.Group, platform.OriginalSettings);
                else if (importer.ContainsSampleSettingsOverride(platform.Platform.Group))
                    importer.ClearSampleSettingOverride(platform.Platform.Group);
            }

            importer.defaultSampleSettings = defaultSettings;
            importer.forceToMono = forceToMono;
            importer.ambisonic = ambisonic;
            importer.loadInBackground = background;
            if (normalizeSupported) AudioImporterSettings.TrySetNormalize(importer, normalize);
        }
    }
}
