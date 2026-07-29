using NUnit.Framework;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers screen-coordinate parsing and resolution for the pointer and hit-test endpoints.
    /// </summary>
    /// <remarks>
    /// Parsing is separated from resolution so both halves can run without a Game view: these
    /// tests pass the resolution in explicitly rather than reading <c>Screen</c>.
    /// </remarks>
    internal sealed class ScreenPointUtilsTests
    {
        // ── TryParse ────────────────────────────────────────────────────────

        [Test]
        public void TryParse_RejectsBothPositionAndNormalizedPosition()
        {
            ScreenPointRequest point;
            string error;
            int statusCode;
            Assert.IsFalse(ScreenPointUtils.TryParse(
                "{\"position\":{\"x\":1,\"y\":2},\"normalizedPosition\":{\"x\":0.5,\"y\":0.5}}",
                out point, out error, out statusCode));
            Assert.AreEqual(400, statusCode);
            StringAssert.Contains("not both", error);
        }

        [Test]
        public void TryParse_RejectsNeitherPositionNorNormalizedPosition()
        {
            ScreenPointRequest point;
            string error;
            int statusCode;
            Assert.IsFalse(ScreenPointUtils.TryParse("{\"origin\":\"topLeft\"}", out point, out error, out statusCode));
            Assert.AreEqual(400, statusCode);
        }

        [TestCase("{\"position\":5}")]
        [TestCase("{\"position\":null}")]
        [TestCase("{\"position\":\"center\"}")]
        [TestCase("{\"normalizedPosition\":[0.5,0.5]}")]
        public void TryParse_RejectsACoordinateThatIsNotAnObject(string body)
        {
            // A malformed coordinate must not read as an absent one: pointer release treats an
            // absent coordinate as "use the current position", so a typo would otherwise change
            // behaviour silently instead of failing.
            ScreenPointRequest point;
            string error;
            int statusCode;
            Assert.IsFalse(ScreenPointUtils.TryParse(body, out point, out error, out statusCode));
            Assert.AreEqual(400, statusCode);
            StringAssert.Contains("must be an object", error);
        }

        [Test]
        public void TryParse_RejectsBothFieldsEvenWhenOneIsMalformed()
        {
            ScreenPointRequest point;
            string error;
            int statusCode;
            Assert.IsFalse(ScreenPointUtils.TryParse(
                "{\"position\":null,\"normalizedPosition\":{\"x\":0.5,\"y\":0.5}}",
                out point, out error, out statusCode));
            Assert.AreEqual(400, statusCode);
            StringAssert.Contains("not both", error);
        }

        [Test]
        public void TryParse_RejectsInvalidOrigin()
        {
            ScreenPointRequest point;
            string error;
            int statusCode;
            Assert.IsFalse(ScreenPointUtils.TryParse(
                "{\"position\":{\"x\":1,\"y\":2},\"origin\":\"middle\"}", out point, out error, out statusCode));
            Assert.AreEqual(400, statusCode);
            StringAssert.Contains("bottomLeft or topLeft", error);
        }

        [Test]
        public void TryParse_DefaultsToBottomLeftOrigin()
        {
            ScreenPointRequest point;
            string error;
            int statusCode;
            Assert.IsTrue(ScreenPointUtils.TryParse("{\"position\":{\"x\":1,\"y\":2}}", out point, out error, out statusCode));
            Assert.IsFalse(point.TopLeft);
            Assert.IsFalse(point.IsNormalized);
            Assert.AreEqual(1f, point.X, 1e-6f);
            Assert.AreEqual(2f, point.Y, 1e-6f);
        }

        [TestCase("{\"position\":{\"x\":1}}")]
        [TestCase("{\"position\":{\"x\":1,\"y\":NaN}}")]
        [TestCase("{\"position\":{\"x\":Infinity,\"y\":2}}")]
        public void TryParse_RejectsMissingOrNonFiniteCoordinates(string body)
        {
            ScreenPointRequest point;
            string error;
            int statusCode;
            Assert.IsFalse(ScreenPointUtils.TryParse(body, out point, out error, out statusCode));
            Assert.AreEqual(400, statusCode);
        }

        [Test]
        public void TryParse_ReadsAPrettyPrintedBody()
        {
            const string body = "{\n  \"normalizedPosition\": {\n    \"x\": 0.25,\n    \"y\": 0.75\n  },\n  \"origin\": \"topLeft\"\n}";
            ScreenPointRequest point;
            string error;
            int statusCode;
            Assert.IsTrue(ScreenPointUtils.TryParse(body, out point, out error, out statusCode));
            Assert.IsTrue(point.IsNormalized);
            Assert.IsTrue(point.TopLeft);
            Assert.AreEqual(0.25f, point.X, 1e-6f);
            Assert.AreEqual(0.75f, point.Y, 1e-6f);
        }

        // ── Resolve ─────────────────────────────────────────────────────────

        [Test]
        public void Resolve_ReturnsPixelsUnchangedForBottomLeft()
        {
            Vector2 resolved;
            string error;
            int statusCode;
            Assert.IsTrue(ScreenPointUtils.Resolve(
                new ScreenPointRequest(false, false, 100f, 50f), 1280, 720, out resolved, out error, out statusCode));
            Assert.AreEqual(100f, resolved.x, 1e-6f);
            Assert.AreEqual(50f, resolved.y, 1e-6f);
        }

        [Test]
        public void Resolve_FlipsTopLeftOrigin()
        {
            Vector2 resolved;
            string error;
            int statusCode;
            Assert.IsTrue(ScreenPointUtils.Resolve(
                new ScreenPointRequest(false, true, 100f, 50f), 1280, 720, out resolved, out error, out statusCode));
            Assert.AreEqual(100f, resolved.x, 1e-6f);
            Assert.AreEqual(670f, resolved.y, 1e-6f);
        }

        [Test]
        public void Resolve_ScalesNormalizedPositions()
        {
            Vector2 resolved;
            string error;
            int statusCode;
            Assert.IsTrue(ScreenPointUtils.Resolve(
                new ScreenPointRequest(true, false, 0.5f, 0.25f), 1280, 720, out resolved, out error, out statusCode));
            Assert.AreEqual(640f, resolved.x, 1e-6f);
            Assert.AreEqual(180f, resolved.y, 1e-6f);
        }

        [Test]
        public void Resolve_FlipsNormalizedTopLeftPositions()
        {
            Vector2 resolved;
            string error;
            int statusCode;
            Assert.IsTrue(ScreenPointUtils.Resolve(
                new ScreenPointRequest(true, true, 0.5f, 0.25f), 1280, 720, out resolved, out error, out statusCode));
            Assert.AreEqual(640f, resolved.x, 1e-6f);
            Assert.AreEqual(540f, resolved.y, 1e-6f);
        }

        [Test]
        public void Resolve_ClampsNormalizedPositionsInsteadOfFailing()
        {
            Vector2 resolved;
            string error;
            int statusCode;
            Assert.IsTrue(ScreenPointUtils.Resolve(
                new ScreenPointRequest(true, false, 2f, -1f), 1280, 720, out resolved, out error, out statusCode));
            Assert.AreEqual(1280f, resolved.x, 1e-6f);
            Assert.AreEqual(0f, resolved.y, 1e-6f);
        }

        [Test]
        public void Resolve_RejectsOutOfBoundsPixelsWith422()
        {
            Vector2 resolved;
            string error;
            int statusCode;
            Assert.IsFalse(ScreenPointUtils.Resolve(
                new ScreenPointRequest(false, false, 2000f, 50f), 1280, 720, out resolved, out error, out statusCode));
            Assert.AreEqual(422, statusCode);
            StringAssert.Contains("outside the screen", error);
        }
    }
}
