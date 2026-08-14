using System;
using System.Collections.Specialized;
using System.IO;
using System.Text;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// In-memory <see cref="UnionAirRequest"/> for tests.
    /// </summary>
    /// <remarks>
    /// This exists because <see cref="UnionAirRequest"/> replaced a sealed framework type that
    /// could not be constructed. Everything that takes a request was previously reachable only
    /// through a live HTTP server, so it was tested by extracting pure helpers around it instead.
    /// </remarks>
    internal sealed class FakeRequest : UnionAirRequest
    {
        private readonly NameValueCollection _query = new NameValueCollection();
        private readonly NameValueCollection _headers = new NameValueCollection();
        private readonly string _method;
        private string _contentType;
        private byte[] _body = new byte[0];
        private bool _inputStreamThrows;

        internal FakeRequest(
            string method = "GET",
            string pathAndQuery = "/api/health",
            string origin = "http://localhost:8765")
        {
            _method = method;
            Url = new Uri(origin.TrimEnd('/') + pathAndQuery);
            ParseQuery(Url.Query, _query);
        }

        public override string HttpMethod => _method;

        public override Uri Url { get; }

        public override NameValueCollection QueryString => _query;

        public override NameValueCollection Headers => _headers;

        public override bool HasEntityBody => _body.Length > 0;

        public override string ContentType => _contentType;

        public override long ContentLength64 => _body.Length;

        internal override Encoding ContentEncoding => Encoding.UTF8;

        internal override Stream InputStream
        {
            get
            {
                InputStreamReads++;
                if (_inputStreamThrows)
                    throw new IOException("The client abandoned the request.");
                return new MemoryStream(_body, false);
            }
        }

        /// <summary>Makes reading the body fail, as a dropped connection would.</summary>
        internal FakeRequest WithUnreadableBody()
        {
            _inputStreamThrows = true;
            return this;
        }

        /// <summary>
        /// Number of times the body stream was opened, so that body caching can be asserted
        /// rather than assumed.
        /// </summary>
        internal int InputStreamReads { get; private set; }

        /// <summary>Sets a JSON body and the content type the API requires for one.</summary>
        internal FakeRequest WithJsonBody(string json)
        {
            _body = Encoding.UTF8.GetBytes(json ?? "");
            return WithContentType("application/json");
        }

        /// <summary>Sets a body without touching the content type, for content-type tests.</summary>
        internal FakeRequest WithRawBody(string body, string contentType)
        {
            _body = Encoding.UTF8.GetBytes(body ?? "");
            return WithContentType(contentType);
        }

        internal FakeRequest WithHeader(string name, string value)
        {
            _headers.Add(name, value);
            return this;
        }

        private FakeRequest WithContentType(string contentType)
        {
            _contentType = contentType;
            return this;
        }

        // Percent-escapes are decoded, because the framework's QueryString hands handlers the
        // decoded value and a fake that did not would make an endpoint taking a JSON object as a
        // query parameter -- ?target={"type":...} -- look broken when it is not.
        private static void ParseQuery(string query, NameValueCollection into)
        {
            if (string.IsNullOrEmpty(query)) return;
            foreach (var pair in query.TrimStart('?').Split('&'))
            {
                if (pair.Length == 0) continue;
                var separator = pair.IndexOf('=');
                if (separator < 0) into.Add(Uri.UnescapeDataString(pair), "");
                else into.Add(
                    Uri.UnescapeDataString(pair.Substring(0, separator)),
                    Uri.UnescapeDataString(pair.Substring(separator + 1)));
            }
        }
    }

    /// <summary>
    /// In-memory <see cref="UnionAirResponse"/> that keeps what was written for assertions.
    /// </summary>
    internal sealed class FakeResponse : UnionAirResponse
    {
        private readonly MemoryStream _body = new MemoryStream();
        private readonly NameValueCollection _headers = new NameValueCollection();

        public override int StatusCode { get; set; } = 200;

        public override string ContentType { get; set; }

        public override long ContentLength64 { get; set; }

        public override Stream OutputStream => _body;

        /// <summary>Number of times <see cref="Close"/> was called.</summary>
        internal int CloseCount { get; private set; }

        /// <summary>Response body decoded as UTF-8.</summary>
        internal string Body => Encoding.UTF8.GetString(_body.ToArray());

        internal string Header(string name) => _headers[name];

        public override void AddHeader(string name, string value) => _headers[name] = value;

        public override void Close() => CloseCount++;
    }
}
