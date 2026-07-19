using System.Collections.Generic;
using System.Net;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Provides request, response, route values, and endpoint metadata to a UnionAir controller method.
    /// </summary>
    /// <remarks>
    /// Custom endpoints receive this context as their only method parameter. Body parsing and response writing
    /// are explicit so custom handlers can choose the helper APIs that fit their payload format.
    /// </remarks>
    public sealed class UnionAirRequestContext
    {
        internal UnionAirRequestContext(
            HttpListenerRequest request,
            HttpListenerResponse response,
            Dictionary<string, string> routeValues,
            UnionAirEndpointDescriptor endpoint)
        {
            Request = request;
            Response = response;
            RouteValues = routeValues;
            Endpoint = endpoint;
        }

        /// <summary>
        /// Incoming HTTP request.
        /// </summary>
        public HttpListenerRequest Request { get; }

        /// <summary>
        /// HTTP response that the endpoint should write to.
        /// </summary>
        public HttpListenerResponse Response { get; }

        /// <summary>
        /// Values captured from route template parameters.
        /// </summary>
        /// <example>
        /// For route <c>/api/assets/{guid}</c>, this dictionary contains a <c>guid</c> entry.
        /// </example>
        public Dictionary<string, string> RouteValues { get; }

        /// <summary>
        /// Metadata descriptor for the matched endpoint.
        /// </summary>
        public UnionAirEndpointDescriptor Endpoint { get; }

        /// <summary>
        /// Gets whether the handler has taken ownership of the response lifetime via <see cref="Defer"/>.
        /// </summary>
        internal bool IsDeferred { get; private set; }

        /// <summary>
        /// Marks the response as deferred: the server will not close it when the handler returns.
        /// The handler then owns the response and must eventually write it (for example with
        /// <see cref="RestResponse.Send"/>) and call <c>Response.Close()</c> itself. Background
        /// response I/O must not call Unity APIs.
        /// </summary>
        public void Defer()
        {
            IsDeferred = true;
        }
    }
}
