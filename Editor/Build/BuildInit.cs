using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Initializes the build services and finalizes an interrupted build.
    /// </summary>
    /// <remarks>
    /// A build has no Unity callback that fires when it is interrupted, so the reload and quit
    /// hooks are the only chance to write a terminal state for one that was in flight. What they
    /// miss — a hard crash — is caught by the reconciliation in <see cref="BuildService.Initialize"/>.
    /// A build target switch is deliberately not finalized by those hooks: the domain reload is its
    /// expected path, and <see cref="BuildTargetSwitchService.Initialize"/> resolves it on the far
    /// side by comparing the active target against the requested one.
    /// </remarks>
    [InitializeOnLoad]
    internal static class BuildInit
    {
        static BuildInit()
        {
            BuildService.Initialize();
            BuildTargetSwitchService.Initialize();

            // Guards the window between the 202 and the deferred start; see BuildService.Update.
            EditorApplication.update -= BuildService.Update;
            EditorApplication.update += BuildService.Update;

            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;
        }

        private static void OnBeforeAssemblyReload()
            => BuildService.FinalizeBeforeReload(
                "The Unity Editor reloaded the assembly domain before the build reported a result.");

        private static void OnEditorQuitting()
            => BuildService.FinalizeBeforeReload(
                "The Unity Editor quit before the build reported a result.");
    }
}
