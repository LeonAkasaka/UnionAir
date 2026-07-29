using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers when a replay is abandoned and why.
    /// </summary>
    /// <remarks>
    /// The watchdog is what turns a stuck Editor into a reported failure instead of a client that
    /// polls forever, so the boundaries — and the deliberate exemption for a paused Editor — are
    /// worth pinning down.
    /// </remarks>
    internal sealed class InputReplayDecisionTests
    {
        [Test]
        public void DecideWatchdog_KeepsAQueuedReplayWithinTheGrace()
        {
            Assert.AreEqual(
                InputReplayWatchdogAction.Continue,
                InputReplayDecision.DecideWatchdog(
                    InputReplayState.Queued, false, false,
                    InputReplayDecision.QueuedGraceSeconds - 1.0, 0.0, false));
        }

        [Test]
        public void DecideWatchdog_AbortsAQueuedReplayAfterTheGrace()
        {
            Assert.AreEqual(
                InputReplayWatchdogAction.AbortPlayModeNeverEntered,
                InputReplayDecision.DecideWatchdog(
                    InputReplayState.Queued, false, false,
                    InputReplayDecision.QueuedGraceSeconds + 1.0, 0.0, false));
        }

        [Test]
        public void DecideWatchdog_AbortsARunningReplayWhenPlayModeEnded()
        {
            Assert.AreEqual(
                InputReplayWatchdogAction.AbortPlayModeExited,
                InputReplayDecision.DecideWatchdog(InputReplayState.Running, false, false, 0.0, 0.0, true));
        }

        [Test]
        public void DecideWatchdog_AbortsARunningReplayWhenFramesStall()
        {
            Assert.AreEqual(
                InputReplayWatchdogAction.AbortFramesStalled,
                InputReplayDecision.DecideWatchdog(
                    InputReplayState.Running, true, false, 0.0,
                    InputReplayDecision.StallGraceSeconds + 1.0, true));
        }

        [Test]
        public void DecideWatchdog_WaitsLongerForTheVeryFirstFrame()
        {
            // Entering Play mode does not mean frames are flowing yet: scene initialization and
            // first-frame shader warmup can hold the counter still for seconds. Observed in a
            // real project, where the ordinary stall grace abandoned a healthy replay.
            Assert.AreEqual(
                InputReplayWatchdogAction.Continue,
                InputReplayDecision.DecideWatchdog(
                    InputReplayState.Running, true, false, 0.0,
                    InputReplayDecision.StallGraceSeconds + 1.0, false));
        }

        [Test]
        public void DecideWatchdog_AbortsWhenEvenTheFirstFrameNeverArrives()
        {
            Assert.AreEqual(
                InputReplayWatchdogAction.AbortFramesStalled,
                InputReplayDecision.DecideWatchdog(
                    InputReplayState.Running, true, false, 0.0,
                    InputReplayDecision.FirstFrameGraceSeconds + 1.0, false));
        }

        [Test]
        public void DecideWatchdog_TreatsAnAdvancingFrameCounterAsHealthy()
        {
            // Callers reset the elapsed time whenever the frame counter moves, so a slow but
            // advancing player loop must never trip the stall detector.
            Assert.AreEqual(
                InputReplayWatchdogAction.Continue,
                InputReplayDecision.DecideWatchdog(
                    InputReplayState.Running, true, false, 600.0,
                    InputReplayDecision.StallGraceSeconds - 0.1, true));
        }

        [Test]
        public void DecideWatchdog_DoesNotAbortWhilePaused()
        {
            // Pausing is how a caller single-steps through a replay; frames legitimately stop.
            Assert.AreEqual(
                InputReplayWatchdogAction.Continue,
                InputReplayDecision.DecideWatchdog(
                    InputReplayState.Running, true, true, 0.0,
                    InputReplayDecision.StallGraceSeconds * 100.0, true));
        }

        [TestCase("completed")]
        [TestCase("aborted")]
        public void DecideWatchdog_IgnoresTerminalRecords(string state)
        {
            Assert.AreEqual(
                InputReplayWatchdogAction.Continue,
                InputReplayDecision.DecideWatchdog(state, false, false, 10000.0, 10000.0, true));
        }

        [Test]
        public void CodeFor_IsEmptyOnlyWhenNotAborting()
        {
            Assert.AreEqual("", InputReplayDecision.CodeFor(InputReplayWatchdogAction.Continue));
            Assert.AreEqual("playModeNeverEntered", InputReplayDecision.CodeFor(InputReplayWatchdogAction.AbortPlayModeNeverEntered));
            Assert.AreEqual("framesStalled", InputReplayDecision.CodeFor(InputReplayWatchdogAction.AbortFramesStalled));
            Assert.AreEqual("playModeExited", InputReplayDecision.CodeFor(InputReplayWatchdogAction.AbortPlayModeExited));
        }

        [Test]
        public void ReasonFor_ExplainsHowToFixAStall()
        {
            var reason = InputReplayDecision.ReasonFor(InputReplayWatchdogAction.AbortFramesStalled);
            StringAssert.Contains("Game view", reason);
            StringAssert.Contains("Background Behavior", reason);
        }
    }
}
