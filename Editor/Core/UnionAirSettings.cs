using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Resolves UnionAir settings from project configuration or legacy <see cref="EditorPrefs"/>.
    /// </summary>
    public static class UnionAirSettings
    {
        private const string PortKey         = "UnionAir.Port";
        private const string AutoStartKey    = "UnionAir.AutoStart";
        private const string CustomHandlersEnabledKey = "UnionAir.CustomHandlersEnabled";
        private const string AllowPlayModeSceneChangesKey = "UnionAir.AllowPlayModeSceneChanges";
        private const string DiagnosticLifecycleLoggingKey = "UnionAir.DiagnosticLifecycleLogging";
        private const string EnabledCategoriesKey = "UnionAir.EnabledCategories";
        private const string DisabledCategoriesKey = "UnionAir.DisabledCategories";

        /// <summary>
        /// Gets or sets the configured TCP port used by the local HTTP server.
        /// A value of <c>0</c> selects Automatic mode; a running server exposes its concrete port
        /// through <see cref="RestHttpServer.Port"/>.
        /// </summary>
        /// <remarks>A valid project settings file takes precedence over the stored EditorPrefs value.</remarks>
        public static int Port
        {
            get => UnionAirProjectSettings.ResolvePort(EditorPrefs.GetInt(PortKey, 0));
            set => EditorPrefs.SetInt(PortKey, value);
        }

        /// <summary>
        /// Gets or sets whether UnionAir should start automatically when the Unity editor loads.
        /// </summary>
        /// <remarks>A valid project settings file takes precedence over the stored EditorPrefs value.</remarks>
        public static bool AutoStart
        {
            get => UnionAirProjectSettings.ResolveAutoStart(EditorPrefs.GetBool(AutoStartKey, true));
            set => EditorPrefs.SetBool(AutoStartKey, value);
        }

        /// <summary>
        /// Gets or sets whether custom controllers from external Editor assemblies are globally enabled.
        /// </summary>
        /// <remarks>
        /// When this value is <c>false</c>, custom categories and endpoints remain discoverable with
        /// <c>includeDisabled=true</c> but are not routable.
        /// </remarks>
        public static bool CustomHandlersEnabled
        {
            get => UnionAirProjectSettings.State == UnionAirProjectSettingsState.Missing
                ? EditorPrefs.GetBool(CustomHandlersEnabledKey, false)
                : UnionAirProjectSettings.CustomHandlersEnabled;
            set
            {
                if (UnionAirProjectSettings.State == UnionAirProjectSettingsState.Missing)
                    EditorPrefs.SetBool(CustomHandlersEnabledKey, value);
                else
                    UnionAirProjectSettings.SetCustomHandlersEnabled(value);
            }
        }

        /// <summary>
        /// Gets or sets whether scene-object write endpoints may run in Play mode when the request opts in.
        /// </summary>
        public static bool AllowPlayModeSceneChanges
        {
            get => UnionAirProjectSettings.State == UnionAirProjectSettingsState.Missing
                ? EditorPrefs.GetBool(AllowPlayModeSceneChangesKey, false)
                : UnionAirProjectSettings.AllowPlayModeSceneChanges;
            set
            {
                if (UnionAirProjectSettings.State == UnionAirProjectSettingsState.Missing)
                    EditorPrefs.SetBool(AllowPlayModeSceneChangesKey, value);
                else
                    UnionAirProjectSettings.SetAllowPlayModeSceneChanges(value);
            }
        }

        /// <summary>
        /// Gets or sets whether detailed server lifecycle events are written to the Unity Console.
        /// Events are retained silently for failure diagnostics even when this setting is disabled.
        /// </summary>
        public static bool DiagnosticLifecycleLogging
        {
            get => EditorPrefs.GetBool(DiagnosticLifecycleLoggingKey, false);
            set => EditorPrefs.SetBool(DiagnosticLifecycleLoggingKey, value);
        }

        /// <summary>
        /// Returns the effective enabled state for a category key.
        /// </summary>
        /// <param name="key">Stable category key, usually <see cref="UnionAirCategoryDefinition.Key"/>.</param>
        /// <param name="enabledByDefault">Default state declared by the category metadata.</param>
        /// <returns>The effective enabled state after applying persisted user overrides.</returns>
        public static bool IsCategoryEnabled(string key, bool enabledByDefault)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (UnionAirProjectSettings.State == UnionAirProjectSettingsState.Valid)
                return UnionAirProjectSettings.IsCategoryEnabled(key);
            if (UnionAirProjectSettings.State == UnionAirProjectSettingsState.Invalid)
                return false;
            var enabled = EditorPrefs.GetString(EnabledCategoriesKey, "");
            if (ContainsToken(enabled, key)) return true;
            var disabled = EditorPrefs.GetString(DisabledCategoriesKey, "");
            if (ContainsToken(disabled, key)) return false;
            return enabledByDefault;
        }

        /// <summary>
        /// Persists a category enabled-state override.
        /// </summary>
        /// <param name="key">Stable category key, usually <see cref="UnionAirCategoryDefinition.Key"/>.</param>
        /// <param name="enabled">New enabled state requested by the user.</param>
        /// <param name="enabledByDefault">Default state declared by the category metadata.</param>
        public static void SetCategoryEnabled(string key, bool enabled, bool enabledByDefault)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (UnionAirProjectSettings.State != UnionAirProjectSettingsState.Missing)
            {
                UnionAirProjectSettings.SetCategoryEnabled(key, enabled);
                return;
            }

            var explicitlyEnabled = EditorPrefs.GetString(EnabledCategoriesKey, "");
            var disabled = EditorPrefs.GetString(DisabledCategoriesKey, "");
            if (enabled == enabledByDefault)
            {
                explicitlyEnabled = RemoveToken(explicitlyEnabled, key);
                disabled = RemoveToken(disabled, key);
            }
            else if (enabled)
            {
                disabled = RemoveToken(disabled, key);
                if (!ContainsToken(explicitlyEnabled, key))
                    explicitlyEnabled = string.IsNullOrEmpty(explicitlyEnabled) ? key : explicitlyEnabled + "," + key;
            }
            else
            {
                explicitlyEnabled = RemoveToken(explicitlyEnabled, key);
                if (!ContainsToken(disabled, key))
                    disabled = string.IsNullOrEmpty(disabled) ? key : disabled + "," + key;
            }

            EditorPrefs.SetString(EnabledCategoriesKey, explicitlyEnabled);
            EditorPrefs.SetString(DisabledCategoriesKey, disabled);
        }

        private static bool ContainsToken(string csv, string token)
        {
            var parts = csv.Split(',');
            foreach (var part in parts)
                if (part == token) return true;
            return false;
        }

        private static string RemoveToken(string csv, string token)
        {
            var parts = csv.Split(',');
            var result = "";
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part) || part == token) continue;
                result = string.IsNullOrEmpty(result) ? part : result + "," + part;
            }
            return result;
        }
    }
}
