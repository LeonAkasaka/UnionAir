using UnityEditor;
using UnityEditor.Compilation;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Subscribes <see cref="CompileService"/> to the Unity compilation pipeline.
    /// </summary>
    [InitializeOnLoad]
    internal static class CompileInit
    {
        static CompileInit()
        {
            CompileService.Initialize();

            // assemblyCompilationStarted is deliberately not used: under the Bee build system it
            // is raised back to back with assemblyCompilationFinished at the end of the cycle and
            // carries no progress information.
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished -= CompileService.OnAssemblyCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += CompileService.OnAssemblyCompilationFinished;
            CompilationPipeline.assemblyCompilationNotRequired -= CompileService.OnAssemblyCompilationNotRequired;
            CompilationPipeline.assemblyCompilationNotRequired += CompileService.OnAssemblyCompilationNotRequired;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;

            EditorApplication.update -= CompileService.Update;
            EditorApplication.update += CompileService.Update;

            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;
        }

        private static void OnCompilationStarted(object context)
            => CompileService.OnCompilationStarted(context);

        private static void OnCompilationFinished(object context)
            => CompileService.OnCompilationFinished(context);

        private static void OnBeforeAssemblyReload()
            => CompileService.FinalizeBeforeReload(
                "The Unity Editor reloaded the assembly domain before compilation reported a result.");

        private static void OnEditorQuitting()
            => CompileService.FinalizeBeforeReload(
                "The Unity Editor quit before compilation reported a result.");
    }
}
