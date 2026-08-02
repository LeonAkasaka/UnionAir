using NUnit.Framework;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the request validation and output naming behind <c>POST /api/builds</c>.
    /// </summary>
    /// <remarks>
    /// The build itself needs a live Editor and roughly a minute per run, so it is verified by
    /// hand. What is covered here is everything that decides <em>what</em> gets built and
    /// <em>where</em> — the parts where a mistake would produce a wrong artifact rather than a
    /// visible failure.
    /// </remarks>
    internal sealed class BuildRequestTests
    {
        private static BuildRequestOptions Defaults(bool development = false)
            => new BuildRequestOptions { development = development };

        [Test]
        public void TryParse_UsesProjectDefaultsForAnEmptyBody()
        {
            BuildRequestOptions options;
            string error;
            Assert.IsTrue(BuildRequestParser.TryParse("", Defaults(true), out options, out error));
            Assert.IsTrue(options.development);
            Assert.IsFalse(options.clean);
            Assert.IsNull(error);
        }

        [Test]
        public void TryParse_LetsTheRequestOverrideAProjectDefault()
        {
            BuildRequestOptions options;
            string error;
            Assert.IsTrue(BuildRequestParser.TryParse(
                "{\"development\":false}", Defaults(true), out options, out error));
            Assert.IsFalse(options.development);
        }

        [Test]
        public void TryParse_ReadsTheAllowlistedOptions()
        {
            BuildRequestOptions options;
            string error;
            Assert.IsTrue(BuildRequestParser.TryParse(
                "{\"development\":true,\"allowDebugging\":true,\"connectProfiler\":true," +
                "\"deepProfiling\":true,\"waitForPlayerConnection\":true,\"clean\":true,\"strictMode\":true}",
                Defaults(), out options, out error));

            Assert.IsTrue(options.development);
            Assert.IsTrue(options.allowDebugging);
            Assert.IsTrue(options.connectProfiler);
            Assert.IsTrue(options.deepProfiling);
            Assert.IsTrue(options.waitForPlayerConnection);
            Assert.IsTrue(options.clean);
            Assert.IsTrue(options.strictMode);
        }

        [Test]
        public void TryParse_RejectsAPresentFieldThatIsNotABoolean()
        {
            // GetBool cannot distinguish absence from a wrong type on its own, so a string value
            // would otherwise fall through to the project default and produce a build the caller
            // did not ask for, with nothing in the response saying so.
            BuildRequestOptions options;
            string error;
            Assert.IsFalse(BuildRequestParser.TryParse(
                "{\"development\":\"true\"}", Defaults(), out options, out error));
            StringAssert.Contains("'development'", error);
            StringAssert.Contains("boolean", error);

            Assert.IsFalse(BuildRequestParser.TryParse(
                "{\"development\":true,\"allowDebugging\":\"false\"}", Defaults(), out options, out error));
            StringAssert.Contains("'allowDebugging'", error);
        }

        [Test]
        public void TryParse_StillAcceptsAnAbsentField()
        {
            BuildRequestOptions options;
            string error;
            Assert.IsTrue(BuildRequestParser.TryParse(
                "{\"development\":true}", Defaults(), out options, out error), error);
            Assert.IsTrue(options.development);
            Assert.IsFalse(options.allowDebugging);
        }

        [Test]
        public void TryParse_RejectsDebugOptionsWithoutADevelopmentBuild()
        {
            // BuildPipeline drops them silently, which would produce a build that is quietly not
            // the one that was asked for.
            BuildRequestOptions options;
            string error;
            Assert.IsFalse(BuildRequestParser.TryParse(
                "{\"development\":false,\"allowDebugging\":true}", Defaults(), out options, out error));
            StringAssert.Contains("'development'", error);
        }

        [Test]
        public void TryParse_CleanAndStrictModeNeverInheritFromTheProject()
        {
            // Neither is a persisted project setting, so a build must not pick one up implicitly.
            var defaults = Defaults();
            defaults.clean = true;
            defaults.strictMode = true;

            BuildRequestOptions options;
            string error;
            Assert.IsTrue(BuildRequestParser.TryParse("", defaults, out options, out error));
            Assert.IsFalse(options.clean);
            Assert.IsFalse(options.strictMode);
        }

        [Test]
        public void OutputFileName_UsesThePlatformExtension()
        {
            Assert.AreEqual("Game.exe", BuildArtifactStore.OutputFileName("Game", BuildTarget.StandaloneWindows64));
            Assert.AreEqual("Game.exe", BuildArtifactStore.OutputFileName("Game", BuildTarget.StandaloneWindows));
            Assert.AreEqual("Game.app", BuildArtifactStore.OutputFileName("Game", BuildTarget.StandaloneOSX));
        }

        [Test]
        public void OutputFileName_FallsBackToADirectoryNameForOtherPlatforms()
        {
            // Unity treats an extensionless location as a directory, which is what WebGL and the
            // Apple platforms want. Guessing an executable suffix would be worse than not guessing.
            Assert.AreEqual("Game", BuildArtifactStore.OutputFileName("Game", BuildTarget.WebGL));
            Assert.AreEqual("Game", BuildArtifactStore.OutputFileName("Game", BuildTarget.StandaloneLinux64));
        }

        [Test]
        public void SanitizeProductName_StripsPathSeparatorsAndTraversal()
        {
            // The output location is server-controlled, and that has to hold even though it is
            // derived from a product name a project author can set to anything.
            Assert.AreEqual("etcpasswd", BuildArtifactStore.SanitizeProductName("../../etc/passwd"));
            Assert.AreEqual("MyGame", BuildArtifactStore.SanitizeProductName("My:Game*?"));
            Assert.AreEqual("My Game", BuildArtifactStore.SanitizeProductName("  My Game  "));
        }

        [Test]
        public void SanitizeProductName_FallsBackWhenNothingUsableRemains()
        {
            Assert.AreEqual("player", BuildArtifactStore.SanitizeProductName(""));
            Assert.AreEqual("player", BuildArtifactStore.SanitizeProductName(null));
            Assert.AreEqual("player", BuildArtifactStore.SanitizeProductName("///"));
        }

        [Test]
        public void ArtifactRoot_IsOutsideLibrary()
        {
            // Unity regenerates Library/ whenever it decides to, which would either destroy a
            // ~95 MB artifact silently or orphan it from the record naming it.
            var root = BuildArtifactStore.NormalizePath(BuildArtifactStore.Root);
            Assert.AreEqual("Builds/UnionAir", root);
        }

        [Test]
        public void ArtifactCaps_AreSetIndependentlyOfTheProfilingQuota()
        {
            // The 5 GB profiling quota would retain roughly fifty builds.
            Assert.Less(BuildArtifactStore.MaxTotalBytes, ProfilingArtifactStore.MaxTotalBytes);
            Assert.GreaterOrEqual(BuildArtifactStore.RetainedArtifacts, 1);
        }

        [Test]
        public void CompareNewestFirst_OrdersByFinishThenRequestThenId()
        {
            var older = new BuildRecord { id = "b-1", requestedAt = "2026-08-02T10:00:00Z", finishedAt = "2026-08-02T10:01:00Z" };
            var newer = new BuildRecord { id = "b-2", requestedAt = "2026-08-02T11:00:00Z", finishedAt = "2026-08-02T11:01:00Z" };

            Assert.Less(BuildService.CompareNewestFirst(newer, older), 0);
            Assert.Greater(BuildService.CompareNewestFirst(older, newer), 0);
        }

        [Test]
        public void CompareNewestFirst_FallsBackToRequestTimeForUnfinishedRecords()
        {
            var queuedEarly = new BuildRecord { id = "b-1", requestedAt = "2026-08-02T10:00:00Z" };
            var queuedLate = new BuildRecord { id = "b-2", requestedAt = "2026-08-02T11:00:00Z" };

            Assert.Less(BuildService.CompareNewestFirst(queuedLate, queuedEarly), 0);
        }
    }
}
