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
        /// <returns>
        /// <c>true</c> when the response is complete and the server should close it;
        /// <c>false</c> when the handler deferred the response and owns its lifetime.
        /// </returns>
        public bool Handle(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            if (!RestRequestPolicy.IsOriginAllowed(request.Headers.GetValues("Origin")))
            {
                RestResponse.SendError(response,
                    "Browser-originated requests are not allowed.", 403);
                return true;
            }

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                return true;
            }

            var pathMatched = false;
            foreach (var descriptor in UnionAirRouteRegistry.Descriptors)
            {
                var routeValues = descriptor.HasPathParameters ? new Dictionary<string, string>() : null;
                if (!descriptor.TryMatch(request.Url.AbsolutePath, routeValues))
                    continue;

                pathMatched = true;
                if (descriptor.Method != request.HttpMethod)
                    continue;

                if (!CanCallDuringTestRun(descriptor, response))
                    return true;

                if (!descriptor.Enabled)
                {
                    RestResponse.SendError(response,
                        string.IsNullOrEmpty(descriptor.Error)
                            ? descriptor.CategoryDefinition.DisplayName + " category is disabled."
                            : descriptor.Error,
                        403);
                    return true;
                }

                if (!RestRequestPolicy.HasSupportedContentType(
                        request.HasEntityBody,
                        request.ContentType))
                {
                    RestResponse.SendError(response,
                        "Requests with a body must use Content-Type: application/json.", 415);
                    return true;
                }

                if (!CanCallInCurrentPlayModeState(descriptor, request, response))
                    return true;

                var routeContext = new UnionAirRequestContext(request, response, routeValues, descriptor);
                try
                {
                    descriptor.Handler.Invoke(descriptor.Target, new object[] { routeContext });
                }
                catch (TargetInvocationException ex)
                {
                    // A handler that already deferred owns the response (and its completion
                    // path), so surface the error without letting the server close it.
                    if (routeContext.IsDeferred)
                    {
                        UnityEngine.Debug.LogError(
                            $"[UnionAir] Deferred handler for {request.Url.AbsolutePath} threw: {(ex.InnerException ?? ex).Message}");
                        return false;
                    }
                    throw ex.InnerException ?? ex;
                }
                return !routeContext.IsDeferred;
            }

            if (pathMatched)
            {
                RestResponse.SendError(response,
                    $"Method not allowed for {request.Url.AbsolutePath}", 405);
                return true;
            }

            RestResponse.SendNotFound(response,
                $"No handler for {request.HttpMethod} {request.Url.AbsolutePath}");
            return true;
        }

        private static bool CanCallDuringTestRun(
            UnionAirEndpointDescriptor descriptor,
            HttpListenerResponse response)
        {
            if (!UnionAirTestRunGate.IsActive ||
                descriptor.TestRunPolicy == UnionAirTestRunPolicy.Allowed)
                return true;

            var source = RestResponse.FormatNullableString(UnionAirTestRunGate.PublicSource);
            var id = RestResponse.FormatNullableString(UnionAirTestRunGate.PublicRunId);
            RestResponse.Send(response,
                $"{{\"error\":\"This endpoint cannot be used while a Unity Test Framework run is active.\",\"activeTestRun\":{{\"source\":{source},\"id\":{id}}}}}",
                409);
            return false;
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

    /// <summary>
    /// Defines transport-level request rules that are enforced before an endpoint handler runs.
    /// Kept independent from <see cref="HttpListenerRequest"/> so the policy can be unit tested.
    /// </summary>
    internal static class RestRequestPolicy
    {
        internal static bool IsOriginAllowed(string[] originHeaderValues)
            => originHeaderValues == null;

        internal static bool HasSupportedContentType(bool hasEntityBody, string contentType)
        {
            if (!hasEntityBody)
                return true;

            if (string.IsNullOrWhiteSpace(contentType))
                return false;

            var parameterIndex = contentType.IndexOf(';');
            var mediaType = parameterIndex >= 0
                ? contentType.Substring(0, parameterIndex)
                : contentType;

            return string.Equals(
                mediaType.Trim(),
                "application/json",
                System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
