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
        private static readonly double[] AutoStartRetryDelaysSeconds =
        {
            0.1,
            0.25,
            0.5,
            1.0,
            2.0
        };
        private static readonly double[] UnexpectedRestartDelaysSeconds =
        {
            0.5,
            1.0,
            2.0
        };

        private static readonly int LifecycleGeneration;
        private static bool _autoStartScheduled;
        private static int _unexpectedRestartCount;
        private static int _retryDelayIndex;
        private static double _nextAutoStartTime;
        private static string _autoStartReason;

        /// <summary>Singleton server instance, accessible to the EditorWindow.</summary>
        public static RestHttpServer Server { get; } = new RestHttpServer();

        static UnionAirInit()
        {
            UnionAirLifecycleDiagnostics.Initialize();
            UnionAirSession.Initialize();
            LifecycleGeneration = UnionAirSession.Generation;
            Server.SetLifecycleGeneration(LifecycleGeneration);
            Server.UnexpectedlyStopped += OnServerUnexpectedlyStopped;

            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorApplication.quitting += OnEditorQuitting;

            LogStore.StartCapturing();
            LogLifecycle(
                $"initialize autoStart={UnionAirSettings.AutoStart} port={UnionAirSettings.Port}");

            if (UnionAirSettings.AutoStart)
                ScheduleAutoStart("editor-load");
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            LogLifecycle($"play-mode state={state} running={Server.IsRunning}");

            // The server keeps running through play mode; restart only if it stopped unexpectedly.
            if (state == PlayModeStateChange.EnteredEditMode && !Server.IsRunning && UnionAirSettings.AutoStart)
            {
                Debug.Log("[UnionAir] Restarting server after exiting play mode.");
                ScheduleAutoStart("play-mode-exit");
            }
        }

        private static void OnBeforeReload()
        {
            CancelAutoStart("before-assembly-reload");
            LogLifecycle($"before-reload begin running={Server.IsRunning}");

            try
            {
                // Always clean up retained listener state, even if IsListening is already false.
                Server.Stop("before-assembly-reload");
            }
            finally
            {
                LogLifecycle("before-reload complete");
                LogStore.StopCapturing();
            }
        }

        private static void OnEditorQuitting()
        {
            CancelAutoStart("editor-quitting");
            LogLifecycle($"editor-quitting begin running={Server.IsRunning}");
            try
            {
                Server.Stop("editor-quitting");
            }
            finally
            {
                LogLifecycle("editor-quitting complete");
                LogStore.StopCapturing();
            }
        }

        internal static void StartServerManually(int port)
        {
            CancelAutoStart("manual-start");
            Server.Start(port);
        }

        internal static void StopServerManually()
        {
            CancelAutoStart("manual-stop");
            Server.Stop("manual");
        }

        internal static void RestartServerManually(int port)
        {
            CancelAutoStart("manual-restart");
            Server.Stop("manual-restart");
            Server.Start(port);
        }

        private static void OnServerUnexpectedlyStopped(string reason)
        {
            if (!UnionAirSettings.AutoStart)
            {
                LogLifecycle(
                    $"unexpected server stop reason={reason} autoStart=false");
                Debug.LogError(
                    "[UnionAir] The listener thread exited unexpectedly. " +
                    "Automatic recovery is disabled.");
                return;
            }

            _unexpectedRestartCount++;
            var maxRestarts = UnexpectedRestartDelaysSeconds.Length;
            LogLifecycle(
                $"unexpected server stop reason={reason} autoStart=true " +
                $"restart={_unexpectedRestartCount}/{maxRestarts}");

            if (_unexpectedRestartCount > maxRestarts)
            {
                Debug.LogError(
                    $"[UnionAir] The listener thread exited unexpectedly. Automatic recovery " +
                    $"stopped after {maxRestarts} restart attempts in this domain.");
                return;
            }

            var delay = UnexpectedRestartDelaysSeconds[_unexpectedRestartCount - 1];
            Debug.LogError(
                $"[UnionAir] The listener thread exited unexpectedly. Automatic recovery " +
                $"{_unexpectedRestartCount}/{maxRestarts} will start in " +
                $"{delay:0.##} seconds.");
            ScheduleAutoStart(reason, delay);
        }

        private static void ScheduleAutoStart(string reason, double initialDelaySeconds = 0)
        {
            EditorApplication.update -= ProcessAutoStart;
            _autoStartScheduled = true;
            _retryDelayIndex = 0;
            _nextAutoStartTime = EditorApplication.timeSinceStartup + initialDelaySeconds;
            _autoStartReason = reason;
            EditorApplication.update += ProcessAutoStart;
            LogLifecycle(
                $"auto-start scheduled reason={reason} port={UnionAirSettings.Port} " +
                $"initialDelaySeconds={initialDelaySeconds:0.##}");
        }

        private static void ProcessAutoStart()
        {
            if (!_autoStartScheduled)
            {
                EditorApplication.update -= ProcessAutoStart;
                return;
            }

            if (!UnionAirSettings.AutoStart)
            {
                CancelAutoStart("auto-start-disabled");
                return;
            }

            if (Server.IsRunning)
            {
                CancelAutoStart("already-running");
                return;
            }

            if (EditorApplication.timeSinceStartup < _nextAutoStartTime)
                return;

            var attempt = _retryDelayIndex + 1;
            var port = UnionAirSettings.Port;
            LogLifecycle(
                $"auto-start attempt={attempt} reason={_autoStartReason} port={port}");

            if (Server.TryStart(
                    port,
                    $"{_autoStartReason}-attempt-{attempt}",
                    true))
            {
                CancelAutoStart($"started-attempt-{attempt}");
                return;
            }

            if (!Server.LastStartFailureWasAddressInUse)
            {
                LogLifecycle(
                    $"auto-start aborted attempt={attempt} reason={_autoStartReason} " +
                    "failure=non-retryable");
                CancelAutoStart("non-retryable-failure");
                return;
            }

            if (_retryDelayIndex >= AutoStartRetryDelaysSeconds.Length)
            {
                Debug.LogError(
                    $"[UnionAir] Automatic server startup failed after {attempt} attempts on " +
                    $"port {port}: the address remains in use.");
                UnionAirLifecycleDiagnostics.DumpFailure(
                    $"automatic startup exhausted {attempt} attempts " +
                    $"for reason={_autoStartReason} port={port}");
                CancelAutoStart("retry-exhausted");
                return;
            }

            var delay = AutoStartRetryDelaysSeconds[_retryDelayIndex];
            _retryDelayIndex++;
            _nextAutoStartTime = EditorApplication.timeSinceStartup + delay;
            LogLifecycle(
                $"auto-start retry scheduled nextAttempt={_retryDelayIndex + 1} " +
                $"delaySeconds={delay:0.##} reason={_autoStartReason} port={port}");
        }

        private static void CancelAutoStart(string reason)
        {
            EditorApplication.update -= ProcessAutoStart;
            if (_autoStartScheduled)
                LogLifecycle($"auto-start canceled reason={reason}");

            _autoStartScheduled = false;
            _retryDelayIndex = 0;
            _nextAutoStartTime = 0;
            _autoStartReason = null;
        }

        private static string LifecyclePrefix =>
            $"[UnionAir] lifecycle process={System.Diagnostics.Process.GetCurrentProcess().Id} " +
            $"generation={LifecycleGeneration}";

        private static void LogLifecycle(string message)
        {
            UnionAirLifecycleDiagnostics.Record($"{LifecyclePrefix} {message}");
        }
    }
}
