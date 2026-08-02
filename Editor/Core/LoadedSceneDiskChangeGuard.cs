using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    [Serializable]
    internal sealed class LoadedSceneDiskSnapshot
    {
        public string path = "";
        public string hash = "";
    }

    [Serializable]
    internal sealed class LoadedSceneDiskSnapshotState
    {
        public List<LoadedSceneDiskSnapshot> scenes = new List<LoadedSceneDiskSnapshot>();
    }

    internal sealed class LoadedSceneDiskConflict
    {
        internal string path;
        internal string name;
        internal bool isDirty;
        internal bool isActive;
        internal string reason;
    }

    /// <summary>
    /// Prevents UnionAir-initiated AssetDatabase refreshes from opening Unity's modal
    /// external-scene-change dialog and blocking every API request.
    /// </summary>
    internal static class LoadedSceneDiskChangeGuard
    {
        private const string SessionKey = "UnionAir.LoadedSceneDiskSnapshots";
        private const string ErrorMessage =
            "Cannot refresh assets while loaded scenes have external file changes. " +
            "Unload them before retrying to avoid Unity's interactive Reload dialog.";
        private const int MaxHashBootstrapAttempts = 120;
        private const int MaxReadinessBootstrapUpdates = 36000;

        private static LoadedSceneDiskSnapshotState _state;
        private static bool _initialized;
        private static bool _bootstrapRequired;
        private static bool _bootstrapScheduled;
        private static int _bootstrapAttempts;
        private static int _bootstrapReadinessUpdates;
        private static bool _persistenceWarningLogged;
        private static readonly HashSet<string> RecordSceneWarnings =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static void Initialize()
        {
            if (_initialized) return;

            var raw = "";
            var state = new LoadedSceneDiskSnapshotState();
            var bootstrapRequired = false;
            try
            {
                raw = SessionState.GetString(SessionKey, "");
                bootstrapRequired = string.IsNullOrEmpty(raw);
                state = bootstrapRequired
                    ? new LoadedSceneDiskSnapshotState()
                    : JsonUtility.FromJson<LoadedSceneDiskSnapshotState>(raw);
            }
            catch (Exception ex)
            {
                bootstrapRequired = true;
                Debug.LogWarning(
                    "[UnionAir] Loaded scene disk snapshots could not be restored: " +
                    ex.Message);
            }
            if (state == null)
            {
                state = new LoadedSceneDiskSnapshotState();
                bootstrapRequired = true;
            }
            if (state.scenes == null)
            {
                state.scenes = new List<LoadedSceneDiskSnapshot>();
                bootstrapRequired = true;
            }

            _state = state;
            _bootstrapRequired = bootstrapRequired;
            _bootstrapAttempts = 0;
            _bootstrapReadinessUpdates = 0;

            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            EditorSceneManager.sceneClosed += OnSceneClosed;

            _initialized = true;
            ScheduleBootstrap();
        }

        internal static List<LoadedSceneDiskConflict> FindConflicts()
        {
            EnsureInitialized();

            var conflicts = new List<LoadedSceneDiskConflict>();
            var activeScene = EditorSceneManager.GetActiveScene();

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || string.IsNullOrEmpty(scene.path))
                    continue;

                var snapshot = FindSnapshot(scene.path);
                string reason;
                if (snapshot == null)
                {
                    reason = "untracked";
                }
                else if (!TryComputeHash(scene.path, out var currentHash, out reason))
                {
                    // reason is set by TryComputeHash.
                }
                else if (string.Equals(snapshot.hash, currentHash, StringComparison.Ordinal))
                {
                    continue;
                }
                else
                {
                    reason = "modified";
                }

                conflicts.Add(new LoadedSceneDiskConflict
                {
                    path = scene.path,
                    name = scene.name,
                    isDirty = scene.isDirty,
                    isActive = scene == activeScene,
                    reason = reason,
                });
            }

            return conflicts;
        }

        internal static bool SendConflictIfAny(HttpListenerResponse response)
        {
            var conflicts = FindConflicts();
            if (conflicts.Count == 0)
                return false;

            RestResponse.Send(response, BuildConflictJson(conflicts), 409);
            return true;
        }

        internal static string BuildConflictJson(
            IReadOnlyList<LoadedSceneDiskConflict> loadedScenes)
        {
            var sb = new StringBuilder();
            sb.Append("{\"error\":\"");
            sb.Append(RestResponse.EscapeJson(ErrorMessage));
            sb.Append("\",\"code\":\"loaded_scene_external_change_blocked\",\"loadedScenes\":[");

            for (var i = 0; i < loadedScenes.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var scene = loadedScenes[i];
                sb.Append("{\"path\":\"");
                sb.Append(RestResponse.EscapeJson(scene.path));
                sb.Append("\",\"name\":\"");
                sb.Append(RestResponse.EscapeJson(scene.name));
                sb.Append("\",\"isDirty\":");
                sb.Append(RestResponse.FormatBool(scene.isDirty));
                sb.Append(",\"isActive\":");
                sb.Append(RestResponse.FormatBool(scene.isActive));
                sb.Append(",\"reason\":\"");
                sb.Append(RestResponse.EscapeJson(scene.reason));
                sb.Append("\"}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        internal static string BuildAbortReason(
            IReadOnlyList<LoadedSceneDiskConflict> loadedScenes)
        {
            var sb = new StringBuilder(
                "Asset refresh was blocked because loaded scenes changed externally: ");
            for (var i = 0; i < loadedScenes.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(loadedScenes[i].path);
                sb.Append(" (");
                sb.Append(loadedScenes[i].reason);
                sb.Append(")");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Captures the current baseline for every loaded scene.
        /// </summary>
        /// <remarks>
        /// Used around an operation that closes and reopens scenes internally without changing
        /// them on disk. A player build is one: <c>BuildPipeline.BuildPlayer</c> raises
        /// <c>sceneClosed</c> for the loaded scene and does not raise a matching
        /// <c>sceneOpened</c>, so the baseline is dropped and every later refresh reports the scene
        /// as <c>untracked</c> until someone saves or reopens it by hand. That would break the
        /// build-then-compile loop for no real reason.
        /// </remarks>
        internal static List<LoadedSceneDiskSnapshot> CaptureLoadedSceneBaselines()
        {
            EnsureInitialized();

            var captured = new List<LoadedSceneDiskSnapshot>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || string.IsNullOrEmpty(scene.path))
                    continue;

                var snapshot = FindSnapshot(scene.path);
                if (snapshot == null)
                    continue;

                captured.Add(new LoadedSceneDiskSnapshot
                {
                    path = snapshot.path,
                    hash = snapshot.hash,
                });
            }
            return captured;
        }

        /// <summary>
        /// Restores captured baselines for scenes whose file is byte-identical to what it was.
        /// </summary>
        /// <param name="captured">Baselines from <see cref="CaptureLoadedSceneBaselines"/>.</param>
        /// <remarks>
        /// The hash comparison is what makes this safe rather than a blanket re-baseline. A scene
        /// someone changed on disk while the operation ran still fails the comparison, stays
        /// untracked, and still trips the guard — which is the case the guard exists for.
        /// </remarks>
        internal static void RestoreUnchangedBaselines(IReadOnlyList<LoadedSceneDiskSnapshot> captured)
        {
            EnsureInitialized();
            if (captured == null || captured.Count == 0)
                return;

            var changed = false;
            for (var i = 0; i < captured.Count; i++)
            {
                var entry = captured[i];
                if (entry == null || string.IsNullOrEmpty(entry.path))
                    continue;
                if (FindSnapshot(entry.path) != null)
                    continue;
                if (!IsSceneLoaded(entry.path))
                    continue;
                if (!TryComputeHash(entry.path, out var hash, out _))
                    continue;
                if (!string.Equals(hash, entry.hash, StringComparison.Ordinal))
                    continue;

                _state.scenes.Add(new LoadedSceneDiskSnapshot { path = entry.path, hash = hash });
                RecordSceneWarnings.Remove(entry.path);
                changed = true;
            }

            if (changed)
                Persist();
        }

        private static bool IsSceneLoaded(string path)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded &&
                    string.Equals(scene.path, path, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Re-baselines only scenes affected by a known-safe AssetDatabase move.
        /// This also covers a folder move without masking conflicts in unrelated scenes.
        /// </summary>
        internal static void RecordLoadedScenesAfterAssetMove(string oldPath, string newPath)
        {
            EnsureInitialized();
            RecordSceneWarnings.RemoveWhere(
                path => IsSameOrDescendantPath(path, oldPath));

            for (var i = _state.scenes.Count - 1; i >= 0; i--)
            {
                if (IsSameOrDescendantPath(_state.scenes[i].path, oldPath))
                    _state.scenes.RemoveAt(i);
            }

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded ||
                    !IsSameOrDescendantPath(scene.path, newPath))
                    continue;

                if (TryComputeHash(scene.path, out var hash, out var reason))
                {
                    _state.scenes.Add(new LoadedSceneDiskSnapshot
                    {
                        path = scene.path,
                        hash = hash,
                    });
                    RecordSceneWarnings.Remove(scene.path);
                }
                else
                    WarnRecordSceneFailure(scene.path, reason);
            }

            Persist();
        }

        internal static bool IsSameOrDescendantPath(string candidate, string root)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(root))
                return false;

            var normalizedCandidate = candidate.Replace('\\', '/').TrimEnd('/');
            var normalizedRoot = root.Replace('\\', '/').TrimEnd('/');
            return string.Equals(
                       normalizedCandidate,
                       normalizedRoot,
                       StringComparison.OrdinalIgnoreCase) ||
                   (normalizedCandidate.Length > normalizedRoot.Length &&
                    normalizedCandidate.StartsWith(
                        normalizedRoot,
                        StringComparison.OrdinalIgnoreCase) &&
                    normalizedCandidate[normalizedRoot.Length] == '/');
        }

        internal static bool TryComputeHash(
            string scenePath,
            out string hash,
            out string reason)
        {
            hash = "";
            reason = null;

            if (string.IsNullOrEmpty(scenePath))
            {
                reason = "untracked";
                return false;
            }

            try
            {
                if (!File.Exists(scenePath))
                {
                    reason = "missing";
                    return false;
                }

                using (var stream = new FileStream(
                    scenePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var sha256 = SHA256.Create())
                {
                    hash = ToHex(sha256.ComputeHash(stream));
                    return true;
                }
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException ||
                ex is NotSupportedException)
            {
                reason = "unreadable";
                return false;
            }
        }

        internal static string ToHex(byte[] bytes)
        {
            var chars = new char[bytes.Length * 2];
            const string hex = "0123456789abcdef";
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = hex[bytes[i] >> 4];
                chars[i * 2 + 1] = hex[bytes[i] & 0x0f];
            }
            return new string(chars);
        }

        private static void EnsureInitialized()
        {
            if (!_initialized)
                Initialize();
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
            => RecordScene(scene);

        private static void OnSceneSaved(Scene scene)
            => RecordScene(scene);

        private static void OnSceneClosed(Scene scene)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(scene.path)) return;

            RecordSceneWarnings.Remove(scene.path);
            var index = FindSnapshotIndex(scene.path);
            if (index < 0) return;

            _state.scenes.RemoveAt(index);
            Persist();
        }

        private static void RecordScene(Scene scene)
        {
            EnsureInitialized();
            if (!scene.isLoaded || string.IsNullOrEmpty(scene.path))
                return;
            if (!TryComputeHash(scene.path, out var hash, out var reason))
            {
                WarnRecordSceneFailure(scene.path, reason);
                return;
            }

            RecordSceneWarnings.Remove(scene.path);
            var snapshot = FindSnapshot(scene.path);
            if (snapshot == null)
            {
                _state.scenes.Add(new LoadedSceneDiskSnapshot
                {
                    path = scene.path,
                    hash = hash,
                });
            }
            else
            {
                snapshot.path = scene.path;
                snapshot.hash = hash;
            }

            Persist();
        }

        private static void ScheduleBootstrap()
        {
            if (!_bootstrapRequired || _bootstrapScheduled)
                return;

            _bootstrapScheduled = true;
            // Use the same pump as the HTTP server. delayCall does not run reliably while the
            // Editor is in the background, which is the normal state for agent-driven workflows.
            EditorApplication.update -= BootstrapLoadedScenesWhenReady;
            EditorApplication.update += BootstrapLoadedScenesWhenReady;
        }

        private static void BootstrapLoadedScenesWhenReady()
        {
            EditorApplication.update -= BootstrapLoadedScenesWhenReady;
            _bootstrapScheduled = false;
            if (!_initialized || !_bootstrapRequired)
                return;

            if (EditorApplication.isUpdating || SceneManager.sceneCount == 0)
            {
                _bootstrapReadinessUpdates++;
                if (_bootstrapReadinessUpdates < MaxReadinessBootstrapUpdates)
                {
                    ScheduleBootstrap();
                }
                else
                {
                    StopBootstrapWithWarning(
                        "Loaded scene disk baseline bootstrap stopped before the Editor became ready. " +
                        "Scenes opened or saved later will still be tracked.");
                }
                return;
            }

            if (TryBootstrapLoadedScenes())
            {
                _bootstrapRequired = false;
                return;
            }

            _bootstrapAttempts++;
            if (_bootstrapAttempts < MaxHashBootstrapAttempts)
                ScheduleBootstrap();
            else
                StopBootstrapWithWarning(
                    "Loaded scene disk baseline bootstrap stopped after repeated file read failures. " +
                    "Affected scenes will remain untracked until they are saved or reopened.");
        }

        private static bool TryBootstrapLoadedScenes()
        {
            var allRecorded = true;
            var changed = false;

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || string.IsNullOrEmpty(scene.path))
                    continue;
                if (FindSnapshot(scene.path) != null)
                    continue;

                if (TryComputeHash(scene.path, out var hash, out _))
                {
                    _state.scenes.Add(new LoadedSceneDiskSnapshot
                    {
                        path = scene.path,
                        hash = hash,
                    });
                    RecordSceneWarnings.Remove(scene.path);
                    changed = true;
                }
                else
                {
                    allRecorded = false;
                }
            }

            if (changed)
                Persist();
            return allRecorded;
        }

        private static void StopBootstrapWithWarning(string message)
        {
            _bootstrapRequired = false;
            Debug.LogWarning("[UnionAir] " + message);
        }

        private static void WarnRecordSceneFailure(string scenePath, string reason)
        {
            if (string.IsNullOrEmpty(scenePath) || !RecordSceneWarnings.Add(scenePath))
                return;

            Debug.LogWarning(
                "[UnionAir] Could not record a disk baseline for loaded scene '" +
                scenePath + "' (" + (reason ?? "unknown") +
                "). Refresh and compile requests will report it as untracked until the scene " +
                "is saved or reopened.");
        }

        private static LoadedSceneDiskSnapshot FindSnapshot(string path)
        {
            var index = FindSnapshotIndex(path);
            return index < 0 ? null : _state.scenes[index];
        }

        private static int FindSnapshotIndex(string path)
        {
            for (var i = 0; i < _state.scenes.Count; i++)
            {
                if (string.Equals(
                        _state.scenes[i].path,
                        path,
                        StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static void Persist()
        {
            try
            {
                SessionState.SetString(SessionKey, JsonUtility.ToJson(_state));
            }
            catch (Exception ex)
            {
                if (_persistenceWarningLogged) return;
                _persistenceWarningLogged = true;
                Debug.LogWarning(
                    "[UnionAir] Loaded scene disk snapshots could not be persisted: " +
                    ex.Message);
            }
        }
    }
}
