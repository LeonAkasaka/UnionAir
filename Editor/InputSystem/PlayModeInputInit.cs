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
            // Registering here is what tells the main assembly that replays are possible at all;
            // without this package there is no driver and POST /api/editor/play rejects an
            // 'inputs' list before entering Play mode.
            InputReplayService.RegisterDriver(InputReplayDriver.Instance);

            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode ||
                state == PlayModeStateChange.ExitingPlayMode)
                PlayModeInputHandler.Cleanup();

            // Starting an armed replay has to follow the cleanup above. Handler ordering for
            // playModeStateChanged across assemblies is undefined, so sharing one handler is the
            // only way to guarantee the cleanup cannot wipe state the replay just established.
            if (state == PlayModeStateChange.EnteredPlayMode)
                InputReplayService.OnEnteredPlayMode();
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
