using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the text handling that turns Unity compiler output into structured fields.
    /// </summary>
    /// <remarks>
    /// These are the parts an agent depends on to locate what to fix, and they are the only
    /// parts of the compile pipeline that can be exercised without driving a real compilation.
    /// </remarks>
    internal sealed class CompileMessageParserTests
    {
        private const string ProjectRoot = "C:/Users/dev/Projects/Game";

        [Test]
        public void ExtractCode_ReadsRoslynCode()
        {
            const string message =
                "Assets/Scripts/Player.cs(12,9): error CS0103: The name 'bar' does not exist";
            Assert.AreEqual("CS0103", CompileMessageParser.ExtractCode(message));
        }

        [Test]
        public void ExtractCode_ReadsAnalyzerCode()
        {
            Assert.AreEqual(
                "UNT0001",
                CompileMessageParser.ExtractCode("Assets/A.cs(1,1): warning UNT0001: Empty Unity message"));
        }

        [Test]
        public void ExtractCode_ReturnsNullWhenAbsent()
        {
            Assert.IsNull(CompileMessageParser.ExtractCode("Build failed: could not write output"));
            Assert.IsNull(CompileMessageParser.ExtractCode(""));
            Assert.IsNull(CompileMessageParser.ExtractCode(null));
        }

        [Test]
        public void ExtractCode_IgnoresCodeLikeTextInsideMessage()
        {
            // A bare token with no following colon is part of the prose, not a diagnostic code.
            Assert.IsNull(CompileMessageParser.ExtractCode("The name 'AB1234' does not exist"));
        }

        [Test]
        public void StripPrefix_RemovesPositionAndCode()
        {
            const string message =
                "Assets/Scripts/Player.cs(12,9): error CS0103: The name 'bar' does not exist";
            Assert.AreEqual(
                "The name 'bar' does not exist",
                CompileMessageParser.StripPrefix(message));
        }

        [Test]
        public void StripPrefix_KeepsColonsInsideTheMessage()
        {
            const string message = "Assets/A.cs(1,1): error CS1002: Expected: a semicolon";
            Assert.AreEqual("Expected: a semicolon", CompileMessageParser.StripPrefix(message));
        }

        [Test]
        public void StripPrefix_ReturnsInputWhenNoPrefix()
        {
            Assert.AreEqual("Build failed", CompileMessageParser.StripPrefix("Build failed"));
            Assert.AreEqual("", CompileMessageParser.StripPrefix(null));
        }

        [Test]
        public void NormalizePath_ConvertsBackslashesToForwardSlashes()
        {
            Assert.AreEqual(
                "Assets/Scratch/Probe.cs",
                CompileMessageParser.NormalizePath(@"Assets\Scratch\Probe.cs", ProjectRoot));
        }

        [Test]
        public void NormalizePath_MakesAbsolutePathsProjectRelative()
        {
            Assert.AreEqual(
                "Assets/Scripts/Player.cs",
                CompileMessageParser.NormalizePath(
                    @"C:\Users\dev\Projects\Game\Assets\Scripts\Player.cs", ProjectRoot));
        }

        [Test]
        public void NormalizePath_LeavesPathsOutsideTheProjectAlone()
        {
            Assert.AreEqual(
                "D:/Other/Thing.cs",
                CompileMessageParser.NormalizePath(@"D:\Other\Thing.cs", ProjectRoot));
        }

        [Test]
        public void NormalizePath_ReturnsNullWhenTheCompilerReportedNoFile()
        {
            // Build-system diagnostics arrive with no file, and must not be reported as one.
            Assert.IsNull(CompileMessageParser.NormalizePath("", ProjectRoot));
            Assert.IsNull(CompileMessageParser.NormalizePath(null, ProjectRoot));
            Assert.IsNull(CompileMessageParser.NormalizePath("   ", ProjectRoot));
        }

        [Test]
        public void ClassifyTarget_DistinguishesEditorFromPlayer()
        {
            Assert.AreEqual("editor", CompileMessageParser.ClassifyTarget("Library/ScriptAssemblies"));
            Assert.AreEqual("player", CompileMessageParser.ClassifyTarget("Library/PlayerScriptAssemblies"));
            Assert.AreEqual("player", CompileMessageParser.ClassifyTarget("Library/Bee/PlayerScriptAssemblies"));
            Assert.AreEqual("other", CompileMessageParser.ClassifyTarget("Temp/Custom"));
            Assert.AreEqual("other", CompileMessageParser.ClassifyTarget(""));
        }

        [Test]
        public void ClassifyTarget_AcceptsWindowsSeparators()
        {
            Assert.AreEqual("editor", CompileMessageParser.ClassifyTarget(@"Library\ScriptAssemblies"));
            Assert.AreEqual("player", CompileMessageParser.ClassifyTarget(@"Library\PlayerScriptAssemblies"));
            Assert.AreEqual("player", CompileMessageParser.ClassifyTarget(@"Library\Bee\PlayerScriptAssemblies"));
        }

        [Test]
        public void ClassifyTarget_AcceptsCaseAndTrailingSeparators()
        {
            Assert.AreEqual("editor", CompileMessageParser.ClassifyTarget("library/scriptassemblies/"));
            Assert.AreEqual("player", CompileMessageParser.ClassifyTarget("library/bee/playerscriptassemblies/"));
        }

        [Test]
        public void ClassifyTarget_RequiresWholePathSegments()
        {
            Assert.AreEqual("other", CompileMessageParser.ClassifyTarget("NotLibrary/Bee/PlayerScriptAssemblies"));
            Assert.AreEqual("other", CompileMessageParser.ClassifyTarget("Library/Bee/PlayerScriptAssembliesBackup"));
            Assert.AreEqual("other", CompileMessageParser.ClassifyTarget("Temp/LibraryLike/ScriptAssemblies"));
        }

        [Test]
        public void IsValidId_RejectsAnythingUsableForPathTraversal()
        {
            Assert.IsTrue(CompileMessageParser.IsValidId("c-20260728-040030-67c0fd"));
            Assert.IsTrue(CompileMessageParser.IsValidId("my_run_1"));
            Assert.IsTrue(CompileMessageParser.IsValidId("my-run-1"));

            Assert.IsFalse(CompileMessageParser.IsValidId(".."));
            Assert.IsFalse(CompileMessageParser.IsValidId("a/b"));
            Assert.IsFalse(CompileMessageParser.IsValidId(@"a\b"));
            Assert.IsFalse(CompileMessageParser.IsValidId("a.json"));
            Assert.IsFalse(CompileMessageParser.IsValidId(""));
            Assert.IsFalse(CompileMessageParser.IsValidId(null));
            Assert.IsFalse(CompileMessageParser.IsValidId(new string('a', 65)));
        }

        [TestCase("CON")]
        [TestCase("con")]
        [TestCase("PRN")]
        [TestCase("AUX")]
        [TestCase("NUL")]
        [TestCase("COM1")]
        [TestCase("com9")]
        [TestCase("LPT1")]
        [TestCase("lpt9")]
        public void IsValidId_RejectsWindowsDeviceNames(string id)
        {
            Assert.IsFalse(CompileMessageParser.IsValidId(id));
        }

        [Test]
        public void Cap_TruncatesPathologicalMessages()
        {
            var capped = CompileMessageParser.Cap(new string('x', 5000));
            Assert.AreEqual(4003, capped.Length);
            Assert.IsTrue(capped.EndsWith("..."));

            Assert.AreEqual("short", CompileMessageParser.Cap("short"));
            Assert.AreEqual("", CompileMessageParser.Cap(null));
        }
    }
}
