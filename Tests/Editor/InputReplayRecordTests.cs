using NUnit.Framework;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the replay record's API projection and its survival across a domain reload.
    /// </summary>
    /// <remarks>
    /// The record is written with <c>JsonUtility</c> so it outlives the domain reload that
    /// entering Play mode causes, but <c>JsonUtility</c> cannot emit <c>null</c> — so the API
    /// projection is hand-written, and the distinction between "not observed yet" and "observed at
    /// frame 0" only holds if that projection is right.
    /// </remarks>
    internal sealed class InputReplayRecordTests
    {
        private static InputReplayRecord Record()
        {
            var record = new InputReplayRecord { id = "ir-20260729-083741-cb2984", state = InputReplayState.Running };
            record.inputs.Add(new InputReplayEventSpec
            {
                frame = 5,
                type = InputReplayEventType.Perform,
                action = "Player/Jump",
                mode = "press"
            });
            record.eventCount = 1;
            record.events.Add(new InputReplayEventResult { index = 0 });
            return record;
        }

        [Test]
        public void ToApiJson_EmitsNullRatherThanAnEmptyAbortReason()
        {
            var json = Record().ToApiJson();
            StringAssert.Contains("\"abortReason\":null", json);
            StringAssert.Contains("\"abortCode\":null", json);
        }

        [Test]
        public void ToApiJson_EmitsNullForAnUnobservedFrame()
        {
            // -1 must not reach the client, and 0 would read as "observed on the first frame".
            var json = Record().ToApiJson();
            StringAssert.Contains("\"frame\":null", json);
            StringAssert.Contains("\"unityFrame\":null", json);
            StringAssert.Contains("\"baseFrame\":null", json);
        }

        [Test]
        public void ToApiJson_EmitsTheObservedFrameOnceApplied()
        {
            var record = Record();
            record.baseFrame = 1204;
            record.events[0].frame = 5;
            record.events[0].unityFrame = 1209;
            record.events[0].status = InputReplayEventStatus.Applied;
            record.events[0].control = "/UnionAirVirtualKeyboard/space";
            record.appliedCount = 1;

            var json = record.ToApiJson();
            StringAssert.Contains("\"frame\":5", json);
            StringAssert.Contains("\"unityFrame\":1209", json);
            StringAssert.Contains("\"baseFrame\":1204", json);
            StringAssert.Contains("\"status\":\"applied\"", json);
            StringAssert.Contains("\"control\":\"/UnionAirVirtualKeyboard/space\"", json);
            StringAssert.Contains("\"error\":null", json);
        }

        [Test]
        public void ToApiJson_ReportsLateAndFailedCounts()
        {
            var record = Record();
            record.lateCount = 2;
            record.failedCount = 3;

            var json = record.ToApiJson();
            StringAssert.Contains("\"lateCount\":2", json);
            // A replay completes even when every event failed, so the count must be visible.
            StringAssert.Contains("\"failedCount\":3", json);
        }

        [Test]
        public void ToApiJson_EscapesAFailureMessage()
        {
            var record = Record();
            record.events[0].status = InputReplayEventStatus.Failed;
            record.events[0].error = "Action not found: \"Player/Jump\"";

            StringAssert.Contains("\\\"Player/Jump\\\"", record.ToApiJson());
        }

        [Test]
        public void IsActive_CoversQueuedAndRunningOnly()
        {
            Assert.IsTrue(new InputReplayRecord { state = InputReplayState.Queued }.IsActive);
            Assert.IsTrue(new InputReplayRecord { state = InputReplayState.Running }.IsActive);
            Assert.IsFalse(new InputReplayRecord { state = InputReplayState.Completed }.IsActive);
            Assert.IsFalse(new InputReplayRecord { state = InputReplayState.Aborted }.IsActive);
        }

        [Test]
        public void Record_RoundTripsThroughJsonUtility()
        {
            // The domain-reload contract: the schedule and its results must both survive.
            var original = Record();
            original.inputs.Add(new InputReplayEventSpec
            {
                frame = 20,
                type = InputReplayEventType.Pointer,
                mode = "press",
                button = "left",
                pointKind = InputReplayPointKind.Normalized,
                pointX = 0.5f,
                pointY = 0.25f,
                originTopLeft = true
            });
            original.events.Add(new InputReplayEventResult { index = 1, late = true });

            var restored = JsonUtility.FromJson<InputReplayRecord>(JsonUtility.ToJson(original));

            Assert.AreEqual(original.id, restored.id);
            Assert.AreEqual(original.state, restored.state);
            Assert.AreEqual(2, restored.inputs.Count);
            Assert.AreEqual("Player/Jump", restored.inputs[0].action);
            Assert.AreEqual(InputReplayPointKind.Normalized, restored.inputs[1].pointKind);
            Assert.AreEqual(0.5f, restored.inputs[1].pointX, 1e-6f);
            Assert.IsTrue(restored.inputs[1].originTopLeft);
            Assert.AreEqual(2, restored.events.Count);
            Assert.IsTrue(restored.events[1].late);
        }
    }
}
