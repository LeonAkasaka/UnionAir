using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles read-only screen-coordinate queries during Play mode, reporting what
    /// a pointer event at a given point would hit (EventSystem raycast + Physics raycast).
    /// </summary>
    internal static class PlayModeScreenHandler
    {
        public static void HandleHitTest(UnionAirRequest request, UnionAirResponse response)
        {
            if (!EditorApplication.isPlaying)
            {
                RestResponse.SendError(response, "Not in Play mode.", 409);
                return;
            }

            var body = RequestBodyReader.ReadString(request);
            if (!ScreenPointUtils.TryResolve(body, out var screenPos, out var screenWidth, out var screenHeight,
                    out var error, out var statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }

            var eventSystem = EventSystem.current;
            var camera = Camera.main;
            if (eventSystem == null && camera == null)
            {
                RestResponse.SendError(response,
                    "Neither an active EventSystem nor Camera.main is available; nothing to raycast against.", 422);
                return;
            }

            var sb = new StringBuilder();
            sb.Append("{\"success\":true");
            sb.Append(",\"position\":{\"x\":").Append(RestResponse.FormatFloat(screenPos.x))
              .Append(",\"y\":").Append(RestResponse.FormatFloat(screenPos.y)).Append("}");
            sb.Append(",\"screenSize\":{\"width\":").Append(screenWidth)
              .Append(",\"height\":").Append(screenHeight).Append("}");

            AppendEventSystemHits(sb, eventSystem, screenPos);
            AppendPhysicsHit(sb, camera, screenPos);

            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        static void AppendEventSystemHits(StringBuilder sb, EventSystem eventSystem, Vector2 screenPos)
        {
            if (eventSystem == null)
            {
                sb.Append(",\"eventSystemHits\":null");
                return;
            }

            var pointer = new PointerEventData(eventSystem) { position = screenPos };
            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(pointer, results);

            sb.Append(",\"eventSystemHits\":[");
            var first = true;
            for (int i = 0; i < results.Count; i++)
            {
                var hit = results[i];
                if (hit.gameObject == null) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append("{\"path\":\"").Append(RestResponse.EscapeJson(GameObjectUtils.GetPath(hit.gameObject))).Append("\"");
                sb.Append(",\"globalObjectId\":\"").Append(RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(hit.gameObject))).Append("\"");
                sb.Append(",\"module\":\"").Append(RestResponse.EscapeJson(hit.module != null ? hit.module.GetType().FullName : "")).Append("\"");
                sb.Append(",\"distance\":").Append(RestResponse.FormatFloat(hit.distance));
                sb.Append("}");
            }
            sb.Append("]");
        }

        static void AppendPhysicsHit(StringBuilder sb, Camera camera, Vector2 screenPos)
        {
            sb.Append(",\"physicsCamera\":");
            if (camera == null)
            {
                sb.Append("null,\"physicsHit\":null");
                return;
            }
            sb.Append("\"").Append(RestResponse.EscapeJson(GameObjectUtils.GetPath(camera.gameObject))).Append("\"");

            sb.Append(",\"physicsHit\":");
            if (!Physics.Raycast(camera.ScreenPointToRay(screenPos), out var hit))
            {
                sb.Append("null");
                return;
            }

            var go = hit.collider.gameObject;
            sb.Append("{\"path\":\"").Append(RestResponse.EscapeJson(GameObjectUtils.GetPath(go))).Append("\"");
            sb.Append(",\"globalObjectId\":\"").Append(RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))).Append("\"");
            sb.Append(",\"distance\":").Append(RestResponse.FormatFloat(hit.distance));
            sb.Append(",\"point\":[")
              .Append(RestResponse.FormatFloat(hit.point.x)).Append(",")
              .Append(RestResponse.FormatFloat(hit.point.y)).Append(",")
              .Append(RestResponse.FormatFloat(hit.point.z)).Append("]");
            sb.Append("}");
        }
    }
}
