using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// One captured HTTP exchange.
    /// </summary>
    /// <remarks>
    /// Fields are filled in two stages: <see cref="RequestLogStore.Begin"/> records everything
    /// known about the request, and <see cref="Complete"/> records the response
    /// once it closes. Nothing reads a half-written entry as if it were whole, because
    /// <see cref="Completed"/> gates the response side.
    /// </remarks>
    internal sealed class RequestLogEntry
    {
        /// <summary>Monotonic identifier within the current Editor session.</summary>
        internal long Id;

        /// <summary>When the request was dequeued for dispatch.</summary>
        internal DateTime StartedUtc;

        /// <summary><see cref="Stopwatch"/> timestamp taken alongside <see cref="StartedUtc"/>.</summary>
        internal long StartTimestamp;

        internal string Method;

        /// <summary>Scheme and authority that received the request.</summary>
        internal string RequestOrigin;

        internal string Path;
        internal string Query;

        /// <summary>Request headers, one <c>name: value</c> per line.</summary>
        internal string RequestHeaders;

        /// <summary>
        /// Request body, or null when there was none, it exceeded the cap
        /// (<see cref="RequestBodyTruncated"/>), or it could not be read
        /// (<see cref="RequestBodyUnreadable"/>).
        /// </summary>
        /// <remarks>
        /// Text rather than bytes, unlike <see cref="ResponseBody"/>: a request with a body must
        /// be <c>application/json</c>, and <see cref="RequestBodyReader"/> has already decoded it
        /// for the handler, so keeping bytes here would mean encoding a string back again.
        /// </remarks>
        internal string RequestBody;

        /// <summary>Body length the client declared, whether or not it was captured.</summary>
        internal long RequestBodyLength;

        /// <summary>Whether the body was not captured because it exceeded the cap.</summary>
        internal bool RequestBodyTruncated;

        /// <summary>
        /// Whether the body was within the cap but could not be read.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="RequestBodyTruncated"/> so that a display can say which
        /// happened. Both mean there is no body to show, but only one of them is explained by
        /// the size, and telling a reader their body was too large when it was not would send
        /// them looking in the wrong place.
        /// </remarks>
        internal bool RequestBodyUnreadable;

        internal int StatusCode;
        internal string ResponseContentType;

        /// <summary>Response bytes, or null when the payload was not captured.</summary>
        internal byte[] ResponseBody;

        /// <summary>Bytes actually written to the response, capture or not.</summary>
        internal long ResponseBodyLength;

        /// <summary>Whether the response body was captured rather than only measured.</summary>
        internal bool ResponseBodyCaptured;

        /// <summary>Whether capture stopped at the cap before the body ended.</summary>
        internal bool ResponseBodyTruncated;

        /// <summary>Whether the response has closed and the response fields are final.</summary>
        internal bool Completed;

        /// <summary>Time from dispatch to response close.</summary>
        internal double DurationMs;

        /// <summary>Store this entry belongs to.</summary>
        internal RequestLogStore Owner;

        /// <summary>
        /// Records the response and closes this entry. Later calls do nothing.
        /// </summary>
        /// <param name="statusCode">Status the response carried.</param>
        /// <param name="contentType">Response content type, or null when none was set.</param>
        /// <param name="captured">Capture stream the body was written through, or null.</param>
        internal void Complete(int statusCode, string contentType, RequestCaptureStream captured)
        {
            if (Owner != null) Owner.CompleteEntry(this, statusCode, contentType, captured);
        }
    }

    /// <summary>
    /// Bounded in-memory record of the HTTP exchanges this Editor session has served.
    /// </summary>
    /// <remarks>
    /// Written from more than one thread: most requests complete on the Unity main thread, but a
    /// deferred response - an artifact download, a replayed input sequence - finishes on a thread
    /// pool thread. Everything here is therefore lock-guarded and touches no Unity API.
    /// <para>
    /// Entries live for the current Editor session only and are lost on a domain reload.
    /// </para>
    /// </remarks>
    internal sealed class RequestLogStore
    {
        /// <summary>Exchanges retained before the oldest is evicted.</summary>
        internal const int MaxEntries = 200;

        /// <summary>Largest request body captured; larger ones are recorded as truncated.</summary>
        internal const int MaxRequestBodyBytes = 64 * 1024;

        /// <summary>Largest response body captured; capture stops at this point.</summary>
        internal const int MaxResponseBodyBytes = 256 * 1024;

        /// <summary>
        /// Store the running server writes to.
        /// </summary>
        /// <remarks>
        /// An instance rather than static state so a test can own one. The server's own store
        /// keeps receiving entries while tests run - a test run is itself started over HTTP -
        /// so assertions against a shared store would race the traffic driving them.
        /// </remarks>
        internal static readonly RequestLogStore Instance = new RequestLogStore();

        private readonly List<RequestLogEntry> _entries = new List<RequestLogEntry>(MaxEntries);
        private readonly object _lock = new object();
        private long _nextId;
        private int _version;

        /// <summary>
        /// Changes whenever an entry is added or completed.
        /// </summary>
        /// <remarks>
        /// A counter rather than an event, because completion can happen on a thread pool thread
        /// and an EditorWindow may only repaint from the main thread. The window polls this.
        /// </remarks>
        internal int Version
        {
            get { lock (_lock) { return _version; } }
        }

        /// <summary>
        /// Records an incoming request and returns the entry its response will complete.
        /// </summary>
        /// <remarks>
        /// The body is read here rather than left to the handler.
        /// <see cref="RequestBodyReader"/> caches what it reads, so a handler that reads the body
        /// afterwards receives the identical value and nothing about its behavior changes.
        /// </remarks>
        internal RequestLogEntry Begin(UnionAirRequest request)
        {
            var entry = new RequestLogEntry
            {
                StartedUtc = DateTime.UtcNow,
                StartTimestamp = Stopwatch.GetTimestamp(),
                Method = request.HttpMethod,
                RequestOrigin = request.Url.GetLeftPart(UriPartial.Authority),
                Path = request.Url.AbsolutePath,
                Query = request.Url.Query,
                RequestHeaders = FormatHeaders(request.Headers),
                RequestBodyLength = request.ContentLength64,
            };

            if (request.HasEntityBody)
            {
                // Over the cap the body is not read at all. Reading it only to discard it would
                // buy nothing and would hold a large payload in memory for no one.
                if (request.ContentLength64 > MaxRequestBodyBytes)
                {
                    entry.RequestBodyTruncated = true;
                }
                else
                {
                    try
                    {
                        entry.RequestBody = RequestBodyReader.ReadString(request);
                    }
                    catch (Exception)
                    {
                        // A body that cannot be read is the handler's problem to report; the log
                        // records that it has nothing rather than failing the request.
                        entry.RequestBodyUnreadable = true;
                    }
                }
            }

            lock (_lock)
            {
                entry.Owner = this;
                entry.Id = _nextId++;
                if (_entries.Count >= MaxEntries)
                    _entries.RemoveAt(0);
                _entries.Add(entry);
                _version++;
            }

            return entry;
        }

        /// <summary>
        /// Records the response and closes the entry. Called through
        /// <see cref="RequestLogEntry.Complete"/>, which knows its own store.
        /// </summary>
        internal void CompleteEntry(
            RequestLogEntry entry,
            int statusCode,
            string contentType,
            RequestCaptureStream captured)
        {
            if (entry == null) return;

            lock (_lock)
            {
                if (entry.Completed) return;

                entry.StatusCode = statusCode;
                entry.ResponseContentType = contentType;

                if (captured != null)
                {
                    entry.ResponseBodyLength = captured.WrittenBytes;
                    entry.ResponseBodyCaptured = captured.IsCapturing;
                    entry.ResponseBodyTruncated = captured.Truncated;
                    entry.ResponseBody = captured.CapturedBytes();
                }

                entry.DurationMs = ElapsedMilliseconds(entry.StartTimestamp);
                entry.Completed = true;
                _version++;
            }
        }

        /// <summary>Returns the retained entries, newest first.</summary>
        internal List<RequestLogEntry> Snapshot()
        {
            lock (_lock)
            {
                var result = new List<RequestLogEntry>(_entries.Count);
                for (var i = _entries.Count - 1; i >= 0; i--)
                    result.Add(_entries[i]);
                return result;
            }
        }

        /// <summary>Returns the entry with the given identifier, or null when it is gone.</summary>
        internal RequestLogEntry Find(long id)
        {
            lock (_lock)
            {
                for (var i = _entries.Count - 1; i >= 0; i--)
                    if (_entries[i].Id == id) return _entries[i];
                return null;
            }
        }

        /// <summary>Discards every retained entry.</summary>
        internal void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
                _version++;
            }
        }

        /// <summary>Number of retained entries.</summary>
        internal int Count
        {
            get { lock (_lock) { return _entries.Count; } }
        }

        /// <summary>
        /// Whether a response with this content type is worth keeping the bytes of.
        /// </summary>
        /// <remarks>
        /// Screenshots and artifact downloads run to megabytes and would be unreadable anyway, so
        /// they are measured rather than buffered. A response with no declared type is captured:
        /// UnionAir always sets one, so an absent type means something unusual that is worth
        /// seeing.
        /// </remarks>
        internal static bool ShouldCaptureBody(string contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return true;

            var separator = contentType.IndexOf(';');
            var mediaType = (separator >= 0 ? contentType.Substring(0, separator) : contentType)
                .Trim();

            return mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                   mediaType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   mediaType.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Renders headers as one <c>name: value</c> per line.</summary>
        internal static string FormatHeaders(NameValueCollection headers)
        {
            if (headers == null || headers.Count == 0) return "";

            var sb = new StringBuilder(256);
            for (var i = 0; i < headers.Count; i++)
            {
                var name = headers.GetKey(i);
                var values = headers.GetValues(i);
                if (values == null) continue;

                for (var v = 0; v < values.Length; v++)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(name).Append(": ").Append(values[v]);
                }
            }
            return sb.ToString();
        }

        private static double ElapsedMilliseconds(long startTimestamp)
        {
            var ticks = Stopwatch.GetTimestamp() - startTimestamp;
            if (ticks < 0) ticks = 0;
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

    }

    /// <summary>
    /// Write-through stream that keeps a bounded copy of what passes through it.
    /// </summary>
    /// <remarks>
    /// The inner write happens first and its result is never affected by capture: a response must
    /// not fail because something was watching it, so a failure on the capture side abandons
    /// capture rather than propagating. Whether to keep the bytes is decided on the first write
    /// rather than at construction, because the content type is set after the stream is obtained.
    /// </remarks>
    internal sealed class RequestCaptureStream : Stream
    {
        private readonly Stream _inner;
        private readonly Func<string> _contentType;
        private readonly int _maxBytes;
        private MemoryStream _buffer;
        private bool _decided;
        private bool _captureFailed;

        internal RequestCaptureStream(Stream inner, Func<string> contentType, int maxBytes)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            _inner = inner;
            _contentType = contentType;
            _maxBytes = maxBytes;
        }

        /// <summary>Bytes written through this stream, whether captured or not.</summary>
        internal long WrittenBytes { get; private set; }

        /// <summary>Whether the content type made the payload worth keeping.</summary>
        internal bool IsCapturing { get; private set; }

        /// <summary>Whether capture stopped at the cap before the body ended.</summary>
        internal bool Truncated { get; private set; }

        /// <summary>Returns the captured prefix, or null when nothing was kept.</summary>
        internal byte[] CapturedBytes() => _buffer == null ? null : _buffer.ToArray();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            WrittenBytes += count;

            // Everything past the inner write is observation. If any of it throws - reading the
            // content type from a response the client already abandoned, or allocating the buffer -
            // the client's bytes have been sent and the handler must not see a failure for it, so
            // capture is abandoned instead.
            try
            {
                Capture(buffer, offset, count);
            }
            catch (Exception)
            {
                IsCapturing = false;
                _buffer = null;
                _captureFailed = true;
            }
        }

        private void Capture(byte[] buffer, int offset, int count)
        {
            if (_captureFailed) return;

            if (!_decided)
            {
                _decided = true;
                IsCapturing = RequestLogStore.ShouldCaptureBody(
                    _contentType == null ? null : _contentType());
                if (IsCapturing) _buffer = new MemoryStream();
            }

            if (!IsCapturing || _buffer == null) return;

            var room = _maxBytes - (int)_buffer.Length;
            if (room <= 0)
            {
                Truncated = true;
                return;
            }

            if (count > room)
            {
                _buffer.Write(buffer, offset, room);
                Truncated = true;
                return;
            }

            _buffer.Write(buffer, offset, count);
        }

        public override void WriteByte(byte value)
        {
            var one = new byte[1];
            one[0] = value;
            Write(one, 0, 1);
        }

        public override void Flush() => _inner.Flush();

        /// <summary>Whether capture was abandoned because observing the write threw.</summary>
        internal bool CaptureFailed => _captureFailed;

        /// <summary>
        /// Disposes the underlying response stream, as disposing the real one would.
        /// </summary>
        /// <remarks>
        /// Without this a handler that wraps the output in a <c>using</c> - a
        /// <see cref="StreamWriter"/> over <c>ctx.Response.OutputStream</c>, say - would dispose
        /// this wrapper and silently leave the real stream open, which is not what the same code
        /// did before the response became a UnionAir type. The buffer is deliberately not
        /// released: the entry is completed afterwards, from the response's own Close, and reads
        /// the captured bytes then.
        /// </remarks>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _inner.Dispose(); }
                catch (Exception) { /* The listener may already have aborted the response. */ }
            }
            base.Dispose(disposing);
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => WrittenBytes;

        public override long Position
        {
            get { return WrittenBytes; }
            set { throw new NotSupportedException(); }
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
