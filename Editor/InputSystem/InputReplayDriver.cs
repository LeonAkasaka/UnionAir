using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Drives a scheduled input replay from inside the Input System's own update.
    /// </summary>
    /// <remarks>
    /// <c>onBeforeUpdate</c> is raised immediately before the Input System flushes the event queue
    /// for the update that is about to run, so an event queued here is consumed by that same
    /// update. The dynamic input update runs in <c>EarlyUpdate</c> — after <c>Time.frameCount</c>
    /// has advanced and before any <c>MonoBehaviour.Update</c> — which is what makes the frame
    /// number read here the same frame number the game will report, and makes "frame N" mean the
    /// frame on which the game observes the input.
    /// <para>
    /// This is preferred over inserting a <c>PlayerLoopSystem</c>: it gives the same instant
    /// without mutating a global structure that other packages also edit and that would have to be
    /// restored on play mode exit, domain reload, and quit. Driving from
    /// <c>EditorApplication.update</c> instead would only observe the frame counter from outside
    /// the player loop, which is the ±1 jitter the pointer sequence has to work around.
    /// </para>
    /// </remarks>
    internal sealed class InputReplayDriver : IInputReplayDriver
    {
        internal static readonly InputReplayDriver Instance = new InputReplayDriver();

        private readonly List<InputReplayDueEvent> _due = new List<InputReplayDueEvent>();

        private InputReplayRecord _record;
        private InputReplaySchedule _schedule;
        private InputUpdateType _playerUpdateType;
        private string _updateModeName = "";
        private int _baseFrame = -1;
        private bool _running;

        public bool TryBegin(InputReplayRecord record, out string error)
        {
            error = null;

            if (record == null || record.inputs == null || record.inputs.Count == 0)
            {
                error = "The replay has no events.";
                return false;
            }

            if (!TryResolvePlayerUpdateType(out _playerUpdateType, out _updateModeName, out error))
                return false;

            // A pointer sequence owns the virtual mouse across several frames and relies on the
            // player loop consuming its own queued events; the two cannot share the devices.
            if (PlayModeInputHandler.HasActivePointerSequence)
            {
                error = "A pointer operation is in progress.";
                return false;
            }

            _record = record;
            _schedule = new InputReplaySchedule(record.inputs);
            _baseFrame = -1;
            _running = true;

            InputSystem.onBeforeUpdate -= OnBeforeUpdate;
            InputSystem.onBeforeUpdate += OnBeforeUpdate;
            return true;
        }

        public void Stop(bool releaseHeld)
        {
            InputSystem.onBeforeUpdate -= OnBeforeUpdate;
            _running = false;
            _record = null;
            _schedule = null;
            _baseFrame = -1;
            _due.Clear();

            if (releaseHeld) PlayModeInputHandler.ReleaseAllHeldInput();
        }

        private void OnBeforeUpdate()
        {
            if (!_running || _record == null || _schedule == null) return;
            if (!EditorApplication.isPlaying) return;

            // onBeforeUpdate also fires for Editor updates, which do not advance player frames.
            if (InputState.currentUpdateType != _playerUpdateType) return;

            if (_baseFrame < 0)
            {
                // Anchoring on the first qualifying update makes relative frame 0 the first
                // player frame by construction, so it can never be reported late.
                _baseFrame = Time.frameCount;
                InputReplayService.ReportFirstFrame(_baseFrame, _updateModeName);
            }

            var relativeFrame = Time.frameCount - _baseFrame;
            _schedule.TakeDue(relativeFrame, _due);

            if (_due.Count > 0)
                PlayModeInputHandler.ApplyReplayFrame(_record.inputs, _due, relativeFrame, Time.frameCount);

            if (_schedule.IsComplete)
                InputReplayService.NotifyComplete();
        }

        private static bool TryResolvePlayerUpdateType(
            out InputUpdateType updateType, out string modeName, out string error)
        {
            error = null;

            switch (InputSystem.settings.updateMode)
            {
                case InputSettings.UpdateMode.ProcessEventsInDynamicUpdate:
                    updateType = InputUpdateType.Dynamic;
                    modeName = "dynamic";
                    return true;
                case InputSettings.UpdateMode.ProcessEventsInFixedUpdate:
                    // A frame can run zero or several fixed updates, so "frame" is looser here.
                    // Events still land on the first input update of each player frame, and any
                    // slippage is reported through the per-event late flag.
                    updateType = InputUpdateType.Fixed;
                    modeName = "fixed";
                    return true;
                default:
                    updateType = InputUpdateType.None;
                    modeName = "manual";
                    error = "The Input System processes events manually, which a replay cannot time. " +
                            "Change Update Mode in Project Settings > Input System Package.";
                    return false;
            }
        }
    }
}
