using System.Collections.Generic;
using System.Net;
using System.Reflection;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Routes incoming HTTP requests to controller methods discovered from route attributes.
    /// </summary>
    internal class RestRouter
    {
        public RestRouter()
        {
            UnionAirRouteRegistry.Refresh();
        }

        /// <summary>
        /// Dispatches an HTTP listener context to the matching route descriptor.
        /// </summary>
        /// <param name="context">HTTP listener context received by the server.</param>
        public void Handle(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            if (request.HttpMethod == "OPTIONS")
            {
                RestResponse.AddCorsHeaders(response);
                response.StatusCode = 204;
                return;
            }

            var pathMatched = false;
            foreach (var descriptor in UnionAirRouteRegistry.Descriptors)
            {
                var routeValues = new Dictionary<string, string>();
                if (!descriptor.TryMatch(request.Url.AbsolutePath, routeValues))
                    continue;

                pathMatched = true;
                if (descriptor.Method != request.HttpMethod)
                    continue;

                if (!descriptor.Enabled)
                {
                    RestResponse.SendError(response,
                        string.IsNullOrEmpty(descriptor.Error)
                            ? descriptor.CategoryDefinition.DisplayName + " category is disabled."
                            : descriptor.Error,
                        403);
                    return;
                }

                if (!CanCallInCurrentPlayModeState(descriptor, request, response))
                    return;

                var routeContext = new UnionAirRequestContext(request, response, routeValues, descriptor);
                try
                {
                    descriptor.Handler.Invoke(descriptor.Target, new object[] { routeContext });
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException ?? ex;
                }
                return;
            }

            if (pathMatched)
            {
                RestResponse.SendError(response,
                    $"Method not allowed for {request.Url.AbsolutePath}", 405);
                return;
            }

            RestResponse.SendNotFound(response,
                $"No handler for {request.HttpMethod} {request.Url.AbsolutePath}");
        }

        private static bool CanCallInCurrentPlayModeState(
            UnionAirEndpointDescriptor descriptor,
            HttpListenerRequest request,
            HttpListenerResponse response)
        {
            if (!EditorApplication.isPlaying ||
                descriptor.PlayModePolicy == UnionAirPlayModePolicy.Allowed)
                return true;

            if (descriptor.PlayModePolicy == UnionAirPlayModePolicy.Blocked)
            {
                RestResponse.SendError(response,
                    "This endpoint cannot be used while the Unity Editor is in Play mode.",
                    409);
                return false;
            }

            if (descriptor.PlayModePolicy == UnionAirPlayModePolicy.ExplicitOptIn)
            {
                if (!UnionAirSettings.AllowPlayModeSceneChanges)
                {
                    RestResponse.SendError(response,
                        "Play Mode scene changes are disabled in UnionAir settings. Enable Allow Play Mode Scene Changes in the EditorWindow to allow this endpoint.",
                        409);
                    return false;
                }

                if (HasPlayModeOptIn(request))
                    return true;
            }

            RestResponse.SendError(response,
                "This endpoint can change the running scene and requires allowWhilePlaying=true while the Unity Editor is in Play mode.",
                409);
            return false;
        }

        private static bool HasPlayModeOptIn(HttpListenerRequest request)
        {
            if (request.HttpMethod == "POST" || request.HttpMethod == "PATCH")
            {
                var bodyValue = RequestBodyReader.GetBool(
                    RequestBodyReader.ReadString(request),
                    "allowWhilePlaying");
                if (bodyValue.HasValue)
                    return bodyValue.Value;
            }

            var queryValue = request.QueryString["allowWhilePlaying"];
            return queryValue == "true" || queryValue == "1";
        }

    }
}
