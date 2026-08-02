using NUnit.Framework;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the record shape and id handling behind the build target switch endpoints.
    /// </summary>
    /// <remarks>
    /// The switch itself reimports the project and can end in a domain reload, so it is verified by
    /// hand. What is covered here is the record a client reads afterwards — the part that has to
    /// still make sense on the far side of a reload it did not witness.
    /// </remarks>
    internal sealed class BuildTargetSwitchTests
    {
        private static BuildTargetSwitchRecord Record(string state, string requested, string active)
            => new BuildTargetSwitchRecord
            {
                id = "t-1",
                state = state,
                requestedTarget = requested,
                requestedTargetGroup = "Standalone",
                requestedNamedBuildTarget = "Standalone",
                previousTarget = "StandaloneWindows",
                activeTarget = active,
            };

        [Test]
        public void IsActive_CoversTheStatesThatCrossADomainReload()
        {
            // 'switching' is the state a record is in while the reimport runs, which is exactly
            // when the reload can happen. Treating it as terminal would strand the record.
            Assert.IsTrue(Record("queued", "StandaloneWindows64", "").IsActive);
            Assert.IsTrue(Record("switching", "StandaloneWindows64", "").IsActive);

            Assert.IsFalse(Record("completed", "StandaloneWindows64", "StandaloneWindows64").IsActive);
            Assert.IsFalse(Record("failed", "StandaloneWindows64", "StandaloneWindows").IsActive);
            Assert.IsFalse(Record("aborted", "StandaloneWindows64", "StandaloneWindows").IsActive);
        }

        [Test]
        public void ToApiJson_ReportsBothEndsOfTheSwitch()
        {
            var json = Record("completed", "StandaloneWindows64", "StandaloneWindows64").ToApiJson();

            StringAssert.Contains("\"state\":\"completed\"", json);
            StringAssert.Contains("\"requestedTarget\":\"StandaloneWindows64\"", json);
            StringAssert.Contains("\"previousTarget\":\"StandaloneWindows\"", json);
            StringAssert.Contains("\"activeTarget\":\"StandaloneWindows64\"", json);
            StringAssert.Contains("\"statusUrl\":\"/api/build/target/t-1\"", json);
        }

        [Test]
        public void ToApiJson_ReportsAbsentValuesAsNull()
        {
            // A queued record has no outcome yet, and an empty string would read as one.
            var json = Record("queued", "StandaloneWindows64", "").ToApiJson();
            StringAssert.Contains("\"activeTarget\":null", json);
            StringAssert.Contains("\"error\":null", json);
            StringAssert.Contains("\"finishedAt\":null", json);
        }

        [Test]
        public void NewId_IsAcceptedByTheRecordPathValidator()
        {
            var id = BuildTargetSwitchService.NewId();
            StringAssert.StartsWith("t-", id);
            Assert.IsTrue(CompileMessageParser.IsValidId(id), id);

            string path;
            Assert.IsTrue(BuildTargetSwitchService.TryGetRecordPath(id, out path));
        }

        [Test]
        public void TryGetRecordPath_RejectsIdsThatEscapeTheRecordsDirectory()
        {
            string path;
            Assert.IsFalse(BuildTargetSwitchService.TryGetRecordPath("../escape", out path));
            Assert.IsFalse(BuildTargetSwitchService.TryGetRecordPath("NUL", out path));
            Assert.IsFalse(BuildTargetSwitchService.TryGetRecordPath("", out path));
        }

        [Test]
        public void Find_RejectsAnInvalidIdWithoutTouchingDisk()
        {
            Assert.IsNull(BuildTargetSwitchService.Find("../escape"));
            Assert.IsNull(BuildTargetSwitchService.Find(null));
        }

        [Test]
        public void Catalog_ReportsTheActiveTargetAsInstalled()
        {
            // A switch request checks this before doing anything, and an Editor that cannot build
            // its own active target would make every later check meaningless.
            var target = EditorUserBuildSettings.activeBuildTarget;
            Assert.IsTrue(
                BuildTargetCatalog.IsInstalled(BuildTargetCatalog.GroupOf(target), target),
                "The active build target reports its platform module as missing: " + target);
        }
    }
}
