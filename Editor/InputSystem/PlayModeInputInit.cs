using UnityEditor;

[assembly: LeonAkasaka.UnionAir.Editor.UnionAirBuiltinAssembly]

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Bootstraps Input System-specific lifecycle hooks independently of the main
    /// <see cref="UnionAirInit"/> class. This keeps the main assembly free of any
    /// dependency on the com.unity.inputsystem package.
    /// </summary>
    [InitializeOnLoad]
    internal static class PlayModeInputInit
    {
        static PlayModeInputInit()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode ||
                state == PlayModeStateChange.ExitingPlayMode)
                PlayModeInputHandler.Cleanup();
        }

        private static void OnBeforeAssemblyReload()
        {
            // A domain reload while staying in Play mode (e.g. a script recompile) wipes the
            // sequence state and its EditorApplication.update hook, so answer any pending
            // deferred response now instead of leaving the HTTP client hanging.
            PlayModeInputHandler.AbortActiveSequence();
        }
    }
}
