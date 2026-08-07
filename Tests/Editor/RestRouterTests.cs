using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the transport-level gates <see cref="RestRouter"/> applies before a handler runs.
    /// </summary>
    /// <remarks>
    /// None of this was reachable while the router took the sealed framework request and response
    /// types: they cannot be constructed, so the only way to exercise a gate was to start a server
    /// and call it. The gates that depend on Editor state - the Play Mode opt-in, the test-run
    /// rejection, and the disabled-category response - still need that state to be arranged and
    /// are not covered here.
    /// </remarks>
    internal sealed class RestRouterTests
    {
        [Test]
        public void Handle_RejectsARequestThatCarriesAnOriginHeader()
        {
            var response = new FakeResponse();
            var completed = new RestRouter().Handle(
                new FakeRequest("GET", "/api/health").WithHeader("Origin", "http://evil.example"),
                response);

            Assert.IsTrue(completed);
            Assert.AreEqual(403, response.StatusCode);
            StringAssert.Contains("Browser-originated", response.Body);
        }

        [Test]
        public void Handle_AnswersPreflightWithoutABody()
        {
            var response = new FakeResponse();
            var completed = new RestRouter().Handle(
                new FakeRequest("OPTIONS", "/api/health"), response);

            Assert.IsTrue(completed);
            Assert.AreEqual(204, response.StatusCode);
            Assert.AreEqual("", response.Body);
        }

        [Test]
        public void Handle_AnswersAnUnknownPathWithNotFound()
        {
            var response = new FakeResponse();
            var completed = new RestRouter().Handle(
                new FakeRequest("GET", "/api/no-such-endpoint"), response);

            Assert.IsTrue(completed);
            Assert.AreEqual(404, response.StatusCode);
            StringAssert.Contains("/api/no-such-endpoint", response.Body);
        }

        [Test]
        public void Handle_AnswersAKnownPathWithTheWrongMethodAsMethodNotAllowed()
        {
            // The path is matched before the method is, so this is 405 rather than 404 even
            // when the endpoint's category is disabled.
            var response = new FakeResponse();
            var completed = new RestRouter().Handle(
                new FakeRequest("DELETE", "/api/health"), response);

            Assert.IsTrue(completed);
            Assert.AreEqual(405, response.StatusCode);
            StringAssert.Contains("/api/health", response.Body);
        }
    }
}
