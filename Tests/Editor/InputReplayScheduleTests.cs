using System.Collections.Generic;
using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the frame model of a replay: which events come due on an observed frame, in what
    /// order, and what counts as late.
    /// </summary>
    /// <remarks>
    /// This is the whole timing contract expressed without Play mode. The driver that consumes it
    /// only reads a frame counter and applies what it is handed, so a schedule that is right here
    /// is right in the Editor too.
    /// </remarks>
    internal sealed class InputReplayScheduleTests
    {
        private static InputReplaySchedule Schedule(params int[] frames)
        {
            var events = new List<InputReplayEventSpec>();
            foreach (var frame in frames)
                events.Add(new InputReplayEventSpec { frame = frame, type = InputReplayEventType.Perform });
            return new InputReplaySchedule(events);
        }

        private static List<int> Indices(List<InputReplayDueEvent> due)
        {
            var result = new List<int>();
            foreach (var e in due) result.Add(e.Index);
            return result;
        }

        [Test]
        public void TakeDue_ReturnsNothingBeforeTheScheduledFrame()
        {
            var schedule = Schedule(5);
            var due = new List<InputReplayDueEvent>();

            schedule.TakeDue(0, due);
            CollectionAssert.IsEmpty(due);
            schedule.TakeDue(4, due);
            CollectionAssert.IsEmpty(due);
            Assert.IsFalse(schedule.IsComplete);
        }

        [Test]
        public void TakeDue_ReturnsTheEventOnItsScheduledFrame()
        {
            var schedule = Schedule(5);
            var due = new List<InputReplayDueEvent>();

            schedule.TakeDue(5, due);
            CollectionAssert.AreEqual(new[] { 0 }, Indices(due));
            Assert.IsFalse(due[0].Late);
            Assert.IsTrue(schedule.IsComplete);
        }

        [Test]
        public void TakeDue_ReturnsSameFrameEventsInRequestOrder()
        {
            // A chord: three presses on one frame must reach the device snapshot in the order
            // the caller wrote them.
            var schedule = Schedule(2, 2, 2);
            var due = new List<InputReplayDueEvent>();

            schedule.TakeDue(2, due);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, Indices(due));
        }

        [Test]
        public void TakeDue_OrdersUnsortedFramesByFrame()
        {
            var schedule = Schedule(10, 2, 6);
            var due = new List<InputReplayDueEvent>();

            schedule.TakeDue(6, due);
            CollectionAssert.AreEqual(new[] { 1, 2 }, Indices(due));
            schedule.TakeDue(10, due);
            CollectionAssert.AreEqual(new[] { 0 }, Indices(due));
        }

        [Test]
        public void TakeDue_ReturnsSkippedEventsOnTheNextObservedFrame()
        {
            // The player loop jumped from frame 1 to frame 5; the frame-3 event is not lost.
            var schedule = Schedule(0, 3, 5);
            var due = new List<InputReplayDueEvent>();

            schedule.TakeDue(0, due);
            CollectionAssert.AreEqual(new[] { 0 }, Indices(due));

            schedule.TakeDue(5, due);
            CollectionAssert.AreEqual(new[] { 1, 2 }, Indices(due));
            Assert.IsTrue(due[0].Late, "The frame-3 event was observed at frame 5 and is late.");
            Assert.IsFalse(due[1].Late, "The frame-5 event arrived on time.");
        }

        [Test]
        public void TakeDue_ClearsTheDestinationBetweenCalls()
        {
            var schedule = Schedule(0, 1);
            var due = new List<InputReplayDueEvent>();

            schedule.TakeDue(0, due);
            schedule.TakeDue(1, due);
            CollectionAssert.AreEqual(new[] { 1 }, Indices(due));
        }

        [Test]
        public void TakeDue_NeverDropsAnEventAcrossAnIrregularFrameSequence()
        {
            var frames = new[] { 0, 1, 1, 4, 9, 9, 12, 30 };
            var schedule = Schedule(frames);
            var due = new List<InputReplayDueEvent>();
            var seen = new List<int>();

            // A deliberately ragged observation sequence: repeats, gaps, and a large jump.
            foreach (var observed in new[] { 0, 0, 2, 3, 3, 8, 10, 11, 40 })
            {
                schedule.TakeDue(observed, due);
                seen.AddRange(Indices(due));
            }

            Assert.IsTrue(schedule.IsComplete);
            seen.Sort();
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, seen);
        }

        [Test]
        public void PendingCount_TracksWhatIsLeft()
        {
            var schedule = Schedule(0, 5);
            var due = new List<InputReplayDueEvent>();

            Assert.AreEqual(2, schedule.Count);
            Assert.AreEqual(2, schedule.PendingCount);

            schedule.TakeDue(0, due);
            Assert.AreEqual(1, schedule.PendingCount);

            schedule.TakeDue(5, due);
            Assert.AreEqual(0, schedule.PendingCount);
            Assert.IsTrue(schedule.IsComplete);
        }

        [Test]
        public void IsComplete_IsTrueForAnEmptySchedule()
        {
            Assert.IsTrue(new InputReplaySchedule(new List<InputReplayEventSpec>()).IsComplete);
        }
    }
}
