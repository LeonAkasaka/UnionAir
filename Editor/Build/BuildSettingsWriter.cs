using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Build;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>Outcome of one attempted setting change.</summary>
    internal sealed class BuildSettingsChangeResult
    {
        internal string Setting = "";

        /// <summary><c>applied</c>, <c>unchanged</c>, or <c>failed</c>.</summary>
        internal string Outcome = "";

        /// <summary><c>project</c> for a file every user of the project shares, <c>user</c> for a local one.</summary>
        internal string Persistence = "";

        internal string File = "";
        internal string Previous = "";
        internal string Value = "";
        internal string Error = "";
    }

    /// <summary>
    /// Applies build settings changes and reports exactly what happened to each one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Partial failure is reported, never rolled back.</b> Every value is validated before
    /// anything is written, so the only way a change fails here is Unity refusing it — and at that
    /// point undoing the earlier writes could fail too, leaving a third state that matches neither
    /// what was asked for nor what was there. Instead each change reports its own outcome and the
    /// response carries the resulting settings, so the caller reads the truth rather than inferring
    /// it. Callers that need all-or-nothing should issue one change per request.
    /// </para>
    /// <para>
    /// <b>Persistence is not uniform, and the response says which kind each change was.</b>
    /// Scripting settings go to <c>ProjectSettings/ProjectSettings.asset</c> and the build scene
    /// list to <c>ProjectSettings/EditorBuildSettings.asset</c>; both are project files that appear
    /// as Git diffs and reach every user of the project. Build flags go to
    /// <c>Library/EditorUserBuildSettings.asset</c>, which is per user and not shared. Presenting
    /// the two as one kind of "setting" would be the misleading part.
    /// </para>
    /// </remarks>
    internal static class BuildSettingsWriter
    {
        private const string ProjectSettingsFile = "ProjectSettings/ProjectSettings.asset";
        private const string BuildSettingsFile = "ProjectSettings/EditorBuildSettings.asset";
        private const string UserBuildSettingsFile = "Library/EditorUserBuildSettings.asset";

        private const string ProjectPersistence = "project";
        private const string UserPersistence = "user";

        /// <summary>
        /// Applies a validated settings plan to one named build target.
        /// </summary>
        internal static List<BuildSettingsChangeResult> Apply(
            BuildSettingsWritePlan plan,
            NamedBuildTarget namedBuildTarget)
        {
            var results = new List<BuildSettingsChangeResult>();

            if (plan.HasScriptingBackend)
                Change(results, "scriptingBackend", ProjectPersistence, ProjectSettingsFile,
                    () => PlayerSettings.GetScriptingBackend(namedBuildTarget).ToString(),
                    plan.ScriptingBackend.ToString(),
                    () => PlayerSettings.SetScriptingBackend(namedBuildTarget, plan.ScriptingBackend));

            if (plan.HasApiCompatibilityLevel)
                Change(results, "apiCompatibilityLevel", ProjectPersistence, ProjectSettingsFile,
                    () => PlayerSettings.GetApiCompatibilityLevel(namedBuildTarget).ToString(),
                    plan.ApiCompatibilityLevel.ToString(),
                    () => PlayerSettings.SetApiCompatibilityLevel(namedBuildTarget, plan.ApiCompatibilityLevel));

            if (plan.HasManagedStrippingLevel)
                Change(results, "managedStrippingLevel", ProjectPersistence, ProjectSettingsFile,
                    () => PlayerSettings.GetManagedStrippingLevel(namedBuildTarget).ToString(),
                    plan.ManagedStrippingLevel.ToString(),
                    () => PlayerSettings.SetManagedStrippingLevel(namedBuildTarget, plan.ManagedStrippingLevel));

            if (plan.HasIl2CppCompilerConfiguration)
                Change(results, "il2CppCompilerConfiguration", ProjectPersistence, ProjectSettingsFile,
                    () => PlayerSettings.GetIl2CppCompilerConfiguration(namedBuildTarget).ToString(),
                    plan.Il2CppCompilerConfiguration.ToString(),
                    () => PlayerSettings.SetIl2CppCompilerConfiguration(namedBuildTarget, plan.Il2CppCompilerConfiguration));

            ApplyDefineSymbols(results, plan, namedBuildTarget);

            if (plan.HasDevelopment)
                Change(results, "development", UserPersistence, UserBuildSettingsFile,
                    () => Bool(EditorUserBuildSettings.development),
                    Bool(plan.Development),
                    () => EditorUserBuildSettings.development = plan.Development);

            if (plan.HasAllowDebugging)
                Change(results, "allowDebugging", UserPersistence, UserBuildSettingsFile,
                    () => Bool(EditorUserBuildSettings.allowDebugging),
                    Bool(plan.AllowDebugging),
                    () => EditorUserBuildSettings.allowDebugging = plan.AllowDebugging);

            if (plan.HasConnectProfiler)
                Change(results, "connectProfiler", UserPersistence, UserBuildSettingsFile,
                    () => Bool(EditorUserBuildSettings.connectProfiler),
                    Bool(plan.ConnectProfiler),
                    () => EditorUserBuildSettings.connectProfiler = plan.ConnectProfiler);

            if (plan.HasDeepProfiling)
                Change(results, "buildWithDeepProfilingSupport", UserPersistence, UserBuildSettingsFile,
                    () => Bool(EditorUserBuildSettings.buildWithDeepProfilingSupport),
                    Bool(plan.DeepProfiling),
                    () => EditorUserBuildSettings.buildWithDeepProfilingSupport = plan.DeepProfiling);

            if (plan.HasWaitForManagedDebugger)
                Change(results, "waitForManagedDebugger", UserPersistence, UserBuildSettingsFile,
                    () => Bool(EditorUserBuildSettings.waitForManagedDebugger),
                    Bool(plan.WaitForManagedDebugger),
                    () => EditorUserBuildSettings.waitForManagedDebugger = plan.WaitForManagedDebugger);

            return results;
        }

        private static void ApplyDefineSymbols(
            List<BuildSettingsChangeResult> results,
            BuildSettingsWritePlan plan,
            NamedBuildTarget namedBuildTarget)
        {
            if (!plan.HasDefineSymbols &&
                plan.AddDefineSymbols.Count == 0 &&
                plan.RemoveDefineSymbols.Count == 0)
                return;

            string current;
            try { current = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget) ?? ""; }
            catch (Exception ex)
            {
                results.Add(new BuildSettingsChangeResult
                {
                    Setting = "defineSymbols",
                    Outcome = "failed",
                    Persistence = ProjectPersistence,
                    File = ProjectSettingsFile,
                    Error = "The current define symbols could not be read: " + ex.Message,
                });
                return;
            }

            var symbols = plan.HasDefineSymbols
                ? new List<string>(plan.DefineSymbols)
                : Merge(BuildSettingsReader.SplitDefineSymbols(current), plan);

            // Semicolon-joined, which is the separator Unity itself writes back. The stored string
            // is normalized as a side effect: a list Unity accepted with stray commas or spaces
            // comes back canonical, which is what makes a re-read comparable.
            var next = string.Join(";", symbols.ToArray());

            Change(results, "defineSymbols", ProjectPersistence, ProjectSettingsFile,
                () => current,
                next,
                () => PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, next));
        }

        private static List<string> Merge(List<string> current, BuildSettingsWritePlan plan)
        {
            var result = new List<string>(current);

            foreach (var symbol in plan.RemoveDefineSymbols)
                result.RemoveAll(existing => string.Equals(existing, symbol, StringComparison.Ordinal));

            foreach (var symbol in plan.AddDefineSymbols)
            {
                if (!result.Contains(symbol))
                    result.Add(symbol);
            }

            return result;
        }

        /// <summary>
        /// Replaces the build scene list.
        /// </summary>
        /// <remarks>
        /// Every path is checked against the AssetDatabase before anything is written, because a
        /// scene entry pointing at nothing is accepted by Unity and only fails at build time.
        /// </remarks>
        internal static bool TryApplyScenes(
            IReadOnlyList<BuildSceneEntry> scenes,
            out BuildSettingsChangeResult result,
            out string error)
        {
            result = null;
            error = null;

            var entries = new EditorBuildSettingsScene[scenes.Count];
            for (var i = 0; i < scenes.Count; i++)
            {
                var guid = AssetDatabase.AssetPathToGUID(scenes[i].Path);
                if (string.IsNullOrEmpty(guid))
                {
                    error = $"Element {i} of 'scenes' does not name an imported scene asset: {scenes[i].Path}";
                    return false;
                }

                entries[i] = new EditorBuildSettingsScene(scenes[i].Path, scenes[i].Enabled);
            }

            var previous = DescribeScenes(EditorBuildSettings.scenes);
            var next = DescribeScenes(entries);

            result = new BuildSettingsChangeResult
            {
                Setting = "scenes",
                Persistence = ProjectPersistence,
                File = BuildSettingsFile,
                Previous = previous,
                Value = next,
            };

            if (previous == next)
            {
                result.Outcome = "unchanged";
                return true;
            }

            try
            {
                EditorBuildSettings.scenes = entries;
                result.Outcome = "applied";
            }
            catch (Exception ex)
            {
                result.Outcome = "failed";
                result.Error = ex.Message;
            }
            return true;
        }

        private static string DescribeScenes(EditorBuildSettingsScene[] scenes)
        {
            if (scenes == null) return "";
            var sb = new StringBuilder();
            for (var i = 0; i < scenes.Length; i++)
            {
                if (i > 0) sb.Append(";");
                sb.Append(scenes[i].path).Append(scenes[i].enabled ? "+" : "-");
            }
            return sb.ToString();
        }

        private static void Change(
            List<BuildSettingsChangeResult> results,
            string setting,
            string persistence,
            string file,
            Func<string> read,
            string value,
            Action write)
        {
            var result = new BuildSettingsChangeResult
            {
                Setting = setting,
                Persistence = persistence,
                File = file,
                Value = value,
            };
            results.Add(result);

            try { result.Previous = read(); }
            catch (Exception ex)
            {
                result.Outcome = "failed";
                result.Error = "The current value could not be read: " + ex.Message;
                return;
            }

            // Reported rather than skipped silently: a caller that set a value and got no
            // acknowledgement cannot tell a no-op apart from a dropped field.
            if (string.Equals(result.Previous, value, StringComparison.Ordinal))
            {
                result.Outcome = "unchanged";
                return;
            }

            try
            {
                write();
                result.Outcome = "applied";
            }
            catch (Exception ex)
            {
                result.Outcome = "failed";
                result.Error = ex.Message;
            }
        }

        private static string Bool(bool value) => value ? "true" : "false";

        /// <summary>Serializes change results as the API response array.</summary>
        internal static void AppendResults(StringBuilder sb, IReadOnlyList<BuildSettingsChangeResult> results)
        {
            sb.Append("[");
            for (var i = 0; i < results.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var result = results[i];
                sb.Append("{\"setting\":\"").Append(RestResponse.EscapeJson(result.Setting));
                sb.Append("\",\"outcome\":\"").Append(RestResponse.EscapeJson(result.Outcome));
                sb.Append("\",\"persistence\":\"").Append(RestResponse.EscapeJson(result.Persistence));
                sb.Append("\",\"file\":\"").Append(RestResponse.EscapeJson(result.File));
                sb.Append("\",\"previous\":").Append(RestResponse.FormatNullableString(Empty(result.Previous)));
                sb.Append(",\"value\":").Append(RestResponse.FormatNullableString(Empty(result.Value)));
                sb.Append(",\"error\":").Append(RestResponse.FormatNullableString(Empty(result.Error)));
                sb.Append("}");
            }
            sb.Append("]");
        }

        private static string Empty(string value) => string.IsNullOrEmpty(value) ? null : value;

        /// <summary>Whether any change failed, which is what makes the response a 207.</summary>
        internal static bool AnyFailed(IReadOnlyList<BuildSettingsChangeResult> results)
        {
            for (var i = 0; i < results.Count; i++)
                if (results[i].Outcome == "failed") return true;
            return false;
        }

        /// <summary>Whether any change was actually written.</summary>
        internal static bool AnyApplied(IReadOnlyList<BuildSettingsChangeResult> results)
        {
            for (var i = 0; i < results.Count; i++)
                if (results[i].Outcome == "applied") return true;
            return false;
        }
    }
}
