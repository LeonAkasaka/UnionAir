using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// What a <c>properties</c> key is taken to address, decided on the key alone.
    /// </summary>
    /// <remarks>
    /// This is the gate that decides which keys reach an array at all, so a spelling that slips
    /// through it is a write to something the caller did not name. It needs no Editor state, which
    /// is why the exhaustive cases live here rather than in the endpoint tests.
    /// </remarks>
    internal sealed class SerializedPropertyArrayAddressTests
    {
        [TestCase("m_Materials.Array.data[0]", "m_Materials", 0)]
        [TestCase("m_Materials.Array.data[12]", "m_Materials", 12)]
        [TestCase("m_Nested.m_Items.Array.data[3]", "m_Nested.m_Items", 3)]
        public void AnElementAddressReportsItsArrayAndIndex(string key, string arrayPath, int index)
        {
            Assert.IsTrue(SerializedPropertySerializer.TryParseArrayAddress(
                key, out var parsedPath, out var address, out var parsedIndex), key);
            Assert.AreEqual(arrayPath, parsedPath);
            Assert.AreEqual(SerializedPropertySerializer.ArrayAddress.Element, address);
            Assert.AreEqual(index, parsedIndex);
        }

        [Test]
        public void ASizeAddressReportsItsArray()
        {
            Assert.IsTrue(SerializedPropertySerializer.TryParseArrayAddress(
                "m_Materials.Array.size", out var arrayPath, out var address, out _));
            Assert.AreEqual("m_Materials", arrayPath);
            Assert.AreEqual(SerializedPropertySerializer.ArrayAddress.Size, address);
        }

        [TestCase("m_Materials", TestName = "the array itself")]
        [TestCase("m_LocalPosition.x", TestName = "an ordinary child path")]
        [TestCase("m_Materials.Array.data[0].name", TestName = "past an element")]
        [TestCase("m_Materials.Array.data[]", TestName = "no index")]
        [TestCase("m_Materials.Array.data[-1]", TestName = "a negative index")]
        [TestCase("m_Materials.Array.data[1x]", TestName = "an index that is not a number")]
        [TestCase("m_Materials.Array.data[99999999999999999999]", TestName = "an index that overflows")]
        [TestCase(".Array.size", TestName = "a length with no array")]
        [TestCase("m_Items.Array.data[0].m_Inner.Array.size", TestName = "a length beneath an element")]
        [TestCase("m_Items.Array.data[0].m_Inner.Array.data[1]", TestName = "an element beneath an element")]
        public void AnythingElseIsNotAnAddress(string key)
        {
            Assert.IsFalse(SerializedPropertySerializer.TryParseArrayAddress(key, out _, out _, out _), key);
        }

        [TestCase("m_Materials.Array.data[0].name", true)]
        [TestCase("m_Materials.Array.size", true)]
        [TestCase("m_LocalPosition.x", false)]
        [TestCase("m_Materials", false)]
        public void ReachingIntoAnArrayIsRecognisedSeparately(string key, bool reaches)
        {
            // A key that reaches inside an array without being one of the two addresses has to be
            // refused with a message about arrays, not reported as naming nothing.
            Assert.AreEqual(reaches, SerializedPropertySerializer.NamesArrayInternals(key), key);
        }
    }
}
