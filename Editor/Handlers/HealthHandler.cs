using System.Net;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class HealthHandler : IRequestHandler
    {
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "GET" && request.Url.AbsolutePath == "/api/health";

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var json = $"{{\"status\":\"ok\",\"unityVersion\":\"{RestResponse.EscapeJson(Application.unityVersion)}\"}}";
            RestResponse.Send(response, json);
        }
    }
}
