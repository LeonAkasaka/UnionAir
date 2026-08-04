using System;
using System.Collections.Generic;
using System.Globalization;
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

    internal sealed class UnionAirLocalCapabilityState
    {
        internal string Capability;
        internal bool Enabled;
    }

    /// <summary>Maintains the session working document and project-scoped local security decisions.</summary>
    internal static class UnionAirProjectSettings
    {
        private const string ApprovedPrefix = "UnionAir.Project.Approved.";
        private const string DeniedPrefix = "UnionAir.Project.Denied.";
        private const string SessionSnapshotKey = "UnionAir.ProjectSettings.Snapshot";
        private const string CustomHandlersCapability = "customHandlers";
        private const string PlayModeSceneChangesCapability = "playMode.allowSceneChanges";

        private static UnionAirProjectSettingsDocument _document;
        private static HashSet<string> _knownCustomCategories =
            new HashSet<string>(StringComparer.Ordinal);
        private static string _projectKey;
        private static bool _savePending;
        private static string _saveError;
        private static int _retryFailureCount;
        private static double _nextRetryTime;

        internal static UnionAirProjectSettingsState State { get; private set; } =
            UnionAirProjectSettingsState.Missing;

        internal static string Error { get; private set; }
        internal static string SettingsPath => UnionAirProjectPaths.SettingsPath;
        internal static bool IsProjectControlled => State != UnionAirProjectSettingsState.Missing;
        internal static bool IsValid => State == UnionAirProjectSettingsState.Valid;
        internal static bool SavePending => _savePending;
        internal static string SaveError => _saveError;
        internal static UnionAirProjectSettingsDocument Document => _document;

        internal static void Initialize()
        {
            _projectKey = ProjectScopeKey(UnionAirProjectPaths.ProjectRoot);
            _knownCustomCategories = DiscoverCustomCategoryIds();

            var restored = !UnionAirSession.IsNewEditorSession && TryRestoreSessionSnapshot();
            if (!restored)
            {
                if (UnionAirSession.IsNewEditorSession)
                    SessionState.EraseString(SessionSnapshotKey);
                LoadFromDisk();
                SaveSessionSnapshot();
            }

            if (State == UnionAirProjectSettingsState.Invalid)
                Debug.LogError($"[UnionAir] Invalid .unionair/settings.json: {Error}");

            if (_savePending)
                ScheduleRetry();
        }

        internal static void FlushPendingWrite()
        {
            if (_savePending)
                TryPersistWorkingDocument();
            SaveSessionSnapshot();
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

        internal static void SetPort(int port)
        {
            if (!UnionAirPortAllocator.IsValidConfiguredPort(port))
                throw new ArgumentOutOfRangeException(
                    nameof(port), port, "Port must be 0 (Automatic) or between 1 and 65535.");
            ApplyDocumentChange(delegate
            {
                UnionAirProjectSettingsDocumentModel.SetPort(_document, port);
            });
        }

        internal static void SetAutoStart(bool autoStart)
            => ApplyDocumentChange(delegate
            {
                UnionAirProjectSettingsDocumentModel.SetAutoStart(_document, autoStart);
            });

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
            var identifier = CategoryIdentifier(categoryKey);
            if (string.IsNullOrEmpty(identifier)) return;

            ApplyDocumentChange(delegate
            {
                var capability = CategoryCapability(identifier);
                if (enabled)
                {
                    if (identifier.StartsWith("custom:", StringComparison.Ordinal))
                    {
                        ApproveCapability(CustomHandlersCapability);
                    }
                    ApproveCapability(capability);
                }
                UnionAirProjectSettingsDocumentModel.SetCategoryEnabled(
                    _document, identifier, enabled);
            });
        }

        internal static bool CustomHandlersEnabled
            => State == UnionAirProjectSettingsState.Valid &&
               _document.CustomHandlers &&
               IsCapabilityEnabled(CustomHandlersCapability);

        internal static void SetCustomHandlersEnabled(bool enabled)
        {
            ApplyDocumentChange(delegate
            {
                UnionAirProjectSettingsDocumentModel.SetCustomHandlersEnabled(
                    _document, enabled);
                if (enabled)
                    ApproveCapability(CustomHandlersCapability);
            });
        }

        internal static bool AllowPlayModeSceneChanges
            => State == UnionAirProjectSettingsState.Valid &&
               _document.AllowSceneChanges &&
               IsCapabilityEnabled(PlayModeSceneChangesCapability);

        internal static void SetAllowPlayModeSceneChanges(bool enabled)
        {
            ApplyDocumentChange(delegate
            {
                UnionAirProjectSettingsDocumentModel.SetAllowSceneChanges(
                    _document, enabled);
                if (enabled)
                    ApproveCapability(PlayModeSceneChangesCapability);
            });
        }

        internal static string[] PendingCapabilities()
        {
            if (State != UnionAirProjectSettingsState.Valid) return new string[0];
            var approved = LoadSet(ApprovedKey);
            var denied = LoadSet(DeniedKey);
            return UnionAirProjectSettingsDecision.Pending(
                RequestedCapabilities(_document), approved, denied);
        }

        internal static UnionAirLocalCapabilityState[] LocalCapabilities()
        {
            if (State != UnionAirProjectSettingsState.Valid)
                return new UnionAirLocalCapabilityState[0];

            var approved = LoadSet(ApprovedKey);
            var denied = LoadSet(DeniedKey);
            var capabilities = new List<string>(RequestedCapabilities(_document));
            capabilities.Sort(StringComparer.Ordinal);
            var result = new UnionAirLocalCapabilityState[capabilities.Count];
            for (var i = 0; i < capabilities.Count; i++)
            {
                var capability = capabilities[i];
                result[i] = new UnionAirLocalCapabilityState
                {
                    Capability = capability,
                    Enabled = UnionAirProjectSettingsDecision.IsEffective(
                        true, approved, denied, capability)
                };
            }
            return result;
        }

        internal static void SetLocalCapabilityEnabled(string capability, bool enabled)
        {
            if (State != UnionAirProjectSettingsState.Valid ||
                !ContainsCapability(RequestedCapabilities(_document), capability)) return;

            if (enabled) ApproveCapability(capability);
            else
            {
                var denied = LoadSet(DeniedKey);
                denied.Add(capability);
                SaveSet(DeniedKey, denied);
            }
            UnionAirRouteRegistry.Refresh();
        }

        internal static void ApprovePendingCapabilities()
        {
            foreach (var capability in PendingCapabilities())
                ApproveCapability(capability);
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

        private static void LoadFromDisk()
        {
            _document = null;
            Error = null;
            _savePending = false;
            _saveError = null;
            _retryFailureCount = 0;

            State = UnionAirProjectSettingsLoader.Load(
                SettingsPath,
                _knownCustomCategories,
                out _document,
                out var loadError);
            Error = loadError;
        }

        private static void ApplyDocumentChange(Action change)
        {
            EnsureWorkingDocument();
            change();
            State = UnionAirProjectSettingsState.Valid;
            Error = null;
            _savePending = true;
            SaveSessionSnapshot();
            TryPersistWorkingDocument();
            UnionAirRouteRegistry.Refresh();
        }

        private static void EnsureWorkingDocument()
        {
            if (State == UnionAirProjectSettingsState.Valid && _document != null)
                return;

            var previousState = State;
            _document = UnionAirProjectSettingsDocumentModel.BeginChange(
                previousState,
                _document,
                CaptureLegacyEffectiveSettings);
            if (previousState == UnionAirProjectSettingsState.Missing)
            {
                var approved = LoadSet(ApprovedKey);
                var denied = LoadSet(DeniedKey);
                foreach (var capability in RequestedCapabilities(_document))
                    UnionAirProjectSettingsDecision.Approve(
                        capability, approved, denied);
                SaveSet(ApprovedKey, approved);
                SaveSet(DeniedKey, denied);
            }
        }

        private static UnionAirProjectSettingsDocument CaptureLegacyEffectiveSettings()
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
            if (!document.CustomHandlers)
                document.EnabledCategories.RemoveWhere(
                    category => category.StartsWith("custom:", StringComparison.Ordinal));
            return document;
        }

        private static bool TryPersistWorkingDocument()
        {
            if (State != UnionAirProjectSettingsState.Valid || _document == null)
                return true;

            string persistenceError;
            if (UnionAirProjectSettingsSavePolicy.TryWrite(delegate
            {
                string ignoreError;
                if (!UnionAirEndpointDiscovery.TryEnsureIgnore(
                        UnionAirProjectPaths.ProjectRoot,
                        out ignoreError))
                    throw new IOException(ignoreError);

                UnionAirEndpointDiscovery.WriteAtomicText(
                    SettingsPath,
                    UnionAirProjectSettingsParser.Serialize(_document));
            }, out persistenceError))
            {
                var recovered = _savePending && !string.IsNullOrEmpty(_saveError);
                _savePending = false;
                _saveError = null;
                _retryFailureCount = 0;
                _nextRetryTime = 0;
                EditorApplication.update -= ProcessSaveRetry;
                SaveSessionSnapshot();
                if (recovered)
                    Debug.Log("[UnionAir] Saved .unionair/settings.json after a previous I/O failure.");
                return true;
            }

            var message = $"Failed to save .unionair/settings.json: {persistenceError}";
            if (!string.Equals(_saveError, message, StringComparison.Ordinal))
                Debug.LogWarning("[UnionAir] " + message);
            _savePending = true;
            _saveError = message;
            SaveSessionSnapshot();
            ScheduleRetry();
            return false;
        }

        private static void ScheduleRetry()
        {
            _nextRetryTime = UnionAirProjectSettingsSavePolicy.NextRetryTime(
                EditorApplication.timeSinceStartup,
                _retryFailureCount);
            _retryFailureCount++;
            EditorApplication.update -= ProcessSaveRetry;
            EditorApplication.update += ProcessSaveRetry;
        }

        private static void ProcessSaveRetry()
        {
            if (!_savePending)
            {
                EditorApplication.update -= ProcessSaveRetry;
                return;
            }
            if (EditorApplication.timeSinceStartup < _nextRetryTime)
                return;
            TryPersistWorkingDocument();
        }

        private static void SaveSessionSnapshot()
        {
            try
            {
                SessionState.SetString(
                    SessionSnapshotKey,
                    UnionAirProjectSettingsSnapshotCodec.Encode(
                        State, _document, Error, _savePending, _saveError));
            }
            catch
            {
                // SessionState can be unavailable while the Editor is tearing down.
            }
        }

        private static bool TryRestoreSessionSnapshot()
        {
            var raw = SessionState.GetString(SessionSnapshotKey, "");
            if (string.IsNullOrEmpty(raw)) return false;

            UnionAirProjectSettingsState state;
            UnionAirProjectSettingsDocument document;
            string loadError;
            bool savePending;
            string saveError;
            if (!UnionAirProjectSettingsSnapshotCodec.TryDecode(
                    raw,
                    _knownCustomCategories,
                    out state,
                    out document,
                    out loadError,
                    out savePending,
                    out saveError)) return false;

            State = state;
            _document = document;
            Error = loadError;
            _savePending = savePending;
            _saveError = saveError;
            _retryFailureCount = 0;
            return true;
        }

        private static IEnumerable<string> RequestedCapabilities(
            UnionAirProjectSettingsDocument document)
        {
            foreach (var category in document.EnabledCategories)
                yield return CategoryCapability(category);
            if (document.CustomHandlers) yield return CustomHandlersCapability;
            if (document.AllowSceneChanges) yield return PlayModeSceneChangesCapability;
        }

        private static bool ContainsCapability(IEnumerable<string> values, string expected)
        {
            foreach (var value in values)
                if (string.Equals(value, expected, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsCapabilityEnabled(string capability)
        {
            var approved = LoadSet(ApprovedKey);
            var denied = LoadSet(DeniedKey);
            return UnionAirProjectSettingsDecision.IsEffective(
                true, approved, denied, capability);
        }

        private static void ApproveCapability(string capability)
        {
            var approved = LoadSet(ApprovedKey);
            var denied = LoadSet(DeniedKey);
            UnionAirProjectSettingsDecision.Approve(
                capability, approved, denied);
            SaveSet(ApprovedKey, approved);
            SaveSet(DeniedKey, denied);
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

    internal static class UnionAirProjectSettingsDocumentModel
    {
        internal static UnionAirProjectSettingsDocument BeginChange(
            UnionAirProjectSettingsState state,
            UnionAirProjectSettingsDocument document,
            Func<UnionAirProjectSettingsDocument> captureLegacy)
        {
            if (state == UnionAirProjectSettingsState.Valid && document != null)
                return document;
            if (state == UnionAirProjectSettingsState.Missing)
                return captureLegacy();
            return new UnionAirProjectSettingsDocument
            {
                Port = 0,
                AutoStart = false,
                CustomHandlers = false,
                AllowSceneChanges = false
            };
        }

        internal static void SetPort(
            UnionAirProjectSettingsDocument document,
            int port)
        {
            if (!UnionAirPortAllocator.IsValidConfiguredPort(port))
                throw new ArgumentOutOfRangeException(
                    nameof(port), port, "Port must be 0 (Automatic) or between 1 and 65535.");
            document.Port = port;
        }

        internal static void SetAutoStart(
            UnionAirProjectSettingsDocument document,
            bool autoStart)
            => document.AutoStart = autoStart;

        internal static void SetCategoryEnabled(
            UnionAirProjectSettingsDocument document,
            string identifier,
            bool enabled)
        {
            if (enabled)
            {
                if (identifier.StartsWith("custom:", StringComparison.Ordinal))
                    document.CustomHandlers = true;
                document.EnabledCategories.Add(identifier);
            }
            else
            {
                document.EnabledCategories.Remove(identifier);
            }
        }

        internal static void SetCustomHandlersEnabled(
            UnionAirProjectSettingsDocument document,
            bool enabled)
        {
            document.CustomHandlers = enabled;
            if (!enabled)
                document.EnabledCategories.RemoveWhere(
                    category => category.StartsWith("custom:", StringComparison.Ordinal));
        }

        internal static void SetAllowSceneChanges(
            UnionAirProjectSettingsDocument document,
            bool enabled)
            => document.AllowSceneChanges = enabled;
    }

    internal static class UnionAirProjectSettingsSavePolicy
    {
        private static readonly double[] RetryDelaysSeconds = { 1, 2, 5, 10, 30 };

        internal static bool TryWrite(Action writer, out string error)
        {
            try
            {
                writer();
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static double NextRetryTime(double now, int previousFailureCount)
        {
            var index = Math.Min(
                Math.Max(previousFailureCount, 0),
                RetryDelaysSeconds.Length - 1);
            return now + RetryDelaysSeconds[index];
        }
    }

    internal static class UnionAirProjectSettingsSnapshotCodec
    {
        internal static string Encode(
            UnionAirProjectSettingsState state,
            UnionAirProjectSettingsDocument document,
            string loadError,
            bool savePending,
            string saveError)
        {
            var documentJson = document == null
                ? ""
                : UnionAirProjectSettingsParser.Serialize(document);
            return ((int)state).ToString(CultureInfo.InvariantCulture) + "\n" +
                   (savePending ? "1" : "0") + "\n" +
                   ToBase64(loadError) + "\n" +
                   ToBase64(saveError) + "\n" +
                   ToBase64(documentJson);
        }

        internal static bool TryDecode(
            string encoded,
            ISet<string> knownCustomCategories,
            out UnionAirProjectSettingsState state,
            out UnionAirProjectSettingsDocument document,
            out string loadError,
            out bool savePending,
            out string saveError)
        {
            state = UnionAirProjectSettingsState.Missing;
            document = null;
            loadError = null;
            savePending = false;
            saveError = null;

            var parts = encoded.Split(new[] { '\n' }, 5);
            int stateValue;
            if (parts.Length != 5 ||
                !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out stateValue) ||
                stateValue < (int)UnionAirProjectSettingsState.Missing ||
                stateValue > (int)UnionAirProjectSettingsState.Invalid ||
                (parts[1] != "0" && parts[1] != "1")) return false;

            string documentJson;
            if (!TryFromBase64(parts[2], out loadError) ||
                !TryFromBase64(parts[3], out saveError) ||
                !TryFromBase64(parts[4], out documentJson)) return false;

            state = (UnionAirProjectSettingsState)stateValue;
            savePending = parts[1] == "1";
            if (!string.IsNullOrEmpty(documentJson))
            {
                string parseError;
                if (!UnionAirProjectSettingsParser.TryParse(
                        documentJson,
                        knownCustomCategories,
                        out document,
                        out parseError)) return false;
            }
            return state != UnionAirProjectSettingsState.Valid || document != null;
        }

        private static string ToBase64(string value)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));

        private static bool TryFromBase64(string value, out string decoded)
        {
            decoded = null;
            try
            {
                var text = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                decoded = text.Length == 0 ? null : text;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class UnionAirProjectSettingsDecision
    {
        internal static void Approve(
            string capability,
            ISet<string> approved,
            ISet<string> denied)
        {
            approved.Add(capability);
            denied.Remove(capability);
        }

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
