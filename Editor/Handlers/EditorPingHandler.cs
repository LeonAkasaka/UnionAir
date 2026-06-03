using System.Net;
using System.Text;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class EditorPingHandler
    {
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var targetJson = RequestBodyReader.GetObject(body, "target");
            if (string.IsNullOrEmpty(targetJson))
            {
                RestResponse.SendError(response, "Missing required field: target", 400);
                return;
            }

            if (!EditorTargetUtils.TryResolveTarget(
                    targetJson,
                    RequestBodyReader.GetString(body, "scenePath"),
                    "target",
                    out var target,
                    out var error,
                    out var statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }

            EditorGUIUtility.PingObject(target);

            var sb = new StringBuilder();
            sb.Append("{\"pinged\":true,\"target\":");
            EditorTargetUtils.AppendObjectJson(sb, target);
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }
    }
}
