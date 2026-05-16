using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>Persistent settings stored in EditorPrefs.</summary>
    public static class UnionAirSettings
    {
        private const string PortKey         = "UnionAir.Port";
        private const string AutoStartKey    = "UnionAir.AutoStart";
        private const string WriteEnabledKey      = "UnionAir.WriteEnabled";
        private const string AssetWriteEnabledKey  = "UnionAir.AssetWriteEnabled";

        private const string PlayModeEnabledKey  = "UnionAir.PlayModeEnabled";

        public static int Port
        {
            get => EditorPrefs.GetInt(PortKey, 8765);
            set => EditorPrefs.SetInt(PortKey, value);
        }

        public static bool AutoStart
        {
            get => EditorPrefs.GetBool(AutoStartKey, true);
            set => EditorPrefs.SetBool(AutoStartKey, value);
        }

        /// <summary>
        /// When false (default), all non-GET requests are rejected with 403.
        /// Must be explicitly enabled to allow scene modification via the API.
        /// </summary>
        public static bool WriteEnabled
        {
            get => EditorPrefs.GetBool(WriteEnabledKey, false);
            set => EditorPrefs.SetBool(WriteEnabledKey, value);
        }

        /// <summary>
        /// When false (default), all asset-mutating requests are rejected with 403.
        /// Covers: prefab creation, material editing, asset delete/move, scene save.
        /// Must be explicitly enabled separately from WriteEnabled.
        /// </summary>
        public static bool AssetWriteEnabled
        {
            get => EditorPrefs.GetBool(AssetWriteEnabledKey, false);
            set => EditorPrefs.SetBool(AssetWriteEnabledKey, value);
        }

        /// <summary>
        /// When false (default), play/stop/pause/step requests are rejected with 403.
        /// Controls EditorApplication.isPlaying and isPaused via the API.
        /// </summary>
        public static bool PlayModeEnabled
        {
            get => EditorPrefs.GetBool(PlayModeEnabledKey, false);
            set => EditorPrefs.SetBool(PlayModeEnabledKey, value);
        }
    }
}
