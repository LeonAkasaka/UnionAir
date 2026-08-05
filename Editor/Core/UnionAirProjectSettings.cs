using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

    /// <summary>Maintains and persists the project settings working document.</summary>
    internal static class UnionAirProjectSettings
    {
        private const string SessionSnapshotKey = "UnionAir.ProjectSettings.Snapshot";

        private static UnionAirProjectSettingsDocument _document;
        private static HashSet<string> _knownCustomCategories =
            new HashSet<string>(StringComparer.Ordinal);
        private static bool _savePending;
        private static string _saveError;
        private static int _retryFailureCount;
        private static double _nextRetryTime;

        internal static UnionAirProjectSettingsState State { get; private set; } =
            UnionAirProjectSettingsState.Missing;

        internal static string Error { get; private set; }
        internal static string SettingsPath => UnionAirProjectPaths.SettingsPath;
        internal static bool SavePending => _savePending;
        internal static string SaveError => _saveError;

        internal static void Initialize()
        {
            _knownCustomCategories = UnionAirRouteRegistry.GetKnownCustomCategoryIds();

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

            UnionAirRouteRegistry.RefreshState();

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
            }, false);
        }

        internal static void SetAutoStart(bool autoStart)
            => ApplyDocumentChange(delegate
            {
                UnionAirProjectSettingsDocumentModel.SetAutoStart(_document, autoStart);
            }, false);

        internal static bool IsCategoryEnabled(string categoryKey)
        {
            if (State != UnionAirProjectSettingsState.Valid) return false;
            var identifier = CategoryIdentifier(categoryKey);
            return !string.IsNullOrEmpty(identifier) &&
                   _document.EnabledCategories.Contains(identifier);
        }

        internal static void SetCategoryEnabled(string categoryKey, bool enabled)
        {
            var identifier = CategoryIdentifier(categoryKey);
            if (string.IsNullOrEmpty(identifier)) return;

            ApplyDocumentChange(delegate
            {
                UnionAirProjectSettingsDocumentModel.SetCategoryEnabled(
                    _document, identifier, enabled);
            }, true);
        }

        internal static bool CustomHandlersEnabled
            => State == UnionAirProjectSettingsState.Valid &&
               _document.CustomHandlers;

        internal static void SetCustomHandlersEnabled(bool enabled)
        {
            ApplyDocumentChange(delegate
            {
                UnionAirProjectSettingsDocumentModel.SetCustomHandlersEnabled(
                    _document, enabled);
            }, true);
        }

        internal static bool AllowPlayModeSceneChanges
            => State == UnionAirProjectSettingsState.Valid &&
               _document.AllowSceneChanges;

        internal static void SetAllowPlayModeSceneChanges(bool enabled)
        {
            ApplyDocumentChange(delegate
            {
                UnionAirProjectSettingsDocumentModel.SetAllowSceneChanges(
                    _document, enabled);
            }, false);
        }

        internal static void DisableAllSensitiveApis()
            => ApplyDocumentChange(delegate
            {
                UnionAirProjectSettingsDocumentModel.DisableAllSensitiveApis(_document);
            }, true);

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

        private static void ApplyDocumentChange(Action change, bool refreshRoutes)
        {
            EnsureWorkingDocument();
            change();
            State = UnionAirProjectSettingsState.Valid;
            Error = null;
            _savePending = true;
            SaveSessionSnapshot();
            TryPersistWorkingDocument();
            if (refreshRoutes)
                UnionAirRouteRegistry.RefreshState();
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
            string ignoreWarning;
            if (TryWriteDocument(
                    SettingsPath,
                    UnionAirProjectPaths.ProjectRoot,
                    _document,
                    out persistenceError,
                    out ignoreWarning))
            {
                UnionAirEndpointDiscovery.UpdateIgnoreWarning(ignoreWarning);
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

        internal static bool TryWriteDocument(
            string settingsPath,
            string projectRoot,
            UnionAirProjectSettingsDocument document,
            out string persistenceError,
            out string ignoreWarning)
        {
            ignoreWarning = null;
            if (!UnionAirProjectSettingsSavePolicy.TryWrite(delegate
            {
                UnionAirEndpointDiscovery.WriteAtomicText(
                    settingsPath,
                    UnionAirProjectSettingsParser.Serialize(document));
            }, out persistenceError))
                return false;

            UnionAirEndpointDiscovery.TryEnsureIgnore(projectRoot, out ignoreWarning);
            return true;
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
                if (identifier.StartsWith("custom:", StringComparison.Ordinal) &&
                    !document.CustomHandlers)
                    throw new InvalidOperationException(
                        "Enable Custom Handlers before enabling a custom category.");
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

        internal static void DisableAllSensitiveApis(
            UnionAirProjectSettingsDocument document)
        {
            document.EnabledCategories.Clear();
            document.CustomHandlers = false;
            document.AllowSceneChanges = false;
        }
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

}
