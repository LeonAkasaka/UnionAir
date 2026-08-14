using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class GameObjectHandler
    {
        public void Handle(UnionAirRequest request, UnionAirResponse response)
        {
            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene))
                return;

            if (!ObjectRefUtils.TryReadQuery(request.QueryString, "target", out var target, out var error, out var statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }

            if (!ObjectRefUtils.TryResolveGameObject(scene, target, "target", out var go, out error, out statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }
            var path = GameObjectUtils.GetPath(go);

            var sb = new StringBuilder();
            var t = go.transform;
            var p = t.localPosition;
            var r = t.localEulerAngles;
            var s = t.localScale;

            sb.Append("{");
            sb.Append($"\"name\":\"{RestResponse.EscapeJson(go.name)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(path)}\",");
            sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\",");
            sb.Append($"\"isActive\":{RestResponse.FormatBool(go.activeInHierarchy)},");
            sb.Append($"\"tag\":\"{RestResponse.EscapeJson(go.tag)}\",");
            sb.Append($"\"layer\":{go.layer},");
            sb.Append("\"transform\":{");
            sb.Append($"\"position\":{{\"x\":{RestResponse.FormatFloat(p.x)},\"y\":{RestResponse.FormatFloat(p.y)},\"z\":{RestResponse.FormatFloat(p.z)}}},");
            sb.Append($"\"rotation\":{{\"x\":{RestResponse.FormatFloat(r.x)},\"y\":{RestResponse.FormatFloat(r.y)},\"z\":{RestResponse.FormatFloat(r.z)}}},");
            sb.Append($"\"scale\":{{\"x\":{RestResponse.FormatFloat(s.x)},\"y\":{RestResponse.FormatFloat(s.y)},\"z\":{RestResponse.FormatFloat(s.z)}}}");
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

            // Omitted rather than defaulted for a component that has no checkbox, so that a reader
            // can tell "this cannot be disabled" from "this is disabled".
            var enabled = ComponentEnabledState.Read(component);
            if (enabled.HasValue)
                sb.Append($"\"enabled\":{RestResponse.FormatBool(enabled.Value)},");

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
            if (prop.isArray && prop.propertyType != SerializedPropertyType.String)
            {
                sb.Append('[');
                for (int i = 0; i < prop.arraySize; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendPropertyValue(sb, prop.GetArrayElementAtIndex(i));
                }
                sb.Append(']');
                return;
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    sb.Append(RestResponse.FormatBool(prop.boolValue));
                    break;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Enum:
                    sb.Append(prop.intValue);
                    break;
                case SerializedPropertyType.Float:
                    sb.Append(RestResponse.FormatFloat(prop.floatValue));
                    break;
                case SerializedPropertyType.String:
                    sb.Append($"\"{RestResponse.EscapeJson(prop.stringValue)}\"");
                    break;
                case SerializedPropertyType.Color:
                    var c = prop.colorValue;
                    sb.Append($"{{\"r\":{RestResponse.FormatFloat(c.r)},\"g\":{RestResponse.FormatFloat(c.g)},\"b\":{RestResponse.FormatFloat(c.b)},\"a\":{RestResponse.FormatFloat(c.a)}}}");
                    break;
                case SerializedPropertyType.Vector2:
                    var v2 = prop.vector2Value;
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(v2.x)},\"y\":{RestResponse.FormatFloat(v2.y)}}}");
                    break;
                case SerializedPropertyType.Vector3:
                    var v3 = prop.vector3Value;
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(v3.x)},\"y\":{RestResponse.FormatFloat(v3.y)},\"z\":{RestResponse.FormatFloat(v3.z)}}}");
                    break;
                case SerializedPropertyType.Vector4:
                    var v4 = prop.vector4Value;
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(v4.x)},\"y\":{RestResponse.FormatFloat(v4.y)},\"z\":{RestResponse.FormatFloat(v4.z)},\"w\":{RestResponse.FormatFloat(v4.w)}}}");
                    break;
                case SerializedPropertyType.Rect:
                    var rect = prop.rectValue;
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(rect.x)},\"y\":{RestResponse.FormatFloat(rect.y)},\"width\":{RestResponse.FormatFloat(rect.width)},\"height\":{RestResponse.FormatFloat(rect.height)}}}");
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

    }
}
