namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>What the replay watchdog concluded about a record on this tick.</summary>
    internal enum InputReplayWatchdogAction
    {
        Continue,
        AbortPlayModeNeverEntered,
        AbortFramesStalled,
        AbortPlayModeExited
    }

    /// <summary>
    /// Pure lifecycle decisions for an input replay, kept apart from the Editor wiring that acts
    /// on them so the reasoning can be exercised without Play mode.
    /// </summary>
    internal static class InputReplayDecision
    {
        /// <summary>
        /// How long an armed replay may wait for Play mode before it is considered lost.
        /// Entering Play mode involves a domain reload and an asset import, so the grace is
        /// generous — matching the queued grace the compile pipeline uses.
        /// </summary>
        internal const double QueuedGraceSeconds = 30.0;

        /// <summary>
        /// How long player frames may stop advancing before a running replay is abandoned.
        /// </summary>
        /// <remarks>
        /// This detects stalled frames, not slow ones: callers refresh the elapsed time whenever
        /// the frame counter moves, so a long replay at a low frame rate never trips it.
        /// </remarks>
        internal const double StallGraceSeconds = 5.0;

        /// <summary>
        /// How long a started replay may wait for its <em>first</em> frame advance.
        /// </summary>
        /// <remarks>
        /// Entering Play mode does not mean the player loop is producing frames yet: scene
        /// initialization and first-frame shader warmup can hold the frame counter still for
        /// several seconds. Applying the ordinary stall grace during that window would abandon
        /// replays that were about to run perfectly, so the detector only tightens once the
        /// player loop has proven it is advancing.
        /// </remarks>
        internal const double FirstFrameGraceSeconds = 30.0;

        /// <summary>
        /// Decides whether a replay should keep running or be aborted.
        /// </summary>
        /// <param name="state">Current record state.</param>
        /// <param name="isPlaying">Whether the Editor is in Play mode.</param>
        /// <param name="isPaused">Whether Play mode is paused.</param>
        /// <param name="secondsSinceQueued">Seconds since the replay was armed.</param>
        /// <param name="secondsSinceFrameAdvanced">Seconds since the player frame counter last moved.</param>
        /// <param name="framesHaveAdvanced">Whether the player frame counter has moved at least once.</param>
        internal static InputReplayWatchdogAction DecideWatchdog(
            string state,
            bool isPlaying,
            bool isPaused,
            double secondsSinceQueued,
            double secondsSinceFrameAdvanced,
            bool framesHaveAdvanced)
        {
            if (state == InputReplayState.Queued)
                return secondsSinceQueued > QueuedGraceSeconds
                    ? InputReplayWatchdogAction.AbortPlayModeNeverEntered
                    : InputReplayWatchdogAction.Continue;

            if (state != InputReplayState.Running)
                return InputReplayWatchdogAction.Continue;

            if (!isPlaying)
                return InputReplayWatchdogAction.AbortPlayModeExited;

            // Pausing suspends the stall deadline on purpose: it is how a caller single-steps
            // through a replay with POST /api/editor/pause and POST /api/editor/step.
            if (isPaused)
                return InputReplayWatchdogAction.Continue;

            var grace = framesHaveAdvanced ? StallGraceSeconds : FirstFrameGraceSeconds;
            return secondsSinceFrameAdvanced > grace
                ? InputReplayWatchdogAction.AbortFramesStalled
                : InputReplayWatchdogAction.Continue;
        }

        /// <summary>Machine-readable token for an aborting action, or an empty string.</summary>
        internal static string CodeFor(InputReplayWatchdogAction action)
        {
            switch (action)
            {
                case InputReplayWatchdogAction.AbortPlayModeNeverEntered: return "playModeNeverEntered";
                case InputReplayWatchdogAction.AbortFramesStalled:        return "framesStalled";
                case InputReplayWatchdogAction.AbortPlayModeExited:       return "playModeExited";
                default: return "";
            }
        }

        /// <summary>Human-readable sentence for an aborting action, or an empty string.</summary>
        internal static string ReasonFor(InputReplayWatchdogAction action)
        {
            switch (action)
            {
                case InputReplayWatchdogAction.AbortPlayModeNeverEntered:
                    return "Play mode was not entered within " + QueuedGraceSeconds + " seconds of arming the replay.";
                case InputReplayWatchdogAction.AbortFramesStalled:
                    return "Timed out waiting for player frames to advance. Focus the Game view or check the Input System package's Background Behavior setting.";
                case InputReplayWatchdogAction.AbortPlayModeExited:
                    return "Play mode ended before the replay finished.";
                default: return "";
            }
        }
    }
}
