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
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode ||
                state == PlayModeStateChange.ExitingPlayMode)
                PlayModeInputHandler.Cleanup();
        }
    }
}
