using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Owns the lifecycle of an input replay: arming it before Play mode, starting it once Play
    /// mode is running, recording what happened, and finishing it exactly once.
    /// </summary>
    /// <remarks>
    /// Every decision this class acts on comes from <see cref="InputReplayDecision"/> or
    /// <see cref="InputReplaySchedule"/>, both of which are unit-tested; what remains here is
    /// Editor wiring that cannot be exercised without a running Editor.
    /// </remarks>
    internal static class InputReplayService
    {
        private const double DefaultFlushIntervalSeconds = 0.5;
        private const double LargeReplayFlushIntervalSeconds = 2.0;

        /// <summary>
        /// Above this many events, progress is flushed less often. Each flush re-serializes the
        /// whole record, and the schedule has no size cap by design.
        /// </summary>
        private const int LargeReplayEventCount = 2000;

        private static InputReplayRecord _current;
        private static InputReplayRecord _latest;
        private static IInputReplayDriver _driver;

        private static bool _dirty;
        private static bool _flushErrorLogged;
        private static double _lastFlushAt;
        private static double _queuedAt;
        private static double _lastFrameAdvanceAt;
        private static int _lastObservedUnityFrame = -1;
        private static bool _framesHaveAdvanced;

        /// <summary>The active or most recently armed replay, or null.</summary>
        internal static InputReplayRecord Current => _current;

        /// <summary>The most recently finished replay, or null.</summary>
        internal static InputReplayRecord Latest => _latest;

        /// <summary>Whether a replay is armed or running.</summary>
        internal static bool IsActive => _current != null && _current.IsActive;

        /// <summary>Id of the armed or running replay, or null.</summary>
        internal static string ActiveId => IsActive ? _current.id : null;

        /// <summary>Whether the optional Input System assembly registered a driver.</summary>
        internal static bool HasDriver => _driver != null;

        /// <summary>
        /// Registers the driver that applies events to virtual devices. Called from the Input
        /// System assembly's bootstrap; ordering against this assembly's bootstrap is irrelevant
        /// because nothing reads the driver until a request or a Play mode transition arrives.
        /// </summary>
        internal static void RegisterDriver(IInputReplayDriver driver)
        {
            _driver = driver;
        }

        /// <summary>
        /// Recovers persisted state after a domain reload or an Editor restart.
        /// </summary>
        internal static void Initialize()
        {
            _current = InputReplayStore.Read(InputReplayStore.CurrentPath);
            _latest = InputReplayStore.Read(InputReplayStore.LatestPath);

            // A record that still claims to be live while no gate is open belongs to a replay
            // whose process is gone: the gate lives in SessionState and dies with the Editor.
            if (_current != null && _current.IsActive && !UnionAirInputReplayGate.IsActive)
            {
                Abort(_current, "domainReload",
                    "The Unity Editor domain was reloaded or restarted during the replay.");
                Commit(releaseHeld: false);
            }

            // The mirror case: a gate with no live record behind it. Nothing would ever close it,
            // and it would reject every later replay for the rest of the Editor session, so it is
            // treated as debris rather than trusted.
            if (UnionAirInputReplayGate.IsActive &&
                (_current == null || !_current.IsActive || _current.id != UnionAirInputReplayGate.Id))
                UnionAirInputReplayGate.End();

            var now = EditorApplication.timeSinceStartup;
            _queuedAt = now;
            _lastFlushAt = now;
            _lastFrameAdvanceAt = now;
            _lastObservedUnityFrame = -1;
            _framesHaveAdvanced = false;
        }

        /// <summary>
        /// Persists a validated schedule and opens the gate, so the replay survives the domain
        /// reload that entering Play mode causes.
        /// </summary>
        internal static InputReplayRecord Arm(List<InputReplayEventSpec> inputs)
        {
            var record = new InputReplayRecord
            {
                id = InputReplayStore.NewId(),
                state = InputReplayState.Queued,
                sessionId = UnionAirSession.SessionId,
                requestedAt = Timestamp(),
                lifecycleGenerationAtRequest = UnionAirSession.Generation,
                eventCount = inputs.Count
            };

            record.inputs.AddRange(inputs);
            for (var i = 0; i < inputs.Count; i++)
                record.events.Add(new InputReplayEventResult { index = i });

            _current = record;

            // The gate is only opened once the schedule is safely on disk. Opening it for a
            // record that could not be written would leave a liveness bit with nothing behind
            // it: after the domain reload there would be no schedule to restore and nothing to
            // close the gate, so every later replay would be rejected for the rest of the
            // Editor session.
            if (!FlushNow())
            {
                _current = null;
                return null;
            }

            UnionAirInputReplayGate.Begin(record.id);

            var now = EditorApplication.timeSinceStartup;
            _queuedAt = now;
            _lastFrameAdvanceAt = now;
            _lastObservedUnityFrame = -1;
            _framesHaveAdvanced = false;
            return record;
        }

        /// <summary>
        /// Abandons a replay that is armed but has not started, for a caller that changed its
        /// mind before Play mode began.
        /// </summary>
        internal static void CancelQueued(string reason)
        {
            if (_current == null || _current.state != InputReplayState.Queued) return;

            Abort(_current, "cancelled", reason);
            Commit(releaseHeld: false);
        }

        /// <summary>
        /// Starts the armed replay. Called from the Input System assembly's play mode handler,
        /// after its virtual device cleanup, because handler ordering across assemblies is
        /// undefined and cleanup would otherwise wipe freshly armed state.
        /// </summary>
        internal static void OnEnteredPlayMode()
        {
            if (_current == null || _current.state != InputReplayState.Queued) return;
            if (!UnionAirInputReplayGate.IsActive || UnionAirInputReplayGate.Id != _current.id) return;

            if (_driver == null)
            {
                Abort(_current, "driverUnavailable",
                    "Input replay requires the com.unity.inputsystem package.");
                Commit(releaseHeld: false);
                return;
            }

            string error;
            if (!_driver.TryBegin(_current, out error))
            {
                Abort(_current, "driverRefused", error);
                Commit(releaseHeld: false);
                return;
            }

            _current.state = InputReplayState.Running;
            _current.startedAt = Timestamp();
            _lastFrameAdvanceAt = EditorApplication.timeSinceStartup;
            _lastObservedUnityFrame = -1;
            _framesHaveAdvanced = false;
            FlushNow();
        }

        /// <summary>Abandons a replay that Play mode ended underneath.</summary>
        internal static void OnExitingPlayMode()
        {
            if (_current == null || !_current.IsActive) return;

            Abort(_current, "playModeExited",
                InputReplayDecision.ReasonFor(InputReplayWatchdogAction.AbortPlayModeExited));
            // Exiting Play mode resets the virtual devices on its own.
            Commit(releaseHeld: false);
        }

        /// <summary>
        /// Answers a replay that a domain reload is about to destroy.
        /// </summary>
        /// <remarks>
        /// A reload while <em>armed</em> is the expected path into Play mode, so the record is
        /// only flushed. A reload while <em>running</em> destroys the frame timing the replay
        /// exists to guarantee, so it is abandoned rather than resumed.
        /// </remarks>
        internal static void FinalizeBeforeReload(string reason)
        {
            if (_current == null) return;

            if (_current.state == InputReplayState.Running)
            {
                Abort(_current, "domainReload", reason);
                Commit(releaseHeld: false);
                return;
            }

            FlushNow();
        }

        /// <summary>Pumps progress flushing and the watchdog. Attached to EditorApplication.update.</summary>
        internal static void Update()
        {
            if (_current == null) return;

            if (_current.IsActive)
            {
                var now = EditorApplication.timeSinceStartup;

                // Refreshing on every frame advance makes this a stall detector rather than a
                // slowness detector: a long replay at a low frame rate must never trip it.
                if (EditorApplication.isPlaying && Time.frameCount != _lastObservedUnityFrame)
                {
                    // The first reading only establishes a baseline; a second, different one is
                    // what proves the player loop is actually running.
                    if (_lastObservedUnityFrame >= 0) _framesHaveAdvanced = true;
                    _lastObservedUnityFrame = Time.frameCount;
                    _lastFrameAdvanceAt = now;
                }
                else if (EditorApplication.isPaused)
                {
                    // Pausing stops the player loop on purpose, so the pause must stay out of the
                    // stall budget. Suppressing only the abort would let the paused time
                    // accumulate and trip the detector on the first tick after resuming, before
                    // the player loop has had any chance to produce a frame.
                    _lastFrameAdvanceAt = now;
                }

                var action = InputReplayDecision.DecideWatchdog(
                    _current.state,
                    EditorApplication.isPlaying,
                    EditorApplication.isPaused,
                    now - _queuedAt,
                    now - _lastFrameAdvanceAt,
                    _framesHaveAdvanced);

                if (action != InputReplayWatchdogAction.Continue)
                {
                    Abort(_current, InputReplayDecision.CodeFor(action), InputReplayDecision.ReasonFor(action));
                    Commit(releaseHeld: EditorApplication.isPlaying);
                    return;
                }
            }

            FlushIfDue();
        }

        // ── Driver callbacks ────────────────────────────────────────────────

        /// <summary>Records the player frame the replay clock is anchored to.</summary>
        internal static void ReportFirstFrame(int baseFrame, string updateMode)
        {
            if (_current == null) return;
            _current.baseFrame = baseFrame;
            _current.updateMode = updateMode ?? "";
            MarkDirty();
        }

        /// <summary>
        /// Records the outcome of one event. A failure does not abort the replay: stopping would
        /// strand whatever is already held and lose the frames that would have worked.
        /// </summary>
        internal static void ReportApplied(
            int index, int relativeFrame, int unityFrame, bool late, string control, string error)
        {
            if (_current == null || index < 0 || index >= _current.events.Count) return;

            var result = _current.events[index];
            result.frame = relativeFrame;
            result.unityFrame = unityFrame;
            result.late = late;
            result.control = control ?? "";

            if (string.IsNullOrEmpty(error))
            {
                result.status = InputReplayEventStatus.Applied;
                _current.appliedCount++;
            }
            else
            {
                result.status = InputReplayEventStatus.Failed;
                result.error = error;
                _current.failedCount++;
            }

            if (late) _current.lateCount++;
            _current.lastObservedFrame = relativeFrame;
            MarkDirty();
        }

        /// <summary>Finishes a replay whose every event has been handed out.</summary>
        internal static void NotifyComplete()
        {
            if (_current == null || !_current.IsActive) return;

            _current.state = InputReplayState.Completed;
            Finish(_current);
            // A press with no scheduled release is meant to stay held, matching the immediate
            // perform endpoint, so nothing is released here.
            Commit(releaseHeld: false);
        }

        // ── Internals ───────────────────────────────────────────────────────

        private static void Abort(InputReplayRecord record, string code, string reason)
        {
            if (record == null || !record.IsActive) return;

            record.state = InputReplayState.Aborted;
            record.abortCode = code ?? "";
            record.abortReason = reason ?? "";
            Finish(record);
        }

        private static void Finish(InputReplayRecord record)
        {
            record.finishedAt = Timestamp();
            record.lifecycleGenerationAtFinish = UnionAirSession.Generation;

            DateTime started;
            DateTime finished;
            if (DateTime.TryParse(record.startedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out started) &&
                DateTime.TryParse(record.finishedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out finished))
                record.durationSeconds = (finished - started).TotalSeconds;
        }

        private static void Commit(bool releaseHeld)
        {
            if (_current == null) return;

            _latest = _current;
            FlushNow();
            TryWrite(InputReplayStore.LatestPath, _current);
            UnionAirInputReplayGate.End(_current.id);

            if (_driver != null) _driver.Stop(releaseHeld);
        }

        private static void MarkDirty() => _dirty = true;

        private static void FlushIfDue()
        {
            if (!_dirty) return;

            var now = EditorApplication.timeSinceStartup;
            var interval = _current != null && _current.eventCount > LargeReplayEventCount
                ? LargeReplayFlushIntervalSeconds
                : DefaultFlushIntervalSeconds;

            if (now - _lastFlushAt < interval) return;
            FlushNow();
        }

        private static bool FlushNow()
        {
            if (_current == null) return false;

            var written = TryWrite(InputReplayStore.CurrentPath, _current);
            _dirty = false;
            _lastFlushAt = EditorApplication.timeSinceStartup;
            return written;
        }

        private static bool TryWrite(string path, InputReplayRecord record)
        {
            try
            {
                InputReplayStore.Write(path, record);
                _flushErrorLogged = false;
                return true;
            }
            catch (Exception ex)
            {
                // One warning per failing streak: a locked artifact directory would otherwise
                // fill the console at the flush interval.
                if (_flushErrorLogged) return false;
                _flushErrorLogged = true;
                Debug.LogWarning($"[UnionAir] Could not persist the input replay record: {ex.Message}");
                return false;
            }
        }

        private static string Timestamp()
            => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
    }
}
