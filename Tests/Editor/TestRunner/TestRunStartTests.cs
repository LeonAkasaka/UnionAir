using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the order the Test Runner start transaction commits in: the record is durable and the
    /// activity is open before anything reaches the Unity Test Framework, and a dispatch that fails
    /// afterwards still releases the activity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The store and the dispatch are injected. A failed dispatch cannot be provoked from the file
    /// system, and it is the path that matters most: a record that leaves the activity open with
    /// nothing able to close it makes every endpoint blocked during a test run answer <c>409</c>
    /// for the rest of the Editor session.
    /// </para>
    /// <para>
    /// These tests run inside a test run of their own, which UnionAir has adopted or started, so
    /// the activity, the cancellation handle, and the current record are borrowed and put back.
    /// They are plain <c>[Test]</c> methods, so each completes within one Editor update and
    /// <c>TestRunnerService.Update</c> cannot observe the borrowed state.
    /// </para>
    /// </remarks>
    internal sealed class TestRunStartTests
    {
        private TestRunRecord _previousCurrent;
        private bool _gateWasActive;
        private string _gateSource;
        private string _gateRunId;
        private string _handleOwner;
        private string _handleRunId;

        [SetUp]
        public void SetUp()
        {
            _previousCurrent = TestRunnerService.Current;
            _gateWasActive = UnionAirTestRunGate.IsActive;
            _gateSource = UnionAirTestRunGate.Source;
            _gateRunId = UnionAirTestRunGate.RunId;
            _handleOwner = TestRunCancellationHandle.Owner;
            _handleRunId = TestRunCancellationHandle.StoredRunId;

            ReleaseGate();
            TestRunCancellationHandle.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ReleaseGate();
            TestRunCancellationHandle.Clear();

            if (_gateWasActive)
                UnionAirTestRunGate.Begin(_gateSource, _gateRunId);
            TestRunCancellationHandle.Set(_handleOwner, _handleRunId);
            TestRunnerService.SetCurrentForTests(_previousCurrent);
        }

        /// <summary>
        /// Closes the activity through <c>End</c> rather than <c>ClearDebris</c>: the latter is what
        /// real debris deserves, and it writes a Console warning nothing here has earned.
        /// </summary>
        private static void ReleaseGate()
        {
            if (UnionAirTestRunGate.IsActive)
                UnionAirTestRunGate.End(UnionAirTestRunGate.Source);
        }

        private static Func<TestRunRecord, bool> Rejecting(Action<TestRunRecord> observe = null)
            => record => { if (observe != null) observe(record); return false; };

        private static Func<TestRunRecord, bool> Accepting(Action<TestRunRecord> observe = null)
            => record => { if (observe != null) observe(record); return true; };

        [Test]
        public void TryStartRun_DispatchesNothingWhenTheRecordCannotBeWritten()
        {
            var dispatched = false;

            var started = TestRunnerService.TryStartRun(
                TestMode.EditMode, "editMode", new TestRunFilters(), "",
                out var runId, out var error,
                Rejecting(),
                (mode, filters) => { dispatched = true; return "framework-1"; });

            Assert.IsFalse(started);
            Assert.IsFalse(dispatched, "The run was dispatched for a record that never reached disk.");
            Assert.IsFalse(UnionAirTestRunGate.IsActive, "The activity was opened for a record that was not stored.");
            Assert.IsEmpty(runId);
            StringAssert.Contains("no run was started", error);
        }

        [Test]
        public void TryStartRun_StoresTheRecordBeforeTheRunIsDispatched()
        {
            var sequence = new List<string>();
            TestRunRecord storedRecord = null;

            var started = TestRunnerService.TryStartRun(
                TestMode.PlayMode, "playMode", new TestRunFilters(), "",
                out var runId, out var error,
                Accepting(record => { sequence.Add("save"); storedRecord = record; }),
                (mode, filters) => { sequence.Add("execute"); return "framework-1"; });

            Assert.IsTrue(started, error);
            CollectionAssert.AreEqual(new[] { "save", "execute" }, sequence);
            Assert.IsNotNull(storedRecord);
            Assert.AreEqual(runId, storedRecord.id);
            Assert.AreEqual("queued", storedRecord.state);
            Assert.AreEqual("playMode", storedRecord.mode);
        }

        [Test]
        public void TryStartRun_IssuesItsOwnRunIdAndKeepsTheFrameworkIdAsAHandle()
        {
            var started = TestRunnerService.TryStartRun(
                TestMode.EditMode, "editMode", new TestRunFilters(), "",
                out var runId, out var error,
                Accepting(),
                (mode, filters) => "framework-1");

            Assert.IsTrue(started, error);
            Assert.AreNotEqual("framework-1", runId, "The framework id was adopted as the identity.");
            Assert.IsTrue(Guid.TryParse(runId, out _), "The run id is not a GUID: " + runId);

            Assert.IsTrue(UnionAirTestRunGate.IsActive);
            Assert.AreEqual(UnionAirTestRunGate.UnionAirSource, UnionAirTestRunGate.Source);
            Assert.AreEqual(runId, UnionAirTestRunGate.RunId);

            Assert.IsTrue(TestRunCancellationHandle.TryGet(runId, out var frameworkRunId));
            Assert.AreEqual("framework-1", frameworkRunId);
        }

        [Test]
        public void TryStartRun_FinishesTheRecordAndReleasesTheActivityWhenTheDispatchThrows()
        {
            TestRunRecord stored = null;

            var started = TestRunnerService.TryStartRun(
                TestMode.EditMode, "editMode", new TestRunFilters(), "",
                out var runId, out var error,
                Accepting(record => stored = record),
                (mode, filters) => { throw new InvalidOperationException("no runner"); });

            Assert.IsFalse(started);
            Assert.IsEmpty(runId);
            StringAssert.Contains("no runner", error);

            Assert.IsNotNull(stored);
            Assert.AreEqual("aborted", stored.state, "The record was left claiming to be live.");
            Assert.IsFalse(stored.IsActive);
            StringAssert.Contains("no runner", stored.error);

            // The point of the ordering: nothing else would ever close this one.
            Assert.IsFalse(UnionAirTestRunGate.IsActive, "The activity outlived the run it was opened for.");
            Assert.IsFalse(TestRunCancellationHandle.TryGet(stored.id, out _));
        }

        [Test]
        public void TryStartRun_StoresNoHandleWhenTheFrameworkNamesNoRun()
        {
            // Cancellation is then reported as unavailable rather than sending the framework an id
            // it never issued.
            var started = TestRunnerService.TryStartRun(
                TestMode.EditMode, "editMode", new TestRunFilters(), "",
                out var runId, out var error,
                Accepting(),
                (mode, filters) => "");

            Assert.IsTrue(started, error);
            Assert.IsFalse(TestRunCancellationHandle.TryGet(runId, out _));
        }
    }
}
