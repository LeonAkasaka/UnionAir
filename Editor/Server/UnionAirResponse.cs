using System;
using System.IO;
using System.Net;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Outgoing HTTP response as UnionAir presents it to endpoint handlers.
    /// </summary>
    /// <remarks>
    /// Abstract with an internal constructor for the reasons given on <see cref="UnionAirRequest"/>.
    /// Hiding <see cref="HttpListenerResponse"/> also means every byte a handler writes passes
    /// through a type UnionAir owns. <see cref="HttpListenerResponse.OutputStream"/> has no setter
    /// and the type is sealed, so a substitute stream cannot be injected into it; presenting the
    /// response through this type is what makes the write path observable at all.
    /// </remarks>
    public abstract class UnionAirResponse
    {
        internal UnionAirResponse()
        {
        }

        /// <summary>HTTP status code to send.</summary>
        public abstract int StatusCode { get; set; }

        /// <summary>Media type of the response body.</summary>
        public abstract string ContentType { get; set; }

        /// <summary>Body length in bytes, set before the body is written.</summary>
        public abstract long ContentLength64 { get; set; }

        /// <summary>Stream the body is written to.</summary>
        public abstract Stream OutputStream { get; }

        /// <summary>Adds a response header.</summary>
        /// <param name="name">Header name.</param>
        /// <param name="value">Header value.</param>
        public abstract void AddHeader(string name, string value);

        /// <summary>Completes the response and releases it.</summary>
        /// <remarks>
        /// Callers that deferred the response through <see cref="UnionAirRequestContext.Defer"/>
        /// own this call. It may run on a background thread.
        /// </remarks>
        public abstract void Close();
    }

    /// <summary>
    /// Presents an <see cref="HttpListenerResponse"/> as a <see cref="UnionAirResponse"/>.
    /// </summary>
    internal sealed class HttpListenerResponseAdapter : UnionAirResponse
    {
        private readonly HttpListenerResponse _response;

        internal HttpListenerResponseAdapter(HttpListenerResponse response)
        {
            if (response == null) throw new ArgumentNullException("response");
            _response = response;
        }

        public override int StatusCode
        {
            get { return _response.StatusCode; }
            set { _response.StatusCode = value; }
        }

        public override string ContentType
        {
            get { return _response.ContentType; }
            set { _response.ContentType = value; }
        }

        public override long ContentLength64
        {
            get { return _response.ContentLength64; }
            set { _response.ContentLength64 = value; }
        }

        public override Stream OutputStream => _response.OutputStream;

        public override void AddHeader(string name, string value)
            => _response.AddHeader(name, value);

        public override void Close() => _response.Close();
    }
}
