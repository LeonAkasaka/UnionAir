using System;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Incoming HTTP request as UnionAir presents it to endpoint handlers.
    /// </summary>
    /// <remarks>
    /// This is an abstract class rather than an interface, and its constructor is internal, so
    /// only UnionAir and the assemblies listed in <c>InternalsVisibleTo</c> can implement it.
    /// Members can therefore be added later without breaking anyone, and tests can supply a
    /// substitute: <see cref="HttpListenerRequest"/> is sealed with no public constructor, which
    /// is why nothing that accepted it directly could be exercised without a live server.
    /// </remarks>
    public abstract class UnionAirRequest
    {
        internal UnionAirRequest()
        {
        }

        /// <summary>HTTP method in upper case, such as <c>GET</c> or <c>POST</c>.</summary>
        public abstract string HttpMethod { get; }

        /// <summary>Full request URL, including scheme, authority, path, and query.</summary>
        public abstract Uri Url { get; }

        /// <summary>Parsed query string values.</summary>
        public abstract NameValueCollection QueryString { get; }

        /// <summary>Request headers.</summary>
        public abstract NameValueCollection Headers { get; }

        /// <summary>Whether the request carries an entity body.</summary>
        public abstract bool HasEntityBody { get; }

        /// <summary>Declared media type of the body, including any parameters.</summary>
        public abstract string ContentType { get; }

        /// <summary>Declared body length in bytes, or 0 when there is no body.</summary>
        public abstract long ContentLength64 { get; }

        /// <summary>Encoding declared for the body.</summary>
        /// <remarks>
        /// Internal for the same reason as <see cref="InputStream"/>: it exists to serve
        /// <see cref="RequestBodyReader"/> and is not part of the handler-facing contract.
        /// </remarks>
        internal abstract Encoding ContentEncoding { get; }

        /// <summary>Raw body stream.</summary>
        /// <remarks>
        /// Deliberately internal. <see cref="RequestBodyReader"/> is the only consumer, and it
        /// caches what it reads so that every reader of a given request sees the same body. A
        /// handler that consumed this stream directly would leave nothing for anything else to
        /// observe, so the single reader is the contract rather than a convention.
        /// </remarks>
        internal abstract Stream InputStream { get; }
    }

    /// <summary>
    /// Presents an <see cref="HttpListenerRequest"/> as a <see cref="UnionAirRequest"/>.
    /// </summary>
    internal sealed class HttpListenerRequestAdapter : UnionAirRequest
    {
        private readonly HttpListenerRequest _request;

        internal HttpListenerRequestAdapter(HttpListenerRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            _request = request;
        }

        public override string HttpMethod => _request.HttpMethod;

        public override Uri Url => _request.Url;

        public override NameValueCollection QueryString => _request.QueryString;

        public override NameValueCollection Headers => _request.Headers;

        public override bool HasEntityBody => _request.HasEntityBody;

        public override string ContentType => _request.ContentType;

        public override long ContentLength64 => _request.ContentLength64;

        internal override Encoding ContentEncoding => _request.ContentEncoding;

        internal override Stream InputStream => _request.InputStream;
    }
}
