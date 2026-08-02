using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the define symbol parsing behind <c>GET /api/build/settings</c>.
    /// </summary>
    /// <remarks>
    /// Unity stores the define symbol list as whatever string the Inspector was given, so the
    /// parsing rather than the reading is where a client-visible mistake can happen. Everything
    /// else on that endpoint needs a live Editor and is verified by hand.
    /// </remarks>
    internal sealed class BuildSettingsReaderTests
    {
        [Test]
        public void SplitDefineSymbols_ReturnsEmptyForNoSymbols()
        {
            Assert.AreEqual(0, BuildSettingsReader.SplitDefineSymbols(null).Count);
            Assert.AreEqual(0, BuildSettingsReader.SplitDefineSymbols("").Count);
            Assert.AreEqual(0, BuildSettingsReader.SplitDefineSymbols(";;").Count);
        }

        [Test]
        public void SplitDefineSymbols_SplitsOnSemicolons()
        {
            var symbols = BuildSettingsReader.SplitDefineSymbols("ALPHA;BETA;GAMMA");
            Assert.AreEqual(3, symbols.Count);
            Assert.AreEqual("ALPHA", symbols[0]);
            Assert.AreEqual("GAMMA", symbols[2]);
        }

        [Test]
        public void SplitDefineSymbols_DropsEmptyEntriesAndWhitespace()
        {
            var symbols = BuildSettingsReader.SplitDefineSymbols(" ALPHA ;; BETA ; ");
            Assert.AreEqual(2, symbols.Count);
            Assert.AreEqual("ALPHA", symbols[0]);
            Assert.AreEqual("BETA", symbols[1]);
        }

        [Test]
        public void SplitDefineSymbols_AcceptsUnitySecondarySeparators()
        {
            // Unity's own Inspector accepts commas and spaces and stores them verbatim, so a
            // project can legitimately hold a list this endpoint must still report correctly.
            var symbols = BuildSettingsReader.SplitDefineSymbols("ALPHA,BETA GAMMA");
            Assert.AreEqual(3, symbols.Count);
            Assert.AreEqual("BETA", symbols[1]);
        }
    }
}
