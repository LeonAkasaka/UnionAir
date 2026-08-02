using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>One entry in a requested build scene list.</summary>
    internal sealed class BuildSceneEntry
    {
        internal string Path;
        internal bool Enabled;
    }

    /// <summary>
    /// Settings a request asked to change, with a flag per field so "absent" and "set to the
    /// current value" stay distinguishable.
    /// </summary>
    internal sealed class BuildSettingsWritePlan
    {
        internal string NamedBuildTargetName = "";

        internal bool HasScriptingBackend;
        internal ScriptingImplementation ScriptingBackend;

        internal bool HasApiCompatibilityLevel;
        internal ApiCompatibilityLevel ApiCompatibilityLevel;

        internal bool HasManagedStrippingLevel;
        internal ManagedStrippingLevel ManagedStrippingLevel;

        internal bool HasIl2CppCompilerConfiguration;
        internal Il2CppCompilerConfiguration Il2CppCompilerConfiguration;

        /// <summary>Replaces the whole define symbol list when set.</summary>
        internal bool HasDefineSymbols;
        internal List<string> DefineSymbols = new List<string>();

        internal List<string> AddDefineSymbols = new List<string>();
        internal List<string> RemoveDefineSymbols = new List<string>();

        internal bool HasDevelopment;
        internal bool Development;
        internal bool HasAllowDebugging;
        internal bool AllowDebugging;
        internal bool HasConnectProfiler;
        internal bool ConnectProfiler;
        internal bool HasDeepProfiling;
        internal bool DeepProfiling;
        internal bool HasWaitForManagedDebugger;
        internal bool WaitForManagedDebugger;

        /// <summary>Whether the request asked for anything at all.</summary>
        internal bool IsEmpty =>
            !HasScriptingBackend && !HasApiCompatibilityLevel && !HasManagedStrippingLevel &&
            !HasIl2CppCompilerConfiguration && !HasDefineSymbols &&
            AddDefineSymbols.Count == 0 && RemoveDefineSymbols.Count == 0 &&
            !HasDevelopment && !HasAllowDebugging && !HasConnectProfiler &&
            !HasDeepProfiling && !HasWaitForManagedDebugger;

        /// <summary>
        /// Whether applying this plan is expected to trigger a script compilation.
        /// </summary>
        /// <remarks>
        /// Only the settings the compiler actually reads. Build flags such as
        /// <c>development</c> are read when a build runs, not when scripts compile.
        /// </remarks>
        internal bool TriggersCompilation =>
            HasScriptingBackend || HasApiCompatibilityLevel || HasDefineSymbols ||
            AddDefineSymbols.Count > 0 || RemoveDefineSymbols.Count > 0;
    }

    /// <summary>
    /// Parses and validates build settings write requests.
    /// </summary>
    /// <remarks>
    /// Every value is validated before anything is applied. A request naming one bad enum value
    /// changes nothing at all, which is the only partial-failure outcome that needs no explanation.
    /// </remarks>
    internal static class BuildSettingsWriteParser
    {
        /// <summary>
        /// Parses a <c>PATCH /api/build/settings</c> body.
        /// </summary>
        internal static bool TryParseSettings(string body, out BuildSettingsWritePlan plan, out string error)
        {
            plan = new BuildSettingsWritePlan();
            error = null;

            plan.NamedBuildTargetName = RequestBodyReader.GetString(body, "namedBuildTarget") ?? "";

            if (!TryReadEnum(body, "scriptingBackend", ref plan.HasScriptingBackend, ref plan.ScriptingBackend, out error) ||
                !TryReadEnum(body, "apiCompatibilityLevel", ref plan.HasApiCompatibilityLevel, ref plan.ApiCompatibilityLevel, out error) ||
                !TryReadEnum(body, "managedStrippingLevel", ref plan.HasManagedStrippingLevel, ref plan.ManagedStrippingLevel, out error) ||
                !TryReadEnum(body, "il2CppCompilerConfiguration", ref plan.HasIl2CppCompilerConfiguration, ref plan.Il2CppCompilerConfiguration, out error))
                return false;

            if (!TryReadSymbols(body, "defineSymbols", out plan.HasDefineSymbols, out plan.DefineSymbols, out error) ||
                !TryReadSymbols(body, "addDefineSymbols", out _, out plan.AddDefineSymbols, out error) ||
                !TryReadSymbols(body, "removeDefineSymbols", out _, out plan.RemoveDefineSymbols, out error))
                return false;

            if (plan.HasDefineSymbols &&
                (plan.AddDefineSymbols.Count > 0 || plan.RemoveDefineSymbols.Count > 0))
            {
                error = "Body field 'defineSymbols' replaces the whole list and cannot be combined " +
                        "with 'addDefineSymbols' or 'removeDefineSymbols'.";
                return false;
            }

            ReadFlag(body, "development", ref plan.HasDevelopment, ref plan.Development);
            ReadFlag(body, "allowDebugging", ref plan.HasAllowDebugging, ref plan.AllowDebugging);
            ReadFlag(body, "connectProfiler", ref plan.HasConnectProfiler, ref plan.ConnectProfiler);
            ReadFlag(body, "buildWithDeepProfilingSupport", ref plan.HasDeepProfiling, ref plan.DeepProfiling);
            ReadFlag(body, "waitForManagedDebugger", ref plan.HasWaitForManagedDebugger, ref plan.WaitForManagedDebugger);

            if (plan.IsEmpty)
            {
                error = "The request changed nothing. Supply at least one of: scriptingBackend, " +
                        "apiCompatibilityLevel, managedStrippingLevel, il2CppCompilerConfiguration, " +
                        "defineSymbols, addDefineSymbols, removeDefineSymbols, development, " +
                        "allowDebugging, connectProfiler, buildWithDeepProfilingSupport, waitForManagedDebugger.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Parses a <c>POST /api/build/scenes</c> body.
        /// </summary>
        /// <remarks>
        /// The list is a replacement rather than a patch. Order decides the build index every
        /// enabled scene gets, so a partial update would have to express intent about position
        /// as well as membership, and any shorthand for that would be guessing.
        /// </remarks>
        internal static bool TryParseScenes(string body, out List<BuildSceneEntry> scenes, out string error)
        {
            scenes = new List<BuildSceneEntry>();
            error = null;

            List<string> elements;
            bool present;
            if (!RequestBodyReader.TryGetArrayElements(body, "scenes", out elements, out present, out error))
            {
                error = "Body field 'scenes' must be a JSON array. " + (error ?? "");
                return false;
            }

            if (!present)
            {
                error = "Body field 'scenes' is required. Supply the complete ordered list; " +
                        "it replaces the current one.";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                var path = RequestBodyReader.GetString(element, "path");

                // A bare string is accepted so the common case -- a list of paths, all enabled --
                // does not need an object per entry.
                if (string.IsNullOrEmpty(path) && element != null)
                {
                    var trimmed = element.Trim();
                    if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
                        path = trimmed.Substring(1, trimmed.Length - 2);
                }

                if (string.IsNullOrEmpty(path))
                {
                    error = $"Element {i} of 'scenes' must be a scene path string or an object with a 'path' field.";
                    return false;
                }

                path = path.Replace('\\', '/');
                if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Element {i} of 'scenes' must reference a .unity scene asset: {path}";
                    return false;
                }

                if (!seen.Add(path))
                {
                    error = $"Element {i} of 'scenes' repeats '{path}'. Unity assigns one build index per scene.";
                    return false;
                }

                var enabled = RequestBodyReader.GetBool(element, "enabled") ?? true;
                scenes.Add(new BuildSceneEntry { Path = path, Enabled = enabled });
            }

            return true;
        }

        /// <summary>
        /// Whether a string is usable as a scripting define symbol.
        /// </summary>
        /// <remarks>
        /// Unity stores whatever it is given and only fails later, at compile time, with an error
        /// that does not mention the setting. Rejecting here means the caller learns which symbol
        /// was wrong instead of why an unrelated assembly stopped compiling.
        /// </remarks>
        internal static bool IsValidDefineSymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return false;
            if (!char.IsLetter(symbol[0]) && symbol[0] != '_') return false;

            foreach (var ch in symbol)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '_')
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Lists the usable names of a Unity enum, skipping members Unity has deprecated.
        /// </summary>
        internal static List<string> EnumNames<T>() where T : struct
        {
            var names = new List<string>();
            foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral || Attribute.IsDefined(field, typeof(ObsoleteAttribute)))
                    continue;
                names.Add(field.Name);
            }
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        private static bool TryReadEnum<T>(
            string body, string field, ref bool has, ref T value, out string error) where T : struct
        {
            error = null;
            var raw = RequestBodyReader.GetString(body, field);
            if (string.IsNullOrEmpty(raw))
                return true;

            foreach (var name in EnumNames<T>())
            {
                if (!string.Equals(name, raw, StringComparison.OrdinalIgnoreCase))
                    continue;

                value = (T)Enum.Parse(typeof(T), name);
                has = true;
                return true;
            }

            error = $"Body field '{field}' must be one of: {string.Join(", ", EnumNames<T>().ToArray())}.";
            return false;
        }

        private static bool TryReadSymbols(
            string body, string field, out bool present, out List<string> symbols, out string error)
        {
            symbols = new List<string>();
            error = null;
            present = RequestBodyReader.HasTopLevelField(body, field);
            if (!present) return true;

            string[] values;
            if (!RequestBodyReader.TryGetStringArray(body, field, out values))
            {
                error = $"Body field '{field}' must be an array of strings.";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                var symbol = (value ?? "").Trim();
                if (!IsValidDefineSymbol(symbol))
                {
                    error = $"Body field '{field}' contains an invalid define symbol '{value}'. " +
                            "A symbol must start with a letter or underscore and contain only letters, digits, and underscores.";
                    return false;
                }
                if (seen.Add(symbol))
                    symbols.Add(symbol);
            }
            return true;
        }

        private static void ReadFlag(string body, string field, ref bool has, ref bool value)
        {
            var parsed = RequestBodyReader.GetBool(body, field);
            if (!parsed.HasValue) return;
            has = true;
            value = parsed.Value;
        }
    }
}
