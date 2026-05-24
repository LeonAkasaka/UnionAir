using System.Globalization;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class GameObjectHandler : IRequestHandler
    {
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "GET" && request.Url.AbsolutePath == "/api/gameobjects";

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var path = request.QueryString["path"];
            var globalObjectId = request.QueryString["globalObjectId"];

            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene))
                return;

            if (!GameObjectUtils.TryResolveTarget(scene, globalObjectId, path, "query parameter", out var go, out var error, out var statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }
            path = GameObjectUtils.GetPath(go);

            var sb = new StringBuilder();
            var t = go.transform;
            var p = t.localPosition;
            var r = t.localEulerAngles;
            var s = t.localScale;

            sb.Append("{");
            sb.Append($"\"name\":\"{RestResponse.EscapeJson(go.name)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(path)}\",");
            sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\",");
            sb.Append($"\"isActive\":{Bool(go.activeInHierarchy)},");
            sb.Append($"\"tag\":\"{RestResponse.EscapeJson(go.tag)}\",");
            sb.Append($"\"layer\":{go.layer},");
            sb.Append("\"transform\":{");
            sb.Append($"\"position\":{{\"x\":{F(p.x)},\"y\":{F(p.y)},\"z\":{F(p.z)}}},");
            sb.Append($"\"rotation\":{{\"x\":{F(r.x)},\"y\":{F(r.y)},\"z\":{F(r.z)}}},");
            sb.Append($"\"scale\":{{\"x\":{F(s.x)},\"y\":{F(s.y)},\"z\":{F(s.z)}}}");
            sb.Append("},\"components\":[");

            var components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (i > 0) sb.Append(",");
                AppendComponent(sb, components[i]);
            }

            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }

        private static void AppendComponent(StringBuilder sb, Component component)
        {
            if (component == null)
            {
                sb.Append("{\"type\":\"null\",\"properties\":{}}");
                return;
            }

            sb.Append("{");
            sb.Append($"\"type\":\"{RestResponse.EscapeJson(component.GetType().FullName)}\",");
            sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(component))}\",");
            sb.Append("\"properties\":{");

            try
            {
                var so = new SerializedObject(component);
                var prop = so.GetIterator();
                bool firstProp = true;
                bool enterChildren = true;

                while (prop.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (prop.name == "m_Script") continue;

                    if (!firstProp) sb.Append(",");
                    firstProp = false;

                    sb.Append($"\"{RestResponse.EscapeJson(prop.name)}\":");
                    AppendPropertyValue(sb, prop);
                }
            }
            catch
            {
                // Ignore serialization errors for exotic components
            }

            sb.Append("}}");
        }

        private static void AppendPropertyValue(StringBuilder sb, SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    sb.Append(Bool(prop.boolValue));
                    break;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Enum:
                    sb.Append(prop.intValue);
                    break;
                case SerializedPropertyType.Float:
                    sb.Append(F(prop.floatValue));
                    break;
                case SerializedPropertyType.String:
                    sb.Append($"\"{RestResponse.EscapeJson(prop.stringValue)}\"");
                    break;
                case SerializedPropertyType.Color:
                    var c = prop.colorValue;
                    sb.Append($"{{\"r\":{F(c.r)},\"g\":{F(c.g)},\"b\":{F(c.b)},\"a\":{F(c.a)}}}");
                    break;
                case SerializedPropertyType.Vector2:
                    var v2 = prop.vector2Value;
                    sb.Append($"{{\"x\":{F(v2.x)},\"y\":{F(v2.y)}}}");
                    break;
                case SerializedPropertyType.Vector3:
                    var v3 = prop.vector3Value;
                    sb.Append($"{{\"x\":{F(v3.x)},\"y\":{F(v3.y)},\"z\":{F(v3.z)}}}");
                    break;
                case SerializedPropertyType.Vector4:
                    var v4 = prop.vector4Value;
                    sb.Append($"{{\"x\":{F(v4.x)},\"y\":{F(v4.y)},\"z\":{F(v4.z)},\"w\":{F(v4.w)}}}");
                    break;
                case SerializedPropertyType.Rect:
                    var rect = prop.rectValue;
                    sb.Append($"{{\"x\":{F(rect.x)},\"y\":{F(rect.y)},\"width\":{F(rect.width)},\"height\":{F(rect.height)}}}");
                    break;
                case SerializedPropertyType.ObjectReference:
                    if (prop.objectReferenceValue != null)
                        sb.Append($"{{\"type\":\"{RestResponse.EscapeJson(prop.objectReferenceValue.GetType().Name)}\",\"name\":\"{RestResponse.EscapeJson(prop.objectReferenceValue.name)}\",\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(prop.objectReferenceValue))}\"}}");
                    else
                        sb.Append("null");
                    break;
                default:
                    sb.Append("null");
                    break;
            }
        }

        private static string F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("G", CultureInfo.InvariantCulture);
        private static string Bool(bool b) => b ? "true" : "false";
    }
}
