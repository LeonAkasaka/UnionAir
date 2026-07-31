using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the hand-rolled JSON reader that every endpoint parses its request body with.
    /// </summary>
    /// <remarks>
    /// The reader is a substring scanner rather than a real parser, so its edge cases are where
    /// request handling silently misreads a body: a value that a client pretty-printed onto the
    /// next line, a key that also appears inside a nested object, or a bracket inside a string
    /// literal. Only the string overloads are covered — the HttpListenerRequest ones cannot be
    /// exercised without a live server.
    /// </remarks>
    internal sealed class RequestBodyReaderTests
    {
        // ── FindToken whitespace handling ────────────────────────────────────

        [Test]
        public void GetString_ReadsAValueOnTheNextLine()
        {
            // A pretty-printer puts the value after a newline; stopping at the newline
            // would report the field as absent.
            const string json = "{\n  \"mode\":\n    \"press\"\n}";
            Assert.AreEqual("press", RequestBodyReader.GetString(json, "mode"));
        }

        [Test]
        public void GetInt_ReadsAValueOnTheNextLine()
        {
            const string json = "{\n  \"frame\":\n    12\n}";
            Assert.AreEqual(12, RequestBodyReader.GetInt(json, "frame"));
        }

        [Test]
        public void GetBool_ReadsAValueOnTheNextLine()
        {
            const string json = "{\n  \"late\":\n    true\n}";
            Assert.AreEqual(true, RequestBodyReader.GetBool(json, "late"));
        }

        [Test]
        public void GetString_StillReadsACompactBody()
        {
            Assert.AreEqual("press", RequestBodyReader.GetString("{\"mode\":\"press\"}", "mode"));
        }

        [Test]
        public void GetString_IgnoresAMatchingKeyInsideANestedObject()
        {
            const string json = "{\"outer\":{\"mode\":\"release\"},\"mode\":\"press\"}";
            Assert.AreEqual("press", RequestBodyReader.GetString(json, "mode"));
        }

        // ── GetRawArray ─────────────────────────────────────────────────────

        [Test]
        public void GetRawArray_ReturnsTheRawTokenForANumericArray()
        {
            Assert.AreEqual("[1.0, 0.0]", RequestBodyReader.GetRawArray("{\"value\":[1.0, 0.0]}", "value"));
        }

        [Test]
        public void GetRawArray_ReturnsNullWhenTheValueIsNotAnArray()
        {
            Assert.IsNull(RequestBodyReader.GetRawArray("{\"value\":5}", "value"));
            Assert.IsNull(RequestBodyReader.GetRawArray("{\"value\":null}", "value"));
            Assert.IsNull(RequestBodyReader.GetRawArray("{\"other\":[1]}", "value"));
        }

        [Test]
        public void GetRawArray_IgnoresAMatchingKeyInsideANestedObject()
        {
            const string json = "{\"outer\":{\"value\":[1,2]},\"value\":[3,4]}";
            Assert.AreEqual("[3,4]", RequestBodyReader.GetRawArray(json, "value"));
        }

        [Test]
        public void GetRawArray_IgnoresABracketInsideAStringLiteral()
        {
            const string json = "{\"note\":\"[not an array]\",\"value\":[1,2]}";
            Assert.AreEqual("[1,2]", RequestBodyReader.GetRawArray(json, "value"));
            Assert.IsNull(RequestBodyReader.GetRawArray(json, "note"));
        }

        [Test]
        public void GetRawArray_HandlesNestedArrays()
        {
            Assert.AreEqual("[[1,2],[3]]", RequestBodyReader.GetRawArray("{\"grid\":[[1,2],[3]]}", "grid"));
        }

        // ── GetArray anchoring ──────────────────────────────────────────────

        [Test]
        public void GetArray_ReturnsEmptyWhenTheValueIsNotAnArray()
        {
            // Scanning forward for the next '[' would latch onto the unrelated "other" array.
            const string json = "{\"operations\":null,\"other\":[{\"a\":1}]}";
            CollectionAssert.IsEmpty(RequestBodyReader.GetArray(json, "operations"));
        }

        [Test]
        public void GetArray_ReturnsObjectElements()
        {
            const string json = "{\"operations\":[{\"op\":\"a\"},{\"op\":\"b\"}]}";
            CollectionAssert.AreEqual(
                new[] { "{\"op\":\"a\"}", "{\"op\":\"b\"}" },
                RequestBodyReader.GetArray(json, "operations"));
        }

        // ── TryGetArrayElements ─────────────────────────────────────────────

        [Test]
        public void TryGetArrayElements_ReportsAnAbsentKeyAsSuccessWithoutPresence()
        {
            System.Collections.Generic.List<string> elements;
            bool present;
            string error;
            Assert.IsTrue(RequestBodyReader.TryGetArrayElements("{\"a\":1}", "inputs", out elements, out present, out error));
            Assert.IsFalse(present);
            Assert.IsNull(error);
            CollectionAssert.IsEmpty(elements);
        }

        [Test]
        public void TryGetArrayElements_KeepsScalarElements()
        {
            // GetArray drops these silently; the strict reader must not.
            System.Collections.Generic.List<string> elements;
            bool present;
            string error;
            Assert.IsTrue(RequestBodyReader.TryGetArrayElements(
                "{\"inputs\":[1, \"two\", {\"three\":3}]}", "inputs", out elements, out present, out error));
            Assert.IsTrue(present);
            CollectionAssert.AreEqual(new[] { "1", "\"two\"", "{\"three\":3}" }, elements);
        }

        [Test]
        public void TryGetArrayElements_RejectsANonArrayValue()
        {
            System.Collections.Generic.List<string> elements;
            bool present;
            string error;
            Assert.IsFalse(RequestBodyReader.TryGetArrayElements("{\"inputs\":5}", "inputs", out elements, out present, out error));
            Assert.IsTrue(present);
            StringAssert.Contains("must be a JSON array", error);
        }

        [Test]
        public void TryGetArrayElements_ReturnsEmptyForAnEmptyArray()
        {
            System.Collections.Generic.List<string> elements;
            bool present;
            string error;
            Assert.IsTrue(RequestBodyReader.TryGetArrayElements("{\"inputs\":[]}", "inputs", out elements, out present, out error));
            Assert.IsTrue(present, "An empty array is present and must be distinguishable from an absent key.");
            CollectionAssert.IsEmpty(elements);
        }

        [Test]
        public void TryGetArrayElements_RejectsATrailingComma()
        {
            System.Collections.Generic.List<string> elements;
            bool present;
            string error;
            Assert.IsFalse(RequestBodyReader.TryGetArrayElements("{\"inputs\":[1,]}", "inputs", out elements, out present, out error));
            StringAssert.Contains("well-formed", error);
        }

        [Test]
        public void TryGetArrayElements_HandlesBracketsInsideStringLiterals()
        {
            System.Collections.Generic.List<string> elements;
            bool present;
            string error;
            Assert.IsTrue(RequestBodyReader.TryGetArrayElements(
                "{\"inputs\":[\"a[b\",\"c]d\"]}", "inputs", out elements, out present, out error));
            CollectionAssert.AreEqual(new[] { "\"a[b\"", "\"c]d\"" }, elements);
        }

        [Test]
        public void TryGetArrayElements_ReadsAPrettyPrintedArray()
        {
            const string json = "{\n  \"inputs\": [\n    { \"frame\": 0 },\n    { \"frame\": 3 }\n  ]\n}";
            System.Collections.Generic.List<string> elements;
            bool present;
            string error;
            Assert.IsTrue(RequestBodyReader.TryGetArrayElements(json, "inputs", out elements, out present, out error));
            Assert.AreEqual(2, elements.Count);
            Assert.AreEqual(0, RequestBodyReader.GetInt(elements[0], "frame"));
            Assert.AreEqual(3, RequestBodyReader.GetInt(elements[1], "frame"));
        }

        // ── TryGetFloatArray ────────────────────────────────────────────────

        [Test]
        public void TryGetFloatArray_ParsesAVector2()
        {
            float[] values;
            string error;
            Assert.IsTrue(RequestBodyReader.TryGetFloatArray("{\"value\":[1.0, -0.5]}", "value", out values, out error));
            Assert.AreEqual(2, values.Length);
            Assert.AreEqual(1.0f, values[0], 1e-6f);
            Assert.AreEqual(-0.5f, values[1], 1e-6f);
        }

        [TestCase("{\"value\":[NaN, 0]}")]
        [TestCase("{\"value\":[Infinity, 0]}")]
        public void TryGetFloatArray_RejectsNonFiniteNumbers(string json)
        {
            float[] values;
            string error;
            Assert.IsFalse(RequestBodyReader.TryGetFloatArray(json, "value", out values, out error));
            StringAssert.Contains("value[0]", error);
        }

        [Test]
        public void TryGetFloatArray_RejectsAQuotedNumber()
        {
            float[] values;
            string error;
            Assert.IsFalse(RequestBodyReader.TryGetFloatArray("{\"value\":[\"1.0\", 0]}", "value", out values, out error));
            StringAssert.Contains("value[0]", error);
        }

        [Test]
        public void TryGetFloatArray_ReportsAMissingField()
        {
            float[] values;
            string error;
            Assert.IsFalse(RequestBodyReader.TryGetFloatArray("{\"other\":1}", "value", out values, out error));
            StringAssert.Contains("missing", error);
        }
    }

    internal sealed class RestRequestPolicyTests
    {
        [Test]
        public void IsOriginAllowed_AllowsAnAbsentHeader()
        {
            Assert.IsTrue(RestRequestPolicy.IsOriginAllowed(null));
        }

        [Test]
        public void IsOriginAllowed_RejectsAPresentHeaderWithoutValues()
        {
            Assert.IsFalse(RestRequestPolicy.IsOriginAllowed(new string[0]));
        }

        [TestCase("")]
        [TestCase("null")]
        [TestCase("https://attacker.example")]
        public void IsOriginAllowed_RejectsAnyPresentHeader(string origin)
        {
            Assert.IsFalse(RestRequestPolicy.IsOriginAllowed(new[] { origin }));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("text/plain")]
        public void HasSupportedContentType_AllowsAnyTypeForAnEmptyBody(string contentType)
        {
            Assert.IsTrue(RestRequestPolicy.HasSupportedContentType(false, contentType));
        }

        [TestCase("application/json")]
        [TestCase("APPLICATION/JSON")]
        [TestCase("application/json; charset=utf-8")]
        [TestCase(" application/json ; charset=UTF-8")]
        public void HasSupportedContentType_AllowsJsonForANonEmptyBody(string contentType)
        {
            Assert.IsTrue(RestRequestPolicy.HasSupportedContentType(true, contentType));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("text/plain")]
        [TestCase("application/x-www-form-urlencoded")]
        [TestCase("multipart/form-data; boundary=test")]
        [TestCase("application/problem+json")]
        public void HasSupportedContentType_RejectsOtherTypesForANonEmptyBody(string contentType)
        {
            Assert.IsFalse(RestRequestPolicy.HasSupportedContentType(true, contentType));
        }
    }
}
