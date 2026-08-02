using System.Collections.Generic;
using System.Net;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Rejects a build while a loaded scene has unsaved changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>BuildPipeline.BuildPlayer</c> reads scenes from their saved assets. Called from script it
    /// does not prompt, unlike the Build Settings window, so a scene edited through the API but not
    /// saved is silently excluded from the player and the build reports success for content that
    /// does not match what the Editor is showing. That is the worst possible failure mode for an
    /// automated client: a green result describing something it did not build.
    /// </para>
    /// <para>
    /// Saving implicitly is deliberately not an option. Writing a person's unsaved scene to disk on
    /// their behalf, as a side effect of a build request, is a larger surprise than a rejection.
    /// </para>
    /// <para>
    /// This is not what <see cref="LoadedSceneDiskChangeGuard"/> covers. That guard compares a
    /// loaded scene against the file on disk to catch <em>external</em> changes; this one catches
    /// in-memory edits that never reached the file. Both can be true at once, and neither implies
    /// the other.
    /// </para>
    /// </remarks>
    internal static class UnsavedSceneGuard
    {
        private const string ErrorMessage =
            "Cannot build while loaded scenes have unsaved changes. " +
            "BuildPipeline.BuildPlayer reads scenes from disk, so the build would not contain them. " +
            "Save the reported scenes explicitly before retrying.";

        /// <summary>Returns every loaded scene with unsaved changes.</summary>
        internal static List<LoadedSceneDiskConflict> FindConflicts()
        {
            var conflicts = new List<LoadedSceneDiskConflict>();
            var activeScene = EditorSceneManager.GetActiveScene();

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !scene.isDirty)
                    continue;

                conflicts.Add(new LoadedSceneDiskConflict
                {
                    path = scene.path,
                    name = scene.name,
                    isDirty = true,
                    isActive = scene == activeScene,
                    // An unsaved scene that was never saved at all has no path to save back to,
                    // which needs a different fix from saving, so it is named separately.
                    reason = string.IsNullOrEmpty(scene.path) ? "unsavedNewScene" : "unsaved",
                });
            }

            return conflicts;
        }

        /// <summary>
        /// Writes the <c>409</c> body when any loaded scene is unsaved.
        /// </summary>
        /// <returns><c>true</c> when the request was rejected.</returns>
        internal static bool SendConflictIfAny(HttpListenerResponse response)
        {
            var conflicts = FindConflicts();
            if (conflicts.Count == 0)
                return false;

            RestResponse.Send(response, BuildConflictJson(conflicts), 409);
            return true;
        }

        /// <summary>
        /// Builds the conflict body, matching the shape
        /// <see cref="LoadedSceneDiskChangeGuard.BuildConflictJson"/> already uses.
        /// </summary>
        internal static string BuildConflictJson(IReadOnlyList<LoadedSceneDiskConflict> loadedScenes)
        {
            var sb = new StringBuilder();
            sb.Append("{\"error\":\"");
            sb.Append(RestResponse.EscapeJson(ErrorMessage));
            sb.Append("\",\"code\":\"loaded_scene_unsaved_blocked\",\"loadedScenes\":[");

            for (var i = 0; i < loadedScenes.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var scene = loadedScenes[i];
                sb.Append("{\"path\":\"").Append(RestResponse.EscapeJson(scene.path));
                sb.Append("\",\"name\":\"").Append(RestResponse.EscapeJson(scene.name));
                sb.Append("\",\"isDirty\":").Append(RestResponse.FormatBool(scene.isDirty));
                sb.Append(",\"isActive\":").Append(RestResponse.FormatBool(scene.isActive));
                sb.Append(",\"reason\":\"").Append(RestResponse.EscapeJson(scene.reason));
                sb.Append("\"}");
            }

            sb.Append("]}");
            return sb.ToString();
        }
    }
}
