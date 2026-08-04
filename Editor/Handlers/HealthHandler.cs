using System.Net;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class HealthHandler
    {
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var json = BuildResponseJson(
                Application.unityVersion,
                UnionAirProjectPaths.ProjectRoot);
            RestResponse.Send(response, json);
        }

        internal static string BuildResponseJson(string unityVersion, string projectPath)
            => $"{{\"status\":\"ok\",\"unityVersion\":\"{RestResponse.EscapeJson(unityVersion)}\"," +
               $"\"projectPath\":\"{RestResponse.EscapeJson(projectPath)}\"}}";
    }
}
