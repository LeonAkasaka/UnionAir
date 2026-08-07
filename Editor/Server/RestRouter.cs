using System.Collections.Generic;
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
            UnionAirRouteRegistry.RefreshState();
        }

        /// <summary>
        /// Dispatches a request to the matching route descriptor.
        /// </summary>
        /// <param name="request">Incoming request received by the server.</param>
        /// <param name="response">Response the matched handler writes to.</param>
        /// <returns>
        /// <c>true</c> when the response is complete and the server should close it;
        /// <c>false</c> when the handler deferred the response and owns its lifetime.
        /// </returns>
        public bool Handle(UnionAirRequest request, UnionAirResponse response)
        {
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

                if (!CanCallDuringCurrentActivity(descriptor, response))
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

        /// <summary>
        /// Rejects an endpoint that declared it cannot run while a test run is active.
        /// </summary>
        /// <remarks>
        /// Evaluated before the category check, which is why it is a stage of its own rather than
        /// part of <see cref="CanCallDuringCurrentActivity"/>: a blocked endpoint in a disabled
        /// category answers <c>409</c> with the run that owns the Editor, not <c>403</c>. That
        /// ordering is documented behavior.
        /// </remarks>
        private static bool CanCallDuringTestRun(
            UnionAirEndpointDescriptor descriptor,
            UnionAirResponse response)
        {
            if (!UnionAirTestRunGate.IsActive ||
                descriptor.TestRunPolicy == UnionAirTestRunPolicy.Allowed)
                return true;

            var blocking = new UnionAirActivityRecord(
                UnionAirActivity.TestRun,
                UnionAirTestRunGate.PublicSource,
                UnionAirTestRunGate.PublicRunId);

            RestResponse.Send(response,
                UnionAirActivityDecision.RejectionJson(
                    blocking,
                    "This endpoint cannot be used while a Unity Test Framework run is active."),
                409);
            return false;
        }

        /// <summary>
        /// Rejects an endpoint while the Editor is busy with an activity it declared it cannot
        /// overlap with.
        /// </summary>
        /// <remarks>
        /// Play mode and test runs are excluded from this stage and enforced by their own, because
        /// each has behavior a mask cannot express: Play mode supports a per-request opt-in, and
        /// the test-run gate runs ahead of the category check. Both are still reported in the
        /// endpoint's <c>blockedDuring</c> metadata, so a client sees one list.
        /// </remarks>
        private static bool CanCallDuringCurrentActivity(
            UnionAirEndpointDescriptor descriptor,
            UnionAirResponse response)
        {
            var blockedDuring = descriptor.DeclaredBlockedDuring & UnionAirActivityDecision.RouterMask;
            if (blockedDuring == UnionAirActivity.None)
                return true;

            var blocking = UnionAirActivityCoordinator.Blocking(blockedDuring);
            if (!blocking.IsActive)
                return true;

            RestResponse.Send(response, UnionAirActivityDecision.RejectionJson(blocking), 409);
            return false;
        }

        private static bool CanCallInCurrentPlayModeState(
            UnionAirEndpointDescriptor descriptor,
            UnionAirRequest request,
            UnionAirResponse response)
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

        private static bool HasPlayModeOptIn(UnionAirRequest request)
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
    /// Stated over plain values rather than over <see cref="UnionAirRequest"/>, so each rule can
    /// be exercised directly against the header shapes that matter.
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
