using System.Collections.Generic;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class EditorSelectionHandler
    {
        public void Handle(UnionAirRequest request, UnionAirResponse response)
        {
            if (request.HttpMethod == "GET")
            {
                RestResponse.Send(response, EditorTargetUtils.SelectionJson());
                return;
            }

            HandlePost(request, response);
        }

        private static void HandlePost(UnionAirRequest request, UnionAirResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            if (RequestBodyReader.GetBool(body, "clear") == true)
            {
                Selection.objects = new UnityEngine.Object[0];
                RestResponse.Send(response, EditorTargetUtils.SelectionJson());
                return;
            }

            var targets = new List<string>();
            var singleTarget = RequestBodyReader.GetObject(body, "target");
            var multiTargets = RequestBodyReader.GetArray(body, "targets");
            if (!string.IsNullOrEmpty(singleTarget) && multiTargets.Count > 0)
            {
                RestResponse.SendError(response, "Use either target or targets, not both.", 400);
                return;
            }

            if (!string.IsNullOrEmpty(singleTarget))
                targets.Add(singleTarget);

            targets.AddRange(multiTargets);
            if (targets.Count == 0)
            {
                RestResponse.SendError(response, "Body requires target, targets, or clear=true.", 400);
                return;
            }

            var activeIndex = RequestBodyReader.GetInt(body, "activeIndex") ?? 0;
            if (activeIndex < 0 || activeIndex >= targets.Count)
            {
                RestResponse.SendError(response, "activeIndex is outside the target range.", 400);
                return;
            }

            var defaultScenePath = RequestBodyReader.GetString(body, "scenePath");
            var objects = new UnityEngine.Object[targets.Count];
            for (var i = 0; i < targets.Count; i++)
            {
                if (!EditorTargetUtils.TryResolveTarget(
                        targets[i],
                        defaultScenePath,
                        "targets[" + i + "]",
                        out objects[i],
                        out var error,
                        out var statusCode))
                {
                    RestResponse.SendError(response, error, statusCode);
                    return;
                }
            }

            Selection.objects = objects;
            Selection.activeObject = objects[activeIndex];
            RestResponse.Send(response, EditorTargetUtils.SelectionJson());
        }
    }
}
