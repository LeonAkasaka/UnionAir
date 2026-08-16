using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the types an object reference's own fields accept.
    /// </summary>
    /// <remarks>
    /// Every field of a reference names something — a GUID, a path, a type, a hierarchy address —
    /// and none of them is a number. The reader they went through returned the raw token when the
    /// value was not quoted, so <c>{"assetGuid": 5}</c> arrived as the GUID <c>"5"</c> and was
    /// answered <c>404 Asset not found</c>: a status about a missing asset, for a request whose
    /// fault is a value of the wrong type.
    ///
    /// These test the reader rather than each endpoint that calls it, because the endpoints share
    /// it: the same six field names are read by the component write, the ScriptableObject write,
    /// the material write, the custom-controller resolver, and query-string target parsing.
    /// </remarks>
    internal sealed class ObjectReferenceFieldTypeTests
    {
        [Test]
        public void AStringIsRead()
        {
            Assert.IsTrue(Read("{\"assetGuid\":\"abc123\"}", "assetGuid", out var value, out var error));
            Assert.AreEqual("abc123", value);
            Assert.IsNull(error);
        }

        [Test]
        public void AnEscapedStringIsUnescaped()
        {
            Assert.IsTrue(Read("{\"assetPath\":\"Assets\\/A B\\u0041.mat\"}", "assetPath", out var value, out _));
            Assert.AreEqual("Assets/A BA.mat", value);
        }

        [Test]
        public void AnAbsentFieldIsNotAnError()
        {
            Assert.IsTrue(Read("{\"assetPath\":\"Assets/A.mat\"}", "assetGuid", out var value, out var error));
            Assert.IsNull(value);
            Assert.IsNull(error);
        }

        [Test]
        public void AnExplicitNullReadsAsAbsent()
        {
            // Not an error, because it already meant this: a reference carrying
            // "assetGuid": null is one that did not give a GUID, and the caller's own
            // "requires assetGuid or assetPath" is the answer it should get.
            Assert.IsTrue(Read("{\"assetGuid\":null}", "assetGuid", out var value, out var error));
            Assert.IsNull(value);
            Assert.IsNull(error);
        }

        [TestCase("5")]
        [TestCase("-1.5")]
        [TestCase("true")]
        [TestCase("false")]
        [TestCase("[\"abc\"]")]
        [TestCase("{\"nested\":\"abc\"}")]
        public void ANonStringValueIsRefusedByName(string rawValue)
        {
            Assert.IsFalse(
                Read("{\"assetGuid\":" + rawValue + "}", "assetGuid", out var value, out var error),
                rawValue);

            Assert.IsNull(value);
            Assert.AreEqual(
                "Field 'assetGuid' of property m_ProbeAnchor must be a JSON string.", error);
        }

        [Test]
        public void ARefusalNamesTheFieldRatherThanTheReference()
        {
            // The whole point of the change: the answer says which field is wrong, where the
            // status used to describe an asset that could not be found.
            Read("{\"assetPath\":7}", "assetPath", out _, out var error);
            StringAssert.Contains("'assetPath'", error);

            Read("{\"scenePath\":7}", "scenePath", out _, out error);
            StringAssert.Contains("'scenePath'", error);
        }

        [Test]
        public void AMalformedValueNeverReachesThisReaderAsAWrongType()
        {
            // An unescaped backslash is what a hand-written Windows path produces. It does not
            // arrive here as a wrong type: the raw-value reader parses before it returns, so the
            // field reads as absent. The endpoint answers earlier still — measured on 6000.0.80f1,
            // PATCH /api/gameobjects/components refuses the whole request with "The value of
            // 'm_Materials' is not well-formed JSON" — so no caller has to tell this apart from a
            // field that was never sent.
            Assert.IsTrue(
                Read("{\"assetPath\":\"C:\\Assets\\Foo.mat\"}", "assetPath", out var value, out var error));
            Assert.IsNull(value);
            Assert.IsNull(error);
        }

        [Test]
        public void ARefusalIsA400()
        {
            ObjectReferenceResolverUtils.TryReadReferenceField(
                "{\"assetGuid\":5}", "assetGuid", "property m_ProbeAnchor",
                out _, out _, out var statusCode);

            // Not 404: nothing was looked up, and nothing was missing.
            Assert.AreEqual(400, statusCode);
        }

        private static bool Read(string json, string field, out string value, out string error)
            => ObjectReferenceResolverUtils.TryReadReferenceField(
                json, field, "property m_ProbeAnchor", out value, out error, out _);
    }
}
