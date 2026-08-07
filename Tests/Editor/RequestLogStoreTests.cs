using System.IO;
using System.Text;
using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the bounded capture store behind the EditorWindow's request log.
    /// </summary>
    /// <remarks>
    /// Every test owns its own store rather than using the shared instance. A test run is itself
    /// started over HTTP, so the running server keeps writing to its store while these run, and
    /// assertions against a shared one would race the traffic driving them.
    /// </remarks>
    internal sealed class RequestLogStoreTests
    {
        private static RequestCaptureStream Capture(string contentType, int maxBytes)
            => new RequestCaptureStream(new MemoryStream(), () => contentType, maxBytes);

        private static void Write(Stream stream, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        // ── Entry lifecycle ─────────────────────────────────────────────────

        [Test]
        public void Begin_RecordsTheRequestLineHeadersAndBody()
        {
            var store = new RequestLogStore();
            var entry = store.Begin(
                new FakeRequest(
                        "POST",
                        "/api/gameobjects?scenePath=Main",
                        "http://localhost:51801")
                    .WithHeader("User-Agent", "curl/8.0")
                    .WithJsonBody("{\"name\":\"Cube\"}"));

            Assert.AreEqual("POST", entry.Method);
            Assert.AreEqual("http://localhost:51801", entry.RequestOrigin);
            Assert.AreEqual("/api/gameobjects", entry.Path);
            Assert.AreEqual("?scenePath=Main", entry.Query);
            StringAssert.Contains("User-Agent: curl/8.0", entry.RequestHeaders);
            Assert.AreEqual("{\"name\":\"Cube\"}", entry.RequestBody);
            Assert.IsFalse(entry.RequestBodyTruncated);
            Assert.IsFalse(entry.Completed);
        }

        [Test]
        public void Begin_LeavesTheBodyNullWhenThereIsNone()
        {
            var store = new RequestLogStore();
            var entry = store.Begin(new FakeRequest("GET", "/api/health"));

            Assert.IsNull(entry.RequestBody);
            Assert.AreEqual(0, entry.RequestBodyLength);
            Assert.IsFalse(entry.RequestBodyTruncated);
        }

        [Test]
        public void Begin_DoesNotReadARequestBodyOverTheCap()
        {
            var store = new RequestLogStore();
            var oversized = new string('x', RequestLogStore.MaxRequestBodyBytes + 1);
            var request = new FakeRequest("POST", "/api/test").WithJsonBody(oversized);

            var entry = store.Begin(request);

            Assert.IsTrue(entry.RequestBodyTruncated);
            Assert.IsNull(entry.RequestBody);
            Assert.AreEqual(oversized.Length, entry.RequestBodyLength);
            Assert.AreEqual(0, request.InputStreamReads,
                "An oversized body must not be read only to be discarded.");
        }

        [Test]
        public void Begin_DistinguishesAnUnreadableBodyFromAnOversizedOne()
        {
            // Both leave nothing to show, but only one is explained by the size, and saying a
            // body was too large when it was not sends a reader looking in the wrong place.
            var store = new RequestLogStore();

            var entry = store.Begin(
                new FakeRequest("POST", "/api/test")
                    .WithJsonBody("{\"a\":1}")
                    .WithUnreadableBody());

            Assert.IsTrue(entry.RequestBodyUnreadable);
            Assert.IsFalse(entry.RequestBodyTruncated);
            Assert.IsNull(entry.RequestBody);
        }

        [Test]
        public void Complete_RecordsTheResponseAndClosesTheEntry()
        {
            var store = new RequestLogStore();
            var entry = store.Begin(new FakeRequest("GET", "/api/health"));

            var capture = Capture("application/json; charset=utf-8", 1024);
            Write(capture, "{\"status\":\"ok\"}");
            entry.Complete(200, "application/json; charset=utf-8", capture);

            Assert.IsTrue(entry.Completed);
            Assert.AreEqual(200, entry.StatusCode);
            Assert.AreEqual("{\"status\":\"ok\"}", Encoding.UTF8.GetString(entry.ResponseBody));
            Assert.AreEqual(15, entry.ResponseBodyLength);
            Assert.IsTrue(entry.ResponseBodyCaptured);
            Assert.IsFalse(entry.ResponseBodyTruncated);
        }

        [Test]
        public void Complete_IsIgnoredTheSecondTime()
        {
            // The stop path can discard a context whose response already closed itself.
            var store = new RequestLogStore();
            var entry = store.Begin(new FakeRequest("GET", "/api/health"));

            entry.Complete(200, "application/json", null);
            entry.Complete(500, "text/plain", null);

            Assert.AreEqual(200, entry.StatusCode);
            Assert.AreEqual("application/json", entry.ResponseContentType);
        }

        [Test]
        public void Complete_RecordsAStatusWithNoBodyForPreflight()
        {
            // OPTIONS sets a status directly and never touches the output stream.
            var store = new RequestLogStore();
            var entry = store.Begin(new FakeRequest("OPTIONS", "/api/health"));

            entry.Complete(204, null, null);

            Assert.IsTrue(entry.Completed);
            Assert.AreEqual(204, entry.StatusCode);
            Assert.IsNull(entry.ResponseBody);
            Assert.AreEqual(0, entry.ResponseBodyLength);
        }

        [Test]
        public void Complete_MeasuresThroughToTheCloseRatherThanTheHandlerReturn()
        {
            // What a deferred response depends on: the duration must span the whole exchange.
            var store = new RequestLogStore();
            var entry = store.Begin(new FakeRequest("GET", "/api/build/artifact"));

            System.Threading.Thread.Sleep(30);
            entry.Complete(200, "application/octet-stream", null);

            Assert.GreaterOrEqual(entry.DurationMs, 25.0);
        }

        // ── Bounds ──────────────────────────────────────────────────────────

        [Test]
        public void Begin_EvictsTheOldestEntryPastTheCap()
        {
            var store = new RequestLogStore();
            for (var i = 0; i < RequestLogStore.MaxEntries + 5; i++)
                store.Begin(new FakeRequest("GET", "/api/health?i=" + i));

            var snapshot = store.Snapshot();
            Assert.AreEqual(RequestLogStore.MaxEntries, store.Count);
            Assert.AreEqual("?i=" + (RequestLogStore.MaxEntries + 4), snapshot[0].Query,
                "The snapshot is newest first.");
            Assert.AreEqual("?i=5", snapshot[snapshot.Count - 1].Query,
                "The five oldest entries must have been evicted.");
        }

        [Test]
        public void Find_ReturnsNullForAnEvictedEntry()
        {
            var store = new RequestLogStore();
            var first = store.Begin(new FakeRequest("GET", "/api/health"));
            for (var i = 0; i < RequestLogStore.MaxEntries; i++)
                store.Begin(new FakeRequest("GET", "/api/health"));

            Assert.IsNull(store.Find(first.Id));
        }

        [Test]
        public void Version_ChangesOnBeginAndOnComplete()
        {
            var store = new RequestLogStore();
            var before = store.Version;

            var entry = store.Begin(new FakeRequest("GET", "/api/health"));
            var afterBegin = store.Version;
            entry.Complete(200, "application/json", null);

            Assert.AreNotEqual(before, afterBegin);
            Assert.AreNotEqual(afterBegin, store.Version);
        }

        // ── Capture stream ──────────────────────────────────────────────────

        [Test]
        public void CaptureStream_WritesThroughToTheRealStream()
        {
            var inner = new MemoryStream();
            var capture = new RequestCaptureStream(inner, () => "application/json", 1024);

            Write(capture, "{\"a\":1}");

            Assert.AreEqual("{\"a\":1}", Encoding.UTF8.GetString(inner.ToArray()),
                "Capture must never change what the client receives.");
        }

        [Test]
        public void CaptureStream_StopsAtTheCapAndSaysSo()
        {
            var capture = Capture("application/json", 8);

            Write(capture, "0123456789");

            Assert.IsTrue(capture.Truncated);
            Assert.AreEqual(10, capture.WrittenBytes);
            Assert.AreEqual("01234567", Encoding.UTF8.GetString(capture.CapturedBytes()));
        }

        [Test]
        public void CaptureStream_TruncatesAcrossSeveralWrites()
        {
            var capture = Capture("application/json", 5);

            Write(capture, "abc");
            Write(capture, "def");
            Write(capture, "ghi");

            Assert.IsTrue(capture.Truncated);
            Assert.AreEqual(9, capture.WrittenBytes);
            Assert.AreEqual("abcde", Encoding.UTF8.GetString(capture.CapturedBytes()));
        }

        [Test]
        public void CaptureStream_MeasuresABinaryPayloadWithoutBufferingIt()
        {
            // A screenshot or an artifact download runs to megabytes and is not worth holding.
            var inner = new MemoryStream();
            var capture = new RequestCaptureStream(inner, () => "image/png", 1024);

            capture.Write(new byte[512], 0, 512);

            Assert.IsFalse(capture.IsCapturing);
            Assert.IsNull(capture.CapturedBytes());
            Assert.AreEqual(512, capture.WrittenBytes);
            Assert.AreEqual(512, inner.Length, "The payload still has to reach the client.");
        }

        [Test]
        public void CaptureStream_DoesNotFailTheResponseWhenCaptureThrows()
        {
            // The bytes have already reached the client by the time capture runs, so a failure
            // there must not surface to the handler as a failed write.
            var inner = new MemoryStream();
            var capture = new RequestCaptureStream(
                inner,
                () => { throw new System.InvalidOperationException("response is gone"); },
                1024);

            Assert.DoesNotThrow(() => Write(capture, "{\"a\":1}"));
            Assert.AreEqual("{\"a\":1}", Encoding.UTF8.GetString(inner.ToArray()));
            Assert.IsTrue(capture.CaptureFailed);
            Assert.IsFalse(capture.IsCapturing);
            Assert.AreEqual(7, capture.WrittenBytes, "The write must still be counted.");
        }

        [Test]
        public void CaptureStream_KeepsWritingThroughAfterCaptureHasFailed()
        {
            var inner = new MemoryStream();
            var capture = new RequestCaptureStream(
                inner,
                () => { throw new System.InvalidOperationException("response is gone"); },
                1024);

            Write(capture, "abc");
            Write(capture, "def");

            Assert.AreEqual("abcdef", Encoding.UTF8.GetString(inner.ToArray()));
            Assert.AreEqual(6, capture.WrittenBytes);
        }

        [Test]
        public void CaptureStream_DisposesTheUnderlyingStream()
        {
            // A handler that wraps the output in a using block - a StreamWriter over
            // ctx.Response.OutputStream - must still dispose the real stream.
            var inner = new MemoryStream();
            var capture = new RequestCaptureStream(inner, () => "application/json", 1024);

            Write(capture, "{\"a\":1}");
            capture.Dispose();

            Assert.Throws<System.ObjectDisposedException>(() => inner.WriteByte(1));
        }

        [Test]
        public void CaptureStream_KeepsTheCapturedBytesAfterDisposal()
        {
            // The entry is completed from the response's own Close, which happens after a
            // handler's using block has already disposed the stream.
            var capture = new RequestCaptureStream(
                new MemoryStream(), () => "application/json", 1024);

            Write(capture, "{\"a\":1}");
            capture.Dispose();

            Assert.AreEqual("{\"a\":1}", Encoding.UTF8.GetString(capture.CapturedBytes()));
        }

        [TestCase("application/json", true)]
        [TestCase("application/json; charset=utf-8", true)]
        [TestCase("text/plain", true)]
        [TestCase("application/xml", true)]
        [TestCase(null, true)]
        [TestCase("image/png", false)]
        [TestCase("application/octet-stream", false)]
        [TestCase("application/zip", false)]
        public void ShouldCaptureBody_KeepsTextAndSkipsBinary(string contentType, bool expected)
        {
            Assert.AreEqual(expected, RequestLogStore.ShouldCaptureBody(contentType));
        }
    }
}
