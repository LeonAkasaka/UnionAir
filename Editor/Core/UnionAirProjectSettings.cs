using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal enum UnionAirProjectSettingsState
    {
        Missing,
        Valid,
        Invalid
    }

    /// <summary>Loads shared project settings and applies project-scoped local security decisions.</summary>
    internal static class UnionAirProjectSettings
    {
        private const string ApprovedPrefix = "UnionAir.Project.Approved.";
        private const string DeniedPrefix = "UnionAir.Project.Denied.";
        private const string CustomHandlersCapability = "customHandlers";
        private const string PlayModeSceneChangesCapability = "playMode.allowSceneChanges";

        private static UnionAirProjectSettingsDocument _document;
        private static HashSet<string> _knownCustomCategories = new HashSet<string>(StringComparer.Ordinal);
        private static string _projectKey;

        internal static UnionAirProjectSettingsState State { get; private set; } =
            UnionAirProjectSettingsState.Missing;

        internal static string Error { get; private set; }
        internal static string SettingsPath => UnionAirProjectPaths.SettingsPath;
        internal static bool IsProjectControlled => State != UnionAirProjectSettingsState.Missing;
        internal static bool IsValid => State == UnionAirProjectSettingsState.Valid;
        internal static UnionAirProjectSettingsDocument Document => _document;

        internal static void Initialize()
            => Reload(false);

        internal static void Reload(bool refreshRoutes)
        {
            _projectKey = ProjectScopeKey(UnionAirProjectPaths.ProjectRoot);
            _knownCustomCategories = DiscoverCustomCategoryIds();
            _document = null;
            Error = null;

            State = UnionAirProjectSettingsLoader.Load(
                SettingsPath,
                _knownCustomCategories,
                out _document,
                out var loadError);
            Error = loadError;

            if (State == UnionAirProjectSettingsState.Invalid)
                Debug.LogError($"[UnionAir] Invalid .unionair/settings.json: {Error}");

            if (refreshRoutes)
                UnionAirRouteRegistry.Refresh();
        }

        internal static int ResolvePort(int legacyValue)
        {
            if (State == UnionAirProjectSettingsState.Valid) return _document.Port;
            if (State == UnionAirProjectSettingsState.Invalid) return 0;
            return legacyValue;
        }

        internal static bool ResolveAutoStart(bool legacyValue)
        {
            if (State == UnionAirProjectSettingsState.Valid) return _document.AutoStart;
            if (State == UnionAirProjectSettingsState.Invalid) return false;
            return legacyValue;
        }

        internal static bool IsCategoryEnabled(string categoryKey)
        {
            if (State != UnionAirProjectSettingsState.Valid) return false;
            var identifier = CategoryIdentifier(categoryKey);
            if (string.IsNullOrEmpty(identifier) || !_document.EnabledCategories.Contains(identifier))
                return false;
            return IsCapabilityEnabled(CategoryCapability(identifier));
        }

        internal static void SetCategoryEnabled(string categoryKey, bool enabled)
        {
            if (State != UnionAirProjectSettingsState.Valid) return;
            var identifier = CategoryIdentifier(categoryKey);
            if (string.IsNullOrEmpty(identifier) || !_document.EnabledCategories.Contains(identifier))
                return;
            SetLocallyDenied(CategoryCapability(identifier), !enabled);
        }

        internal static bool CustomHandlersEnabled
            => State == UnionAirProjectSettingsState.Valid &&
               _document.CustomHandlers &&
               IsCapabilityEnabled(CustomHandlersCapability);

        internal static void SetCustomHandlersEnabled(bool enabled)
        {
            if (State == UnionAirProjectSettingsState.Valid && _document.CustomHandlers)
                SetLocallyDenied(CustomHandlersCapability, !enabled);
        }

        internal static bool AllowPlayModeSceneChanges
            => State == UnionAirProjectSettingsState.Valid &&
               _document.AllowSceneChanges &&
               IsCapabilityEnabled(PlayModeSceneChangesCapability);

        internal static void SetAllowPlayModeSceneChanges(bool enabled)
        {
            if (State == UnionAirProjectSettingsState.Valid && _document.AllowSceneChanges)
                SetLocallyDenied(PlayModeSceneChangesCapability, !enabled);
        }

        internal static string[] PendingCapabilities()
        {
            if (State != UnionAirProjectSettingsState.Valid) return new string[0];
            var approved = LoadSet(ApprovedKey);
            var denied = LoadSet(DeniedKey);
            return UnionAirProjectSettingsDecision.Pending(
                RequestedCapabilities(), approved, denied);
        }

        internal static void ApprovePendingCapabilities()
        {
            var approved = LoadSet(ApprovedKey);
            var denied = LoadSet(DeniedKey);
            foreach (var capability in PendingCapabilities())
            {
                approved.Add(capability);
                denied.Remove(capability);
            }
            SaveSet(ApprovedKey, approved);
            SaveSet(DeniedKey, denied);
            UnionAirRouteRegistry.Refresh();
        }

        internal static void RefusePendingCapabilities()
        {
            var denied = LoadSet(DeniedKey);
            foreach (var capability in PendingCapabilities())
                denied.Add(capability);
            SaveSet(DeniedKey, denied);
            UnionAirRouteRegistry.Refresh();
        }

        internal static void ForgetApprovals()
        {
            EditorPrefs.DeleteKey(ApprovedKey);
            UnionAirRouteRegistry.Refresh();
        }

        internal static bool TrySaveEffective(out string error)
        {
            error = null;
            try
            {
                var document = new UnionAirProjectSettingsDocument
                {
                    Port = UnionAirSettings.Port,
                    AutoStart = UnionAirSettings.AutoStart,
                    CustomHandlers = UnionAirSettings.CustomHandlersEnabled,
                    AllowSceneChanges = UnionAirSettings.AllowPlayModeSceneChanges
                };
                foreach (var category in UnionAirRouteRegistry.Categories)
                {
                    if (!category.CanDisable || !category.Enabled) continue;
                    document.EnabledCategories.Add(
                        category.Source == UnionAirRouteSource.Custom
                            ? "custom:" + category.Id
                            : category.Id);
                }

                if (!UnionAirEndpointDiscovery.TryEnsureIgnore(
                        UnionAirProjectPaths.ProjectRoot,
                        out error)) return false;
                UnionAirEndpointDiscovery.WriteAtomicText(
                    SettingsPath,
                    UnionAirProjectSettingsParser.Serialize(document));
                Reload(true);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to save .unionair/settings.json: {ex.Message}";
                return false;
            }
        }

        private static IEnumerable<string> RequestedCapabilities()
        {
            foreach (var category in _document.EnabledCategories)
                yield return CategoryCapability(category);
            if (_document.CustomHandlers) yield return CustomHandlersCapability;
            if (_document.AllowSceneChanges) yield return PlayModeSceneChangesCapability;
        }

        private static bool IsCapabilityEnabled(string capability)
        {
            var approved = LoadSet(ApprovedKey);
            var denied = LoadSet(DeniedKey);
            return UnionAirProjectSettingsDecision.IsEffective(true, approved, denied, capability);
        }

        private static void SetLocallyDenied(string capability, bool denied)
        {
            var values = LoadSet(DeniedKey);
            if (denied) values.Add(capability);
            else values.Remove(capability);
            SaveSet(DeniedKey, values);
        }

        private static string CategoryIdentifier(string categoryKey)
        {
            const string builtin = "Builtin:";
            const string custom = "Custom:";
            if (categoryKey.StartsWith(builtin, StringComparison.Ordinal))
                return categoryKey.Substring(builtin.Length);
            if (categoryKey.StartsWith(custom, StringComparison.Ordinal))
                return "custom:" + categoryKey.Substring(custom.Length);
            return null;
        }

        private static string CategoryCapability(string identifier)
            => "category:" + identifier;

        private static string ApprovedKey => ApprovedPrefix + _projectKey;
        private static string DeniedKey => DeniedPrefix + _projectKey;

        private static HashSet<string> LoadSet(string key)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            var raw = EditorPrefs.GetString(key, "");
            foreach (var value in raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                result.Add(value);
            return result;
        }

        private static void SaveSet(string key, HashSet<string> values)
        {
            var ordered = new List<string>(values);
            ordered.Sort(StringComparer.Ordinal);
            if (ordered.Count == 0) EditorPrefs.DeleteKey(key);
            else EditorPrefs.SetString(key, string.Join("\n", ordered.ToArray()));
        }

        internal static string ProjectScopeKey(string projectRoot)
        {
            var normalized = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Path.DirectorySeparatorChar == '\\')
                normalized = normalized.ToUpperInvariant();
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var value in bytes) sb.Append(value.ToString("x2"));
                return sb.ToString();
            }
        }

        private static HashSet<string> DiscoverCustomCategoryIds()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var isBuiltInAssembly = assembly == typeof(UnionAirProjectSettings).Assembly;
                try
                {
                    AddCategoryAttributes(
                        result,
                        assembly.GetCustomAttributes(typeof(UnionAirCategoryAttribute), false));
                    isBuiltInAssembly = isBuiltInAssembly ||
                                        assembly.IsDefined(
                                            typeof(UnionAirBuiltinAssemblyAttribute), false);
                }
                catch
                {
                    // An unloadable third-party assembly cannot contribute a usable category.
                }
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                foreach (var type in types)
                {
                    if (type == null) continue;
                    try
                    {
                        AddCategoryAttributes(
                            result,
                            type.GetCustomAttributes(typeof(UnionAirCategoryAttribute), false));
                        if (!isBuiltInAssembly)
                            AddEndpointCategories(result, type);
                    }
                    catch
                    {
                        // Ignore types whose metadata cannot be inspected.
                    }
                }
            }
            return result;
        }

        private static void AddEndpointCategories(HashSet<string> result, Type type)
        {
            var methods = type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            foreach (UnionAirEndpointAttribute endpoint in method.GetCustomAttributes(
                         typeof(UnionAirEndpointAttribute), false))
            {
                if (string.IsNullOrEmpty(endpoint.Category) ||
                    IsBuiltInCategory(endpoint.Category)) continue;
                result.Add(endpoint.Category);
            }
        }

        private static void AddCategoryAttributes(HashSet<string> result, object[] attributes)
        {
            foreach (UnionAirCategoryAttribute attribute in attributes)
            {
                if (string.IsNullOrEmpty(attribute.Id) || IsBuiltInCategory(attribute.Id)) continue;
                result.Add(attribute.Id);
            }
        }

        private static bool IsBuiltInCategory(string id)
            => id == UnionAirEndpointCategories.Read ||
               id == UnionAirEndpointCategories.SceneWrite ||
               id == UnionAirEndpointCategories.AssetWrite ||
               id == UnionAirEndpointCategories.PlayMode ||
               id == UnionAirEndpointCategories.EditorActions ||
               id == UnionAirEndpointCategories.TestRunner ||
               id == UnionAirEndpointCategories.Profiling ||
               id == UnionAirEndpointCategories.Build;
    }

    internal static class UnionAirProjectSettingsDecision
    {
        internal static bool IsEffective(
            bool requested,
            ISet<string> approved,
            ISet<string> denied,
            string capability)
            => requested && approved.Contains(capability) && !denied.Contains(capability);

        internal static string[] Pending(
            IEnumerable<string> requested,
            ISet<string> approved,
            ISet<string> denied)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var capability in requested)
                if (!approved.Contains(capability) && !denied.Contains(capability))
                    result.Add(capability);
            var ordered = new List<string>(result);
            ordered.Sort(StringComparer.Ordinal);
            return ordered.ToArray();
        }
    }
}
