using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class EditorStatusHandler
    {
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"isPlaying\":{RestResponse.FormatBool(EditorApplication.isPlaying)},");
            sb.Append($"\"isPaused\":{RestResponse.FormatBool(EditorApplication.isPaused)},");
            sb.Append($"\"isCompiling\":{RestResponse.FormatBool(EditorApplication.isCompiling)},");
            sb.Append($"\"isUpdating\":{RestResponse.FormatBool(EditorApplication.isUpdating)},");
            sb.Append($"\"unityVersion\":\"{RestResponse.EscapeJson(Application.unityVersion)}\",");
            sb.Append($"\"isTestRunning\":{RestResponse.FormatBool(UnionAirTestRunGate.IsActive)},");
            sb.Append("\"testRunSource\":").Append(RestResponse.FormatNullableString(UnionAirTestRunGate.PublicSource));
            sb.Append(",\"testRunId\":").Append(RestResponse.FormatNullableString(UnionAirTestRunGate.PublicRunId));
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }
    }
}
