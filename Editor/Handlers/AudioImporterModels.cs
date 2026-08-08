using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal sealed class AudioImporterUpdateRequest
    {
        internal bool HasForceToMono;
        internal bool ForceToMono;
        internal bool HasNormalize;
        internal bool Normalize;
        internal bool HasAmbisonic;
        internal bool Ambisonic;
        internal bool HasLoadInBackground;
        internal bool LoadInBackground;
        internal AudioImporterSampleSettingsPatch DefaultSampleSettings;
        internal readonly List<AudioImporterPlatformUpdate> PlatformOverrides =
            new List<AudioImporterPlatformUpdate>();

        internal bool HasAnyChange =>
            HasForceToMono || HasNormalize || HasAmbisonic || HasLoadInBackground ||
            (DefaultSampleSettings != null && DefaultSampleSettings.HasAnyField) ||
            PlatformOverrides.Count > 0;
    }

    internal sealed class AudioImporterPlatformUpdate
    {
        internal string Platform;
        internal bool Override;
        internal AudioImporterSampleSettingsPatch SampleSettings;
    }

    internal sealed class AudioImporterSampleSettingsPatch
    {
        internal bool HasLoadType;
        internal AudioClipLoadType LoadType;
        internal bool HasCompressionFormat;
        internal AudioCompressionFormat CompressionFormat;
        internal bool HasQuality;
        internal float Quality;
        internal bool HasPreloadAudioData;
        internal bool PreloadAudioData;
        internal bool HasSampleRateSetting;
        internal AudioSampleRateSetting SampleRateSetting;
        internal bool HasSampleRateOverride;
        internal int SampleRateOverride;
        internal bool HasConversionMode;
        internal int ConversionMode;

        internal bool HasAnyField =>
            HasLoadType || HasCompressionFormat || HasQuality || HasPreloadAudioData || HasSampleRateSetting ||
            HasSampleRateOverride || HasConversionMode;

        internal AudioImporterSampleSettings Apply(AudioImporterSampleSettings settings)
        {
            if (HasLoadType) settings.loadType = LoadType;
            if (HasCompressionFormat) settings.compressionFormat = CompressionFormat;
            if (HasQuality) settings.quality = Quality;
            if (HasPreloadAudioData) settings.preloadAudioData = PreloadAudioData;
            if (HasSampleRateSetting)
            {
                settings.sampleRateSetting = SampleRateSetting;
                if (!HasSampleRateOverride && SampleRateSetting != AudioSampleRateSetting.OverrideSampleRate)
                    settings.sampleRateOverride = 0;
            }
            if (HasSampleRateOverride) settings.sampleRateOverride = (uint)SampleRateOverride;
            if (HasConversionMode) settings.conversionMode = ConversionMode;
            return settings;
        }
    }

    internal static class AudioImporterUpdateParser
    {
        private static readonly string[] RequestFields =
        {
            "forceToMono", "normalize", "ambisonic", "loadInBackground",
            "defaultSampleSettings", "platformOverrides"
        };

        private static readonly string[] SampleFields =
        {
            "loadType", "compressionFormat", "quality", "preloadAudioData", "sampleRateSetting",
            "sampleRateOverride", "conversionMode"
        };

        private static readonly string[] PlatformFields =
        {
            "platform", "override", "sampleSettings"
        };

        internal static bool TryParse(string body, out AudioImporterUpdateRequest result, out string error)
        {
            result = null;
            if (!RequestBodyReader.TryValidateObjectFields(body, RequestFields, out error))
                return false;

            var parsed = new AudioImporterUpdateRequest();
            if (!TryReadBool(body, "forceToMono", out parsed.HasForceToMono, out parsed.ForceToMono, out error) ||
                !TryReadBool(body, "normalize", out parsed.HasNormalize, out parsed.Normalize, out error) ||
                !TryReadBool(body, "ambisonic", out parsed.HasAmbisonic, out parsed.Ambisonic, out error) ||
                !TryReadBool(body, "loadInBackground", out parsed.HasLoadInBackground,
                    out parsed.LoadInBackground, out error))
                return false;

            if (RequestBodyReader.HasTopLevelField(body, "defaultSampleSettings"))
            {
                var sampleJson = RequestBodyReader.GetObject(body, "defaultSampleSettings");
                if (sampleJson == null)
                {
                    error = "'defaultSampleSettings' must be a JSON object.";
                    return false;
                }
                if (!TryParseSampleSettings(sampleJson, "defaultSampleSettings", out parsed.DefaultSampleSettings,
                        out error))
                    return false;
            }

            List<string> platformElements;
            bool platformsPresent;
            if (!RequestBodyReader.TryGetArrayElements(
                    body, "platformOverrides", out platformElements, out platformsPresent, out error))
                return false;

            if (platformsPresent)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < platformElements.Count; i++)
                {
                    var element = platformElements[i];
                    var prefix = "platformOverrides[" + i + "]";
                    if (!RequestBodyReader.TryValidateObjectFields(element, PlatformFields, out error))
                    {
                        error = prefix + ": " + error;
                        return false;
                    }

                    string platform;
                    bool platformPresent;
                    if (!RequestBodyReader.TryGetStringValue(element, "platform", out platform, out platformPresent) ||
                        !platformPresent || string.IsNullOrWhiteSpace(platform))
                    {
                        error = prefix + ".platform must be a non-empty JSON string.";
                        return false;
                    }
                    if (!seen.Add(platform))
                    {
                        error = "Duplicate platform override '" + platform + "'.";
                        return false;
                    }

                    bool overrideValue;
                    bool overridePresent;
                    if (!RequestBodyReader.TryGetBoolValue(
                            element, "override", out overrideValue, out overridePresent) || !overridePresent)
                    {
                        error = prefix + ".override must be a JSON boolean.";
                        return false;
                    }

                    var update = new AudioImporterPlatformUpdate
                    {
                        Platform = platform,
                        Override = overrideValue
                    };
                    var hasSettings = RequestBodyReader.HasTopLevelField(element, "sampleSettings");
                    if (overrideValue)
                    {
                        var sampleJson = RequestBodyReader.GetObject(element, "sampleSettings");
                        if (sampleJson == null)
                        {
                            error = prefix + ".sampleSettings must be a JSON object when override is true.";
                            return false;
                        }
                        if (!TryParseSampleSettings(
                                sampleJson, prefix + ".sampleSettings", out update.SampleSettings, out error))
                            return false;
                    }
                    else if (hasSettings)
                    {
                        error = prefix + ".sampleSettings is not allowed when override is false.";
                        return false;
                    }

                    parsed.PlatformOverrides.Add(update);
                }
            }

            if (!parsed.HasAnyChange)
            {
                error = "Request does not contain a setting to update.";
                return false;
            }

            result = parsed;
            error = null;
            return true;
        }

        private static bool TryParseSampleSettings(
            string json,
            string prefix,
            out AudioImporterSampleSettingsPatch result,
            out string error)
        {
            result = null;
            if (!RequestBodyReader.TryValidateObjectFields(json, SampleFields, out error))
            {
                error = prefix + ": " + error;
                return false;
            }

            var parsed = new AudioImporterSampleSettingsPatch();
            string text;
            bool present;
            if (!RequestBodyReader.TryGetStringValue(json, "loadType", out text, out present))
            {
                error = prefix + ".loadType must be a JSON string.";
                return false;
            }
            if (present)
            {
                if (!TryParseEnum(text, out parsed.LoadType))
                {
                    error = prefix + ".loadType must be DecompressOnLoad, CompressedInMemory, or Streaming.";
                    return false;
                }
                parsed.HasLoadType = true;
            }

            if (!RequestBodyReader.TryGetStringValue(json, "compressionFormat", out text, out present))
            {
                error = prefix + ".compressionFormat must be a JSON string.";
                return false;
            }
            if (present)
            {
                if (!TryParseEnum(text, out parsed.CompressionFormat))
                {
                    error = prefix + ".compressionFormat is not a known AudioCompressionFormat.";
                    return false;
                }
                parsed.HasCompressionFormat = true;
            }

            if (!RequestBodyReader.TryGetStringValue(json, "sampleRateSetting", out text, out present))
            {
                error = prefix + ".sampleRateSetting must be a JSON string.";
                return false;
            }
            if (present)
            {
                if (!TryParseEnum(text, out parsed.SampleRateSetting))
                {
                    error = prefix +
                            ".sampleRateSetting must be PreserveSampleRate, OptimizeSampleRate, or OverrideSampleRate.";
                    return false;
                }
                parsed.HasSampleRateSetting = true;
            }

            float quality;
            if (!RequestBodyReader.TryGetFloatValue(json, "quality", out quality, out present))
            {
                error = prefix + ".quality must be a finite JSON number.";
                return false;
            }
            if (present)
            {
                if (quality < 0f || quality > 1f)
                {
                    error = prefix + ".quality must be between 0 and 1.";
                    return false;
                }
                parsed.HasQuality = true;
                parsed.Quality = quality;
            }

            bool preload;
            if (!RequestBodyReader.TryGetBoolValue(json, "preloadAudioData", out preload, out present))
            {
                error = prefix + ".preloadAudioData must be a JSON boolean.";
                return false;
            }
            if (present)
            {
                parsed.HasPreloadAudioData = true;
                parsed.PreloadAudioData = preload;
            }

            int integer;
            if (!RequestBodyReader.TryGetIntValue(json, "sampleRateOverride", out integer, out present))
            {
                error = prefix + ".sampleRateOverride must be a JSON integer.";
                return false;
            }
            if (present)
            {
                if (integer < 0 || integer > 192000)
                {
                    error = prefix + ".sampleRateOverride must be between 0 and 192000.";
                    return false;
                }
                parsed.HasSampleRateOverride = true;
                parsed.SampleRateOverride = integer;
            }

            if (!RequestBodyReader.TryGetIntValue(json, "conversionMode", out integer, out present))
            {
                error = prefix + ".conversionMode must be a JSON integer.";
                return false;
            }
            if (present)
            {
                if (integer != 0)
                {
                    error = prefix +
                            ".conversionMode supports only 0; Unity exposes no defined non-zero conversion flags.";
                    return false;
                }
                parsed.HasConversionMode = true;
                parsed.ConversionMode = integer;
            }

            if (!parsed.HasAnyField)
            {
                error = prefix + " must contain at least one sample setting.";
                return false;
            }

            result = parsed;
            error = null;
            return true;
        }

        private static bool TryReadBool(
            string json,
            string field,
            out bool present,
            out bool value,
            out string error)
        {
            if (!RequestBodyReader.TryGetBoolValue(json, field, out value, out present))
            {
                error = "'" + field + "' must be a JSON boolean.";
                return false;
            }
            error = null;
            return true;
        }

        private static bool TryParseEnum<T>(string text, out T value) where T : struct
        {
            if (Enum.TryParse(text, true, out value) && Enum.IsDefined(typeof(T), value))
                return true;
            value = default(T);
            return false;
        }
    }

    internal static class AudioImporterPlatformCatalog
    {
        internal sealed class Entry
        {
            internal string Name;
            internal BuildTargetGroup Group;
            internal bool Installed;
            internal AudioCompressionFormat[] CompressionFormats;
        }

        private static readonly AudioCompressionFormat[] DefaultFormats =
        {
            AudioCompressionFormat.PCM,
            AudioCompressionFormat.Vorbis,
            AudioCompressionFormat.ADPCM
        };

        internal static AudioCompressionFormat[] GetDefaultFormats()
            => (AudioCompressionFormat[])DefaultFormats.Clone();

        internal static List<Entry> List()
        {
            var byName = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in BuildTargetCatalog.List())
            {
                if (target.Group == BuildTargetGroup.Unknown) continue;
                var name = CanonicalName(target.Group);
                Entry entry;
                if (!byName.TryGetValue(name, out entry))
                {
                    entry = new Entry
                    {
                        Name = name,
                        Group = target.Group,
                        CompressionFormats = FormatsFor(name)
                    };
                    byName.Add(name, entry);
                }
                entry.Installed |= target.Installed;
            }

            var entries = new List<Entry>(byName.Values);
            entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            return entries;
        }

        private static string CanonicalName(BuildTargetGroup group)
        {
            // BuildTargetGroup keeps old aliases before their current names, so Enum.ToString()
            // returns iPhone for iOS and Metro for WSA. The API uses the current public names.
            var name = group.ToString();
            if (name == "iPhone") return "iOS";
            if (name == "Metro") return "WSA";
            return name;
        }

        internal static bool TryFind(string name, out Entry entry)
        {
            foreach (var candidate in List())
            {
                if (!string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                entry = candidate;
                return true;
            }
            entry = null;
            return false;
        }

        internal static bool Contains(AudioCompressionFormat[] formats, AudioCompressionFormat format)
        {
            foreach (var candidate in formats)
                if (candidate == format) return true;
            return false;
        }

        internal static string Names(AudioCompressionFormat[] formats)
        {
            var names = new string[formats.Length];
            for (int i = 0; i < formats.Length; i++) names[i] = formats[i].ToString();
            return string.Join(", ", names);
        }

        private static AudioCompressionFormat[] FormatsFor(string platform)
        {
            // This mirrors the compatibility choices displayed by AudioImporterInspector on the
            // supported Editor range. String matching avoids naming platform enum members that are
            // obsolete in one Editor but current in another.
            switch (platform)
            {
                case "WebGL":
                    return new[] { AudioCompressionFormat.AAC };
                case "Standalone":
                case "WSA":
                    return GetDefaultFormats();
                case "PS4":
                case "PS5":
                    return new[]
                    {
                        AudioCompressionFormat.PCM, AudioCompressionFormat.Vorbis,
                        AudioCompressionFormat.ADPCM, AudioCompressionFormat.MP3,
                        AudioCompressionFormat.ATRAC9
                    };
                case "GameCoreScarlett":
                case "GameCoreXboxSeries":
                case "GameCoreXboxOne":
                    return new[]
                    {
                        AudioCompressionFormat.PCM, AudioCompressionFormat.Vorbis,
                        AudioCompressionFormat.ADPCM, AudioCompressionFormat.MP3,
                        AudioCompressionFormat.XMA
                    };
                default:
                    return new[]
                    {
                        AudioCompressionFormat.PCM, AudioCompressionFormat.Vorbis,
                        AudioCompressionFormat.ADPCM, AudioCompressionFormat.MP3
                    };
            }
        }
    }

    internal static class AudioImporterSettings
    {
        internal static bool TryGetNormalize(AudioImporter importer, out bool value)
        {
            var property = FindNormalizeProperty(importer);
            if (property == null)
            {
                value = false;
                return false;
            }
            value = property.boolValue;
            return true;
        }

        internal static bool TrySetNormalize(AudioImporter importer, bool value)
        {
            var serialized = new SerializedObject(importer);
            var property = serialized.FindProperty("normalize") ?? serialized.FindProperty("m_Normalize");
            if (property == null) return false;
            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        internal static bool Equal(AudioImporterSampleSettings a, AudioImporterSampleSettings b)
            => a.loadType == b.loadType &&
               a.compressionFormat == b.compressionFormat &&
               Math.Abs(a.quality - b.quality) < 0.000001f &&
               a.preloadAudioData == b.preloadAudioData &&
               a.sampleRateSetting == b.sampleRateSetting &&
               a.sampleRateOverride == b.sampleRateOverride &&
               a.conversionMode == b.conversionMode;

        private static SerializedProperty FindNormalizeProperty(AudioImporter importer)
        {
            var serialized = new SerializedObject(importer);
            return serialized.FindProperty("normalize") ?? serialized.FindProperty("m_Normalize");
        }
    }

    internal static class AudioImporterJson
    {
        internal static string Build(
            string guid,
            string assetPath,
            AudioImporter importer,
            bool? reimported)
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\"guid\":\"").Append(RestResponse.EscapeJson(guid))
              .Append("\",\"assetPath\":\"").Append(RestResponse.EscapeJson(assetPath)).Append('"')
              .Append(",\"forceToMono\":").Append(Bool(importer.forceToMono));

            bool normalize;
            sb.Append(",\"normalize\":");
            sb.Append(AudioImporterSettings.TryGetNormalize(importer, out normalize) ? Bool(normalize) : "null");
            sb.Append(",\"ambisonic\":").Append(Bool(importer.ambisonic))
              .Append(",\"loadInBackground\":").Append(Bool(importer.loadInBackground))
              .Append(",\"defaultSampleSettings\":");
            AppendSampleSettings(sb, importer.defaultSampleSettings);
            sb.Append(",\"defaultCompressionFormats\":");
            AppendFormats(sb, AudioImporterPlatformCatalog.GetDefaultFormats());
            sb.Append(",\"supportedConversionModes\":[0],\"platforms\":[");

            var platforms = AudioImporterPlatformCatalog.List();
            for (int i = 0; i < platforms.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var platform = platforms[i];
                var hasOverride = importer.ContainsSampleSettingsOverride(platform.Group);
                var effective = importer.GetOverrideSampleSettings(platform.Group);
                sb.Append("{\"platform\":\"").Append(RestResponse.EscapeJson(platform.Name))
                  .Append("\",\"installed\":").Append(Bool(platform.Installed))
                  .Append(",\"compressionFormats\":");
                AppendFormats(sb, platform.CompressionFormats);
                sb.Append(",\"override\":").Append(Bool(hasOverride))
                  .Append(",\"inherited\":");
                AppendSampleSettings(sb, importer.defaultSampleSettings);
                sb.Append(",\"effective\":");
                AppendSampleSettings(sb, effective);
                sb.Append('}');
            }
            sb.Append(']');

            AppendAudioClip(sb, AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath));
            if (reimported.HasValue)
            {
                sb.Append(",\"reimported\":").Append(Bool(reimported.Value));
                if (reimported.Value) AppendDiagnostics(sb, AssetImporter.GetImportLog(assetPath));
                else sb.Append(",\"diagnostics\":[]");
            }
            sb.Append('}');
            return sb.ToString();
        }

        internal static void AppendSampleSettings(StringBuilder sb, AudioImporterSampleSettings settings)
        {
            sb.Append("{\"loadType\":\"").Append(settings.loadType)
              .Append("\",\"compressionFormat\":\"").Append(settings.compressionFormat)
              .Append("\",\"quality\":").Append(RestResponse.FormatFloat(settings.quality))
              .Append(",\"preloadAudioData\":").Append(Bool(settings.preloadAudioData))
              .Append(",\"sampleRateSetting\":\"").Append(settings.sampleRateSetting)
              .Append("\",\"sampleRateOverride\":").Append(settings.sampleRateOverride)
              .Append(",\"conversionMode\":").Append(settings.conversionMode)
              .Append('}');
        }

        private static void AppendFormats(StringBuilder sb, AudioCompressionFormat[] formats)
        {
            sb.Append('[');
            for (int i = 0; i < formats.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(formats[i]).Append('"');
            }
            sb.Append(']');
        }

        private static void AppendAudioClip(StringBuilder sb, AudioClip clip)
        {
            sb.Append(",\"audioClip\":");
            if (clip == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append("{\"name\":\"").Append(RestResponse.EscapeJson(clip.name))
              .Append("\",\"length\":").Append(RestResponse.FormatFloat(clip.length))
              .Append(",\"channels\":").Append(clip.channels)
              .Append(",\"frequency\":").Append(clip.frequency)
              .Append(",\"samples\":").Append(clip.samples)
              .Append(",\"loadType\":\"").Append(clip.loadType)
              .Append("\",\"preloadAudioData\":").Append(Bool(clip.preloadAudioData))
              .Append(",\"ambisonic\":").Append(Bool(clip.ambisonic))
              .Append(",\"loadInBackground\":").Append(Bool(clip.loadInBackground))
              .Append(",\"loadState\":\"").Append(clip.loadState).Append("\"}");
        }

        private static void AppendDiagnostics(StringBuilder sb, ImportLog log)
        {
            sb.Append(",\"diagnostics\":[");
            var wrote = false;
            if (log != null && log.logEntries != null)
            {
                foreach (var entry in log.logEntries)
                {
                    var isError = (entry.flags & ImportLogFlags.Error) != 0;
                    var isWarning = (entry.flags & ImportLogFlags.Warning) != 0;
                    if (!isError && !isWarning) continue;
                    if (wrote) sb.Append(',');
                    wrote = true;
                    sb.Append("{\"severity\":\"").Append(isError ? "error" : "warning")
                      .Append("\",\"message\":\"").Append(RestResponse.EscapeJson(entry.message))
                      .Append("\",\"file\":\"").Append(RestResponse.EscapeJson(entry.file ?? ""))
                      .Append("\",\"line\":").Append(entry.line).Append('}');
                }
            }
            sb.Append(']');
        }

        private static string Bool(bool value) => value ? "true" : "false";
    }
}
