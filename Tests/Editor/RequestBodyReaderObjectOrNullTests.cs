using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the three-way read a PATCH with optional fields depends on: omitted, an
    /// explicit null, and an object. GetObject answers null for the first two alike, which
    /// would make "leave the mask alone" and "clear the mask" the same request.
    /// </summary>
    internal sealed class RequestBodyReaderObjectOrNullTests
    {
        [Test]
        public void OmittedFieldIsNotPresent()
        {
            var ok = RequestBodyReader.TryGetObjectOrNullValue(
                "{\"layerIndex\":1}", "avatarMask", out var value, out var isNull, out var present);

            Assert.IsTrue(ok);
            Assert.IsFalse(present, "an omitted field must not read as present");
            Assert.IsFalse(isNull);
            Assert.IsNull(value);
        }

        [Test]
        public void ExplicitNullIsPresentAndNull()
        {
            var ok = RequestBodyReader.TryGetObjectOrNullValue(
                "{\"layerIndex\":1,\"avatarMask\":null}", "avatarMask", out var value, out var isNull, out var present);

            Assert.IsTrue(ok);
            Assert.IsTrue(present);
            Assert.IsTrue(isNull, "an explicit null is what clears the field");
            Assert.IsNull(value);
        }

        [Test]
        public void AnObjectIsReturnedWhole()
        {
            var ok = RequestBodyReader.TryGetObjectOrNullValue(
                "{\"avatarMask\":{\"guid\":\"abc123\"},\"iKPass\":true}", "avatarMask",
                out var value, out var isNull, out var present);

            Assert.IsTrue(ok);
            Assert.IsTrue(present);
            Assert.IsFalse(isNull);
            Assert.AreEqual("{\"guid\":\"abc123\"}", value);
        }

        [Test]
        public void ANestedObjectIsNotTruncatedAtItsFirstBrace()
        {
            var ok = RequestBodyReader.TryGetObjectOrNullValue(
                "{\"avatarMask\":{\"guid\":\"a\",\"nested\":{\"x\":1}},\"iKPass\":true}", "avatarMask",
                out var value, out _, out var present);

            Assert.IsTrue(ok);
            Assert.IsTrue(present);
            Assert.AreEqual("{\"guid\":\"a\",\"nested\":{\"x\":1}}", value);
        }

        [TestCase("{\"avatarMask\":5}")]
        [TestCase("{\"avatarMask\":\"abc\"}")]
        [TestCase("{\"avatarMask\":true}")]
        public void AValueThatIsNeitherObjectNorNullIsRejected(string body)
        {
            // Rejected rather than treated as a clear: silently reading 5 as "remove the
            // mask" is the shape of mistake the endpoint exists to avoid.
            Assert.IsFalse(RequestBodyReader.TryGetObjectOrNullValue(
                body, "avatarMask", out _, out _, out var present));
            Assert.IsTrue(present);
        }

        [Test]
        public void APrettyPrintedBodyReadsTheSame()
        {
            var body = "{\n  \"layerIndex\": 1,\n  \"avatarMask\": null\n}";
            var ok = RequestBodyReader.TryGetObjectOrNullValue(
                body, "avatarMask", out _, out var isNull, out var present);

            Assert.IsTrue(ok);
            Assert.IsTrue(present);
            Assert.IsTrue(isNull);
        }
    }
}
