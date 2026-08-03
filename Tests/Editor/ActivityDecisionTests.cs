using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the activity state machine: which activity is blamed when several are running, and
    /// what a blocked request is told.
    /// </summary>
    /// <remarks>
    /// These decisions are the part of activity coordination that can be exercised without a live
    /// Editor. Starting and ending activities goes through <c>SessionState</c>, and observing Play
    /// mode and asset updating goes through <c>EditorApplication</c>; both are verified by hand.
    /// </remarks>
    internal sealed class ActivityDecisionTests
    {
        private static UnionAirActivityRecord Record(UnionAirActivity activity, string source = null, string id = null)
            => new UnionAirActivityRecord(activity, source, id);

        [Test]
        public void SelectBlocking_ReturnsNoneWhenNothingIsDeclared()
        {
            var active = new List<UnionAirActivityRecord> { Record(UnionAirActivity.Compile) };
            Assert.IsFalse(
                UnionAirActivityDecision.SelectBlocking(UnionAirActivity.None, active).IsActive);
        }

        [Test]
        public void SelectBlocking_IgnoresActivitiesTheEndpointDidNotDeclare()
        {
            var active = new List<UnionAirActivityRecord> { Record(UnionAirActivity.AssetUpdate) };
            Assert.IsFalse(
                UnionAirActivityDecision.SelectBlocking(UnionAirActivity.Compile, active).IsActive);
        }

        [Test]
        public void SelectBlocking_ReturnsTheDeclaredActivityWithItsOwner()
        {
            var active = new List<UnionAirActivityRecord>
            {
                Record(UnionAirActivity.Compile, "unionAir", "c-1")
            };

            var blocking = UnionAirActivityDecision.SelectBlocking(
                UnionAirActivity.Compile | UnionAirActivity.Build, active);

            Assert.AreEqual(UnionAirActivity.Compile, blocking.Activity);
            Assert.AreEqual("unionAir", blocking.Source);
            Assert.AreEqual("c-1", blocking.Id);
        }

        [Test]
        public void SelectBlocking_PrefersTheMostExclusiveActivity()
        {
            // A build runs its own compilation, so blaming the compilation would tell a client to
            // wait for the wrong thing and to retry far too early.
            var active = new List<UnionAirActivityRecord>
            {
                Record(UnionAirActivity.Compile, "build", "c-2"),
                Record(UnionAirActivity.Build, "unionAir", "b-1")
            };

            var blocking = UnionAirActivityDecision.SelectBlocking(
                UnionAirActivity.Compile | UnionAirActivity.Build, active);

            Assert.AreEqual(UnionAirActivity.Build, blocking.Activity);
            Assert.AreEqual("b-1", blocking.Id);
        }

        [Test]
        public void SelectCurrent_ReportsNothingWhenIdle()
        {
            Assert.IsFalse(
                UnionAirActivityDecision.SelectCurrent(new List<UnionAirActivityRecord>()).IsActive);
            Assert.IsFalse(UnionAirActivityDecision.SelectCurrent(null).IsActive);
        }

        [Test]
        public void SelectCurrent_ReportsTheHighestPriorityActivity()
        {
            var active = new List<UnionAirActivityRecord>
            {
                Record(UnionAirActivity.AssetUpdate),
                Record(UnionAirActivity.TestRun, "unionAir", "t-1"),
                Record(UnionAirActivity.PlayMode)
            };

            Assert.AreEqual(
                UnionAirActivity.TestRun,
                UnionAirActivityDecision.SelectCurrent(active).Activity);
        }

        [Test]
        public void RouterMask_ExcludesThePoliciesEnforcedElsewhere()
        {
            // Play mode and test runs keep their own pipeline stages, which have request-level
            // behavior a mask cannot express. Folding them in here would double-reject them and
            // change which status code a disabled category returns during a test run.
            Assert.AreEqual(
                UnionAirActivity.None,
                UnionAirActivityDecision.RouterMask & UnionAirActivity.PlayMode);
            Assert.AreEqual(
                UnionAirActivity.None,
                UnionAirActivityDecision.RouterMask & UnionAirActivity.TestRun);
            Assert.AreEqual(
                UnionAirActivity.Build,
                UnionAirActivityDecision.RouterMask & UnionAirActivity.Build);
        }

        [Test]
        public void IsDebris_IsFalseWhenNothingIsDeclared()
        {
            Assert.IsFalse(UnionAirActivityDecision.IsDebris(false, "", null, false));
            Assert.IsFalse(UnionAirActivityDecision.IsDebris(false, "b-1", "b-1", true));
        }

        [Test]
        public void IsDebris_IsFalseForTheLiveRecordThatOwnsIt()
        {
            Assert.IsFalse(UnionAirActivityDecision.IsDebris(true, "b-1", "b-1", true));
        }

        [Test]
        public void IsDebris_IsTrueWhenNoRecordWasRestored()
        {
            Assert.IsTrue(UnionAirActivityDecision.IsDebris(true, "b-1", null, false));
            Assert.IsTrue(UnionAirActivityDecision.IsDebris(true, "b-1", "", false));
        }

        [Test]
        public void IsDebris_IsTrueForATerminalRecordEvenWhenTheIdMatches()
        {
            // The flag is released last, after the record reaches its terminal state. A terminal
            // record still paired with a set flag means the release did not happen, and nothing
            // else will do it. This is the case two of the three hand-written copies of this
            // predicate got wrong: one omitted the test, the other inverted it.
            Assert.IsTrue(UnionAirActivityDecision.IsDebris(true, "b-1", "b-1", false));
        }

        [Test]
        public void IsDebris_IsTrueWhenAnotherRecordOwnsTheFlag()
        {
            Assert.IsTrue(UnionAirActivityDecision.IsDebris(true, "b-2", "b-1", true));
        }

        [Test]
        public void IsDebris_TreatsAnUnnamedFlagAsUnowned()
        {
            // A flag with no id cannot be owned by a record that has one.
            Assert.IsTrue(UnionAirActivityDecision.IsDebris(true, "", "b-1", true));
            Assert.IsTrue(UnionAirActivityDecision.IsDebris(true, null, "b-1", true));
        }

        [Test]
        public void IsDebrisForOwner_IsFalseForAFlagAnotherSourceOwns()
        {
            // The shape of an adopted test run: declared, no id, and no record behind it, which is
            // exactly what IsDebris reports as debris. Releasing it would end the activity for a
            // run that is still going, and a PlayMode run reloads the domain, so the service
            // re-initializes in the middle of one.
            Assert.IsTrue(UnionAirActivityDecision.IsDebris(true, "", null, false));
            Assert.IsFalse(
                UnionAirActivityDecision.IsDebrisForOwner(true, "external", "unionAir", "", null, false));
        }

        [Test]
        public void IsDebrisForOwner_IsFalseWhenNothingIsDeclared()
        {
            Assert.IsFalse(
                UnionAirActivityDecision.IsDebrisForOwner(false, "unionAir", "unionAir", "", null, false));
        }

        [Test]
        public void IsDebrisForOwner_IsFalseForTheLiveRecordThatOwnsIt()
        {
            Assert.IsFalse(
                UnionAirActivityDecision.IsDebrisForOwner(true, "unionAir", "unionAir", "t-1", "t-1", true));
        }

        [Test]
        public void IsDebrisForOwner_MatchesIsDebrisWhenTheSourcesAgree()
        {
            // No record restored, a terminal record, and a record that is not the one the flag
            // names: the three ways an owned flag outlives what it stood for.
            Assert.IsTrue(
                UnionAirActivityDecision.IsDebrisForOwner(true, "unionAir", "unionAir", "t-1", null, false));
            Assert.IsTrue(
                UnionAirActivityDecision.IsDebrisForOwner(true, "unionAir", "unionAir", "t-1", "t-1", false));
            Assert.IsTrue(
                UnionAirActivityDecision.IsDebrisForOwner(true, "unionAir", "unionAir", "t-2", "t-1", true));
        }

        [Test]
        public void IsDebrisForOwner_TreatsAMissingSourceAsUnowned()
        {
            // A flag that names no source is not the caller's to release.
            Assert.IsFalse(
                UnionAirActivityDecision.IsDebrisForOwner(true, null, "unionAir", "t-1", null, false));
            Assert.IsFalse(
                UnionAirActivityDecision.IsDebrisForOwner(true, "", "unionAir", "t-1", null, false));
        }

        [Test]
        public void RejectionJson_DerivesAMessageFromTheActivity()
        {
            var json = UnionAirActivityDecision.RejectionJson(
                Record(UnionAirActivity.Build, "unionAir", "b-1"));

            StringAssert.Contains("\"error\":\"This endpoint cannot be used while a player build is active.\"", json);
            StringAssert.Contains("\"activeActivity\":{\"activity\":\"build\",\"source\":\"unionAir\",\"id\":\"b-1\"}", json);
        }

        [Test]
        public void RejectionJson_KeepsTheCallerMessageWhenOneIsSupplied()
        {
            var json = UnionAirActivityDecision.RejectionJson(
                Record(UnionAirActivity.AssetUpdate), "Compilation cannot be requested.");

            StringAssert.Contains("\"error\":\"Compilation cannot be requested.\"", json);
        }

        [Test]
        public void RejectionJson_KeepsTheLegacyActiveTestRunObject()
        {
            // Shipped and documented; clients read it. The unified object is added beside it
            // rather than replacing it.
            var json = UnionAirActivityDecision.RejectionJson(
                Record(UnionAirActivity.TestRun, "unionAir", "t-1"));

            StringAssert.Contains("\"activeTestRun\":{\"source\":\"unionAir\",\"id\":\"t-1\"}", json);
        }

        [Test]
        public void RejectionJson_ReportsAnUnnamedOwnerAsNull()
        {
            // An adopted external run has no id a client could poll; an empty string would invite
            // exactly that mistake.
            var json = UnionAirActivityDecision.RejectionJson(
                Record(UnionAirActivity.TestRun, "external", null));

            StringAssert.Contains("\"activeTestRun\":{\"source\":\"external\",\"id\":null}", json);
        }

        [Test]
        public void AppendActivity_WritesNullWhenIdle()
        {
            var sb = new StringBuilder();
            UnionAirActivityDecision.AppendActivity(sb, UnionAirActivityRecord.None);
            Assert.AreEqual("null", sb.ToString());
        }

        [Test]
        public void AppendActivityArray_WritesNamesInPriorityOrder()
        {
            var sb = new StringBuilder();
            UnionAirActivityDecision.AppendActivityArray(
                sb,
                UnionAirActivity.Compile | UnionAirActivity.Build | UnionAirActivity.TestRun);

            Assert.AreEqual("[\"build\",\"testRun\",\"compile\"]", sb.ToString());
        }

        [Test]
        public void AppendActivityArray_WritesAnEmptyArrayForNone()
        {
            var sb = new StringBuilder();
            UnionAirActivityDecision.AppendActivityArray(sb, UnionAirActivity.None);
            Assert.AreEqual("[]", sb.ToString());
        }

        [Test]
        public void ActivityNames_AreStableAndDistinct()
        {
            var seen = new HashSet<string>();
            foreach (var activity in UnionAirActivityNames.Priority)
            {
                var name = UnionAirActivityNames.Name(activity);
                Assert.IsNotEmpty(name);
                Assert.AreNotEqual("none", name);
                Assert.IsTrue(seen.Add(name), "Duplicate activity name: " + name);
            }

            Assert.AreEqual("none", UnionAirActivityNames.Name(UnionAirActivity.None));
        }
    }
}
