using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Bootstraps the REST server when the Unity Editor loads and manages its lifecycle
    /// across domain reloads and play-mode transitions.
    /// </summary>
    [InitializeOnLoad]
    public static class UnionAirInit
    {
        /// <summary>Singleton server instance, accessible to the EditorWindow.</summary>
        public static RestHttpServer Server { get; } = new RestHttpServer();

        static UnionAirInit()
        {
            LogStore.StartCapturing();

            if (UnionAirSettings.AutoStart)
                Server.Start(UnionAirSettings.Port);

            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            // The server keeps running through play mode; restart only if it stopped unexpectedly.
            if (state == PlayModeStateChange.EnteredEditMode && !Server.IsRunning && UnionAirSettings.AutoStart)
            {
                Debug.Log("[UnionAir] Restarting server after exiting play mode.");
                Server.Start(UnionAirSettings.Port);
            }
        }

        private static void OnBeforeReload()
        {
            // Must stop before domain reload to release the port and background thread.
            if (Server.IsRunning)
                Server.Stop();

            LogStore.StopCapturing();
        }
    }
}
