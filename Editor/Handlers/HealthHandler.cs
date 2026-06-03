using System.Net;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class HealthHandler
    {
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var json = $"{{\"status\":\"ok\",\"unityVersion\":\"{RestResponse.EscapeJson(Application.unityVersion)}\"}}";
            RestResponse.Send(response, json);
        }
    }
}
