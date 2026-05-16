using System.Net;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class HealthHandler : IRequestHandler
    {
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "GET" && request.Url.AbsolutePath == "/api/health";

        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var json = $"{{\"status\":\"ok\",\"unityVersion\":\"{Application.unityVersion}\"}}";
            RestResponse.Send(response, json);
        }
    }
}
