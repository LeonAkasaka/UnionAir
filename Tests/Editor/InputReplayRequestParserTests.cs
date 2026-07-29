using System.Collections.Generic;
using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers validation of the <c>inputs</c> list accepted by <c>POST /api/editor/play</c>.
    /// </summary>
    /// <remarks>
    /// This is the only point at which a malformed replay can still be reported to the caller:
    /// entering Play mode causes a domain reload and the HTTP response has already been sent by
    /// then. Anything this parser lets through becomes a silent failure, so the rejections matter
    /// as much as the acceptances.
    /// </remarks>
    internal sealed class InputReplayRequestParserTests
    {
        private static string Body(params string[] elements)
            => "{\"inputs\":[" + string.Join(",", elements) + "]}";

        private static bool Parse(string body, out List<InputReplayEventSpec> events, out string error)
        {
            bool present;
            return InputReplayRequestParser.TryParse(body, out events, out present, out error);
        }

        private static string ParseError(string body)
        {
            List<InputReplayEventSpec> events;
            string error;
            Assert.IsFalse(Parse(body, out events, out error), "Expected the body to be rejected.");
            return error;
        }

        // ── Presence and shape ──────────────────────────────────────────────

        [Test]
        public void TryParse_ReportsNoReplayWhenInputsIsAbsent()
        {
            List<InputReplayEventSpec> events;
            bool present;
            string error;
            Assert.IsTrue(InputReplayRequestParser.TryParse("{\"other\":1}", out events, out present, out error));
            Assert.IsFalse(present);
            Assert.IsNull(error);
            CollectionAssert.IsEmpty(events);
        }

        [Test]
        public void TryParse_ReportsNoReplayForAnEmptyBody()
        {
            List<InputReplayEventSpec> events;
            bool present;
            string error;
            Assert.IsTrue(InputReplayRequestParser.TryParse("", out events, out present, out error));
            Assert.IsFalse(present);
        }

        [Test]
        public void TryParse_RejectsAnEmptyList()
        {
            StringAssert.Contains("at least one event", ParseError("{\"inputs\":[]}"));
        }

        [Test]
        public void TryParse_RejectsInputsThatIsNotAnArray()
        {
            StringAssert.Contains("must be a JSON array", ParseError("{\"inputs\":5}"));
        }

        [Test]
        public void TryParse_RejectsANonObjectElementNamingItsIndex()
        {
            var error = ParseError(Body("{\"frame\":0,\"type\":\"perform\",\"action\":\"A\",\"mode\":\"press\"}", "7"));
            StringAssert.Contains("inputs[1]", error);
            StringAssert.Contains("JSON object", error);
        }

        // ── frame and type ──────────────────────────────────────────────────

        [Test]
        public void TryParse_RejectsAMissingFrame()
        {
            StringAssert.Contains("'frame' is required", ParseError(Body("{\"type\":\"perform\",\"action\":\"A\",\"mode\":\"press\"}")));
        }

        [TestCase("-1")]
        [TestCase("1.5")]
        [TestCase("\"3\"")]
        public void TryParse_RejectsAFrameThatIsNotANonNegativeInteger(string frame)
        {
            var body = Body("{\"frame\":" + frame + ",\"type\":\"perform\",\"action\":\"A\",\"mode\":\"press\"}");
            StringAssert.Contains("non-negative integer", ParseError(body));
        }

        [Test]
        public void TryParse_RejectsAMissingType()
        {
            StringAssert.Contains("'type' is required", ParseError(Body("{\"frame\":0}")));
        }

        [Test]
        public void TryParse_RejectsAnUnknownType()
        {
            StringAssert.Contains("Unknown type 'jump'", ParseError(Body("{\"frame\":0,\"type\":\"jump\"}")));
        }

        [Test]
        public void TryParse_RejectsHoldFrames()
        {
            // Duration on a timeline is expressed by placing release on a later frame.
            var body = Body("{\"frame\":0,\"type\":\"perform\",\"action\":\"A\",\"mode\":\"press\",\"holdFrames\":3}");
            StringAssert.Contains("holdFrames", ParseError(body));
        }

        // ── perform ─────────────────────────────────────────────────────────

        [Test]
        public void TryParse_AcceptsPerformPressAndRelease()
        {
            List<InputReplayEventSpec> events;
            string error;
            Assert.IsTrue(Parse(Body(
                "{\"frame\":0,\"type\":\"perform\",\"action\":\"Player/Jump\",\"mode\":\"press\"}",
                "{\"frame\":3,\"type\":\"perform\",\"action\":\"Player/Jump\",\"mode\":\"release\"}"), out events, out error), error);
            Assert.AreEqual(2, events.Count);
            Assert.AreEqual("Player/Jump", events[0].action);
            Assert.AreEqual("press", events[0].mode);
            Assert.AreEqual(3, events[1].frame);
            Assert.AreEqual("release", events[1].mode);
        }

        [Test]
        public void TryParse_RejectsPerformWithoutAnAction()
        {
            StringAssert.Contains("'action' is required", ParseError(Body("{\"frame\":0,\"type\":\"perform\",\"mode\":\"press\"}")));
        }

        [Test]
        public void TryParse_RejectsPerformWithAValue()
        {
            var body = Body("{\"frame\":0,\"type\":\"perform\",\"action\":\"A\",\"mode\":\"press\",\"value\":1}");
            StringAssert.Contains("uses 'mode', not 'value'", ParseError(body));
        }

        [Test]
        public void TryParse_RejectsPerformWithoutAMode()
        {
            StringAssert.Contains("'mode' is required", ParseError(Body("{\"frame\":0,\"type\":\"perform\",\"action\":\"A\"}")));
        }

        [Test]
        public void TryParse_RejectsPerformTapWithGuidance()
        {
            var body = Body("{\"frame\":0,\"type\":\"perform\",\"action\":\"A\",\"mode\":\"tap\"}");
            var error = ParseError(body);
            StringAssert.Contains("'tap' is not supported", error);
            StringAssert.Contains("release", error);
        }

        // ── set ─────────────────────────────────────────────────────────────

        [Test]
        public void TryParse_ParsesAVector2Value()
        {
            List<InputReplayEventSpec> events;
            string error;
            Assert.IsTrue(Parse(Body(
                "{\"frame\":10,\"type\":\"set\",\"action\":\"Player/Move\",\"value\":[1.0, -0.5]}"), out events, out error), error);
            Assert.AreEqual(InputReplayValueKind.Vector2, events[0].valueKind);
            Assert.AreEqual(1.0f, events[0].valueX, 1e-6f);
            Assert.AreEqual(-0.5f, events[0].valueY, 1e-6f);
        }

        [Test]
        public void TryParse_ParsesAScalarValue()
        {
            List<InputReplayEventSpec> events;
            string error;
            Assert.IsTrue(Parse(Body(
                "{\"frame\":0,\"type\":\"set\",\"action\":\"Player/Throttle\",\"value\":0.75}"), out events, out error), error);
            Assert.AreEqual(InputReplayValueKind.Scalar, events[0].valueKind);
            Assert.AreEqual(0.75f, events[0].valueX, 1e-6f);
        }

        [Test]
        public void TryParse_RejectsSetWithoutAValue()
        {
            StringAssert.Contains("'value' is required", ParseError(Body("{\"frame\":0,\"type\":\"set\",\"action\":\"A\"}")));
        }

        [Test]
        public void TryParse_RejectsSetWithAMode()
        {
            var body = Body("{\"frame\":0,\"type\":\"set\",\"action\":\"A\",\"value\":1,\"mode\":\"press\"}");
            StringAssert.Contains("uses 'value', not 'mode'", ParseError(body));
        }

        [TestCase("[1.0]")]
        [TestCase("[1.0, 2.0, 3.0]")]
        public void TryParse_RejectsAVectorValueThatIsNotTwoNumbers(string value)
        {
            var body = Body("{\"frame\":0,\"type\":\"set\",\"action\":\"A\",\"value\":" + value + "}");
            StringAssert.Contains("[x, y]", ParseError(body));
        }

        [TestCase("[NaN, 0]")]
        [TestCase("NaN")]
        [TestCase("\"1.0\"")]
        public void TryParse_RejectsANonFiniteOrNonNumericValue(string value)
        {
            var body = Body("{\"frame\":0,\"type\":\"set\",\"action\":\"A\",\"value\":" + value + "}");
            List<InputReplayEventSpec> events;
            string error;
            Assert.IsFalse(Parse(body, out events, out error));
            StringAssert.Contains("inputs[0]", error);
        }

        // ── pointer ─────────────────────────────────────────────────────────

        [Test]
        public void TryParse_ParsesANormalizedTopLeftPointer()
        {
            List<InputReplayEventSpec> events;
            string error;
            Assert.IsTrue(Parse(Body(
                "{\"frame\":20,\"type\":\"pointer\",\"mode\":\"press\",\"normalizedPosition\":{\"x\":0.5,\"y\":0.25},\"origin\":\"topLeft\"}"),
                out events, out error), error);
            Assert.AreEqual(InputReplayPointKind.Normalized, events[0].pointKind);
            Assert.AreEqual(0.5f, events[0].pointX, 1e-6f);
            Assert.AreEqual(0.25f, events[0].pointY, 1e-6f);
            Assert.IsTrue(events[0].originTopLeft);
            Assert.AreEqual("left", events[0].button, "button must default to left.");
        }

        [Test]
        public void TryParse_AllowsAPointerReleaseWithoutAPosition()
        {
            List<InputReplayEventSpec> events;
            string error;
            Assert.IsTrue(Parse(Body("{\"frame\":5,\"type\":\"pointer\",\"mode\":\"release\"}"), out events, out error), error);
            Assert.AreEqual(InputReplayPointKind.None, events[0].pointKind);
        }

        [TestCase("press")]
        [TestCase("move")]
        public void TryParse_RequiresAPositionForPointerPressAndMove(string mode)
        {
            var body = Body("{\"frame\":0,\"type\":\"pointer\",\"mode\":\"" + mode + "\"}");
            StringAssert.Contains("is required for pointer press and move", ParseError(body));
        }

        [TestCase("\"position\":5")]
        [TestCase("\"position\":null")]
        [TestCase("\"normalizedPosition\":\"center\"")]
        public void TryParse_RejectsAPointerReleaseWithAMalformedPosition(string field)
        {
            // A release with no coordinate legitimately reuses the current mouse position, so a
            // malformed coordinate must not be mistaken for an absent one.
            var body = Body("{\"frame\":0,\"type\":\"pointer\",\"mode\":\"release\"," + field + "}");
            var error = ParseError(body);
            StringAssert.Contains("inputs[0]", error);
            StringAssert.Contains("must be an object", error);
        }

        [Test]
        public void TryParse_RejectsBothPositionAndNormalizedPosition()
        {
            var body = Body("{\"frame\":0,\"type\":\"pointer\",\"mode\":\"press\"," +
                            "\"position\":{\"x\":1,\"y\":2},\"normalizedPosition\":{\"x\":0.5,\"y\":0.5}}");
            StringAssert.Contains("not both", ParseError(body));
        }

        [Test]
        public void TryParse_RejectsAnInvalidPointerButton()
        {
            var body = Body("{\"frame\":0,\"type\":\"pointer\",\"mode\":\"press\"," +
                            "\"position\":{\"x\":1,\"y\":2},\"button\":\"thumb\"}");
            StringAssert.Contains("Invalid button", ParseError(body));
        }

        [Test]
        public void TryParse_RejectsPointerTapWithGuidance()
        {
            var body = Body("{\"frame\":0,\"type\":\"pointer\",\"mode\":\"tap\",\"position\":{\"x\":1,\"y\":2}}");
            StringAssert.Contains("'tap' is not supported", ParseError(body));
        }

        // ── Cross-cutting ───────────────────────────────────────────────────

        [Test]
        public void TryParse_AcceptsAPrettyPrintedBody()
        {
            const string body =
                "{\n" +
                "  \"inputs\": [\n" +
                "    {\n" +
                "      \"frame\": 5,\n" +
                "      \"type\": \"perform\",\n" +
                "      \"action\": \"Player/Jump\",\n" +
                "      \"mode\": \"press\"\n" +
                "    }\n" +
                "  ]\n" +
                "}";
            List<InputReplayEventSpec> events;
            string error;
            Assert.IsTrue(Parse(body, out events, out error), error);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(5, events[0].frame);
            Assert.AreEqual("Player/Jump", events[0].action);
        }

        [Test]
        public void TryParse_PreservesRequestOrderIncludingUnsortedFrames()
        {
            List<InputReplayEventSpec> events;
            string error;
            Assert.IsTrue(Parse(Body(
                "{\"frame\":10,\"type\":\"perform\",\"action\":\"B\",\"mode\":\"press\"}",
                "{\"frame\":2,\"type\":\"perform\",\"action\":\"A\",\"mode\":\"press\"}"), out events, out error), error);
            Assert.AreEqual("B", events[0].action, "The parser must not reorder; ordering belongs to the schedule.");
            Assert.AreEqual("A", events[1].action);
        }

        [Test]
        public void TryParse_ErrorIdentifiesTheOffendingIndex()
        {
            var error = ParseError(Body(
                "{\"frame\":0,\"type\":\"perform\",\"action\":\"A\",\"mode\":\"press\"}",
                "{\"frame\":1,\"type\":\"perform\",\"action\":\"A\",\"mode\":\"press\"}",
                "{\"frame\":2,\"type\":\"perform\",\"action\":\"A\",\"mode\":\"press\"}",
                "{\"frame\":3,\"type\":\"perform\",\"mode\":\"press\"}"));
            StringAssert.Contains("inputs[3]", error);
        }
    }
}
