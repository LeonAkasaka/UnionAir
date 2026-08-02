using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Serializes the Unity build configuration that a client needs in order to interpret a
    /// compilation result or decide whether a build is possible.
    /// </summary>
    /// <remarks>
    /// None of this is readable from the project directory in any useful form. Scripting settings
    /// and define symbols live in <c>ProjectSettings/ProjectSettings.asset</c> as an internal
    /// serialized layout keyed by platform id, the active build target is per-user Editor state,
    /// and which platform modules are installed is a property of the Editor rather than the project.
    /// </remarks>
    internal static class BuildSettingsReader
    {
        /// <summary>
        /// Writes the whole build settings snapshot for one named build target.
        /// </summary>
        /// <param name="namedBuildTarget">Named build target the scripting settings are read for.</param>
        internal static string SettingsJson(NamedBuildTarget namedBuildTarget)
        {
            var activeTarget = EditorUserBuildSettings.activeBuildTarget;
            var activeGroup = BuildTargetCatalog.GroupOf(activeTarget);

            var sb = new StringBuilder();
            sb.Append("{");
            AppendString(sb, "activeBuildTarget", activeTarget.ToString());
            sb.Append(",");
            AppendString(sb, "activeBuildTargetGroup", activeGroup.ToString());
            sb.Append(",");
            AppendString(sb, "activeNamedBuildTarget", BuildTargetCatalog.Active().TargetName);
            sb.Append(",");
            AppendString(sb, "selectedBuildTargetGroup", EditorUserBuildSettings.selectedBuildTargetGroup.ToString());
            sb.Append(",");
            AppendString(sb, "standaloneBuildSubtarget", EditorUserBuildSettings.standaloneBuildSubtarget.ToString());
            sb.Append(",\"activeBuildTargetInstalled\":");
            sb.Append(RestResponse.FormatBool(BuildTargetCatalog.IsInstalled(activeGroup, activeTarget)));
            sb.Append(",");
            AppendScenes(sb);
            sb.Append(",");
            AppendScripting(sb, namedBuildTarget);
            sb.Append(",");
            AppendOptions(sb);
            sb.Append(",");
            AppendPlayer(sb);
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Writes the build target catalog with per-target module availability.
        /// </summary>
        /// <param name="installedOnly">Whether to omit targets whose platform module is missing.</param>
        internal static string TargetsJson(bool installedOnly)
        {
            var entries = BuildTargetCatalog.List();
            var activeTarget = EditorUserBuildSettings.activeBuildTarget;

            var installedCount = 0;
            foreach (var entry in entries)
                if (entry.Installed) installedCount++;

            var sb = new StringBuilder();
            sb.Append("{");
            AppendString(sb, "activeBuildTarget", activeTarget.ToString());
            sb.Append(",\"total\":").Append(entries.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"installedCount\":").Append(installedCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"installedOnly\":").Append(RestResponse.FormatBool(installedOnly));
            sb.Append(",\"targets\":[");

            var first = true;
            foreach (var entry in entries)
            {
                if (installedOnly && !entry.Installed)
                    continue;

                if (!first) sb.Append(",");
                first = false;

                sb.Append("{");
                AppendString(sb, "buildTarget", entry.Target.ToString());
                sb.Append(",");
                AppendString(sb, "buildTargetGroup", entry.Group.ToString());
                sb.Append(",");
                AppendString(sb, "namedBuildTarget", entry.NamedBuildTarget);
                sb.Append(",\"installed\":").Append(RestResponse.FormatBool(entry.Installed));
                sb.Append(",\"isActive\":").Append(RestResponse.FormatBool(entry.Target == activeTarget));
                sb.Append("}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// Writes the build scene list, with the build index Unity assigns at load time.
        /// </summary>
        /// <remarks>
        /// The build index counts enabled scenes only, so a disabled entry reports <c>null</c>
        /// rather than its position in the list. Getting this wrong is the usual way a client
        /// mispredicts what <c>SceneManager.LoadScene(int)</c> will load.
        /// </remarks>
        private static void AppendScenes(StringBuilder sb)
        {
            var scenes = EditorBuildSettings.scenes ?? new EditorBuildSettingsScene[0];
            var enabledCount = 0;

            sb.Append("\"scenes\":[");
            for (var i = 0; i < scenes.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var scene = scenes[i];
                var enabled = scene != null && scene.enabled;

                sb.Append("{");
                AppendString(sb, "path", scene?.path ?? "");
                sb.Append(",");
                AppendString(sb, "guid", scene == null ? "" : scene.guid.ToString());
                sb.Append(",\"enabled\":").Append(RestResponse.FormatBool(enabled));
                sb.Append(",\"buildIndex\":");
                sb.Append(enabled ? enabledCount.ToString(CultureInfo.InvariantCulture) : "null");
                sb.Append("}");

                if (enabled) enabledCount++;
            }
            sb.Append("],\"sceneCount\":").Append(scenes.Length.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"enabledSceneCount\":").Append(enabledCount.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendScripting(StringBuilder sb, NamedBuildTarget namedBuildTarget)
        {
            sb.Append("\"scripting\":{");
            AppendString(sb, "namedBuildTarget", namedBuildTarget.TargetName);
            sb.Append(",");
            AppendString(sb, "scriptingBackend", Describe(() => PlayerSettings.GetScriptingBackend(namedBuildTarget)));
            sb.Append(",");
            AppendString(sb, "apiCompatibilityLevel", Describe(() => PlayerSettings.GetApiCompatibilityLevel(namedBuildTarget)));
            sb.Append(",");
            AppendString(sb, "il2CppCompilerConfiguration", Describe(() => PlayerSettings.GetIl2CppCompilerConfiguration(namedBuildTarget)));
            sb.Append(",");
            AppendString(sb, "managedStrippingLevel", Describe(() => PlayerSettings.GetManagedStrippingLevel(namedBuildTarget)));
            sb.Append(",");

            var raw = "";
            try { raw = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget) ?? ""; }
            catch (Exception) { raw = ""; }

            AppendString(sb, "defineSymbolsRaw", raw);
            sb.Append(",\"defineSymbols\":[");
            var symbols = SplitDefineSymbols(raw);
            for (var i = 0; i < symbols.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(RestResponse.FormatNullableString(symbols[i]));
            }
            sb.Append("]}");
        }

        /// <summary>
        /// Splits Unity's semicolon-separated define symbol string into individual symbols.
        /// </summary>
        /// <remarks>
        /// Unity accepts separators loosely and stores whatever the Inspector was given, so empty
        /// entries and surrounding whitespace occur in real projects and are dropped here.
        /// </remarks>
        internal static List<string> SplitDefineSymbols(string raw)
        {
            var symbols = new List<string>();
            if (string.IsNullOrEmpty(raw))
                return symbols;

            foreach (var part in raw.Split(';', ',', ' ', '\t', '\n', '\r'))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                    symbols.Add(trimmed);
            }
            return symbols;
        }

        private static void AppendOptions(StringBuilder sb)
        {
            sb.Append("\"options\":{");
            sb.Append("\"development\":").Append(RestResponse.FormatBool(EditorUserBuildSettings.development));
            sb.Append(",\"allowDebugging\":").Append(RestResponse.FormatBool(EditorUserBuildSettings.allowDebugging));
            sb.Append(",\"connectProfiler\":").Append(RestResponse.FormatBool(EditorUserBuildSettings.connectProfiler));
            sb.Append(",\"buildWithDeepProfilingSupport\":")
              .Append(RestResponse.FormatBool(EditorUserBuildSettings.buildWithDeepProfilingSupport));
            sb.Append(",\"waitForManagedDebugger\":")
              .Append(RestResponse.FormatBool(EditorUserBuildSettings.waitForManagedDebugger));
            sb.Append("}");
        }

        private static void AppendPlayer(StringBuilder sb)
        {
            sb.Append("\"player\":{");
            AppendString(sb, "productName", PlayerSettings.productName ?? "");
            sb.Append(",");
            AppendString(sb, "companyName", PlayerSettings.companyName ?? "");
            sb.Append(",");
            AppendString(sb, "bundleVersion", PlayerSettings.bundleVersion ?? "");
            sb.Append(",");
            AppendString(sb, "unityVersion", Application.unityVersion);
            sb.Append("}");
        }

        /// <summary>
        /// Reads a per-target PlayerSettings enum, reporting an unsupported combination as an
        /// empty string rather than failing the whole response.
        /// </summary>
        /// <remarks>
        /// Several of these getters throw for a named build target the Editor has no module for,
        /// which is exactly the case a client asks about before requesting a build.
        /// </remarks>
        private static string Describe<T>(Func<T> read)
        {
            try
            {
                var value = read();
                return value == null ? "" : value.ToString();
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static void AppendString(StringBuilder sb, string key, string value)
        {
            sb.Append("\"").Append(key).Append("\":\"");
            sb.Append(RestResponse.EscapeJson(value));
            sb.Append("\"");
        }
    }
}
