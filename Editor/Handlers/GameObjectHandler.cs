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
            sb.Append("},");
            AppendWorldTransform(sb, t);
            sb.Append("\"components\":[");

            var components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (i > 0) sb.Append(",");
                AppendComponent(sb, components[i]);
            }

            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }

        // Beside 'transform' rather than replacing it: 'transform' is the local one the write
        // accepts, and a query parameter that changes what an existing field means would make the
        // response depend on how it was asked for.
        //
        // The basis vectors are the half that makes this usable. A world rotation as Euler angles
        // is enough in principle and not in practice — a bone whose local +X points down in world
        // space reads as (270.8, 90, 0), and recovering the direction from that means the client
        // redoing Unity's quaternion-to-basis conversion. Reported as unit vectors, the answer to
        // "which way is up for this bone" is one field.
        private static void AppendWorldTransform(StringBuilder sb, Transform t)
        {
            var p = t.position;
            var r = t.rotation.eulerAngles;

            // Unity's own name, and the honest one: it is derived from the whole chain and cannot
            // be written back, where 'scale' would invite a client to echo it into a PATCH.
            var s = t.lossyScale;

            sb.Append("\"worldTransform\":{");
            sb.Append($"\"position\":{Vector3Json(p)},");
            sb.Append($"\"rotation\":{Vector3Json(r)},");
            sb.Append($"\"lossyScale\":{Vector3Json(s)},");
            sb.Append($"\"right\":{Vector3Json(t.right)},");
            sb.Append($"\"up\":{Vector3Json(t.up)},");
            sb.Append($"\"forward\":{Vector3Json(t.forward)}");
            sb.Append("},");
        }

        // Renderer.bounds is the world-space AABB Unity computes, and no serialized property
        // carries it: m_AABB is the local bounds and a different value. Beside 'properties' for
        // the reason 'enabled' and 'blendShapeNames' are.
        private static void AppendRendererBounds(StringBuilder sb, Component component)
        {
            var renderer = component as Renderer;
            if (renderer == null) return;

            var bounds = renderer.bounds;
            sb.Append("\"bounds\":{");
            sb.Append($"\"center\":{Vector3Json(bounds.center)},");
            sb.Append($"\"extents\":{Vector3Json(bounds.extents)}");
            sb.Append("},");
        }

        private static string Vector3Json(Vector3 v)
            => $"{{\"x\":{RestResponse.FormatFloat(v.x)},\"y\":{RestResponse.FormatFloat(v.y)},\"z\":{RestResponse.FormatFloat(v.z)}}}";

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

            AppendBlendShapeNames(sb, component);
            AppendRendererBounds(sb, component);

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

        // Beside 'properties' rather than in it, for the reason 'enabled' is: a blend shape name is
        // not a serialized property of the renderer at all. It belongs to the Mesh, so announcing it
        // as a property would name a key PATCH /api/gameobjects/components has to refuse.
        //
        // Positional, in mesh order, so the same integer indexes this and m_BlendShapeWeights. The
        // weights are not repeated here -- they are already reported as the property the write
        // accepts -- and a name-keyed object is not an option, because two shapes on one mesh may
        // share a name and one of them would disappear.
        private static void AppendBlendShapeNames(StringBuilder sb, Component component)
        {
            var renderer = component as SkinnedMeshRenderer;
            if (renderer == null) return;

            sb.Append("\"blendShapeNames\":[");
            var mesh = renderer.sharedMesh;
            if (mesh != null)
            {
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append($"\"{RestResponse.EscapeJson(mesh.GetBlendShapeName(i))}\"");
                }
            }
            sb.Append("],");
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
                    // Spelled the way PATCH /api/gameobjects/components reads it, so a value
                    // taken from here can be sent back without translation. That endpoint
                    // resolves scene objects, so they are reported rather than dropped.
                    SerializedPropertySerializer.AppendObjectReferenceJson(
                        sb, prop.objectReferenceValue, true);
                    break;
                default:
                    sb.Append("null");
                    break;
            }
        }

    }
}
