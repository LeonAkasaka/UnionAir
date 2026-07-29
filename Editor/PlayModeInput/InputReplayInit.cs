using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Bootstraps input replay lifecycle hooks.
    /// </summary>
    /// <remarks>
    /// The static constructor is the after-reload hook: this project has no
    /// <c>afterAssemblyReload</c> subscriber, and <c>[InitializeOnLoad]</c> runs at exactly the
    /// point persisted state needs to be recovered.
    /// <para>
    /// Starting an armed replay is deliberately not wired here. It belongs to the Input System
    /// assembly's play mode handler, which must clean up virtual devices first — and handler
    /// ordering across assemblies is undefined, so the two steps have to share one handler.
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    internal static class InputReplayInit
    {
        static InputReplayInit()
        {
            // Recovery before any subscription, so a hook cannot observe unrecovered state.
            InputReplayService.Initialize();

            EditorApplication.update -= InputReplayService.Update;
            EditorApplication.update += InputReplayService.Update;

            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;

            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnBeforeAssemblyReload()
            => InputReplayService.FinalizeBeforeReload(
                "The Unity Editor domain was reloaded during the replay, which breaks its frame timing.");

        private static void OnEditorQuitting()
            => InputReplayService.FinalizeBeforeReload("The Unity Editor quit during the replay.");

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
                InputReplayService.OnExitingPlayMode();
        }
    }
}
