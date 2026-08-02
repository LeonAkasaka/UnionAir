using System.Globalization;
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
            var isCompiling = EditorApplication.isCompiling;
            var isUpdating = EditorApplication.isUpdating;

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"isPlaying\":{RestResponse.FormatBool(EditorApplication.isPlaying)},");
            sb.Append($"\"isPaused\":{RestResponse.FormatBool(EditorApplication.isPaused)},");
            sb.Append($"\"isCompiling\":{RestResponse.FormatBool(isCompiling)},");
            sb.Append($"\"isUpdating\":{RestResponse.FormatBool(isUpdating)},");
            sb.Append($"\"unityVersion\":\"{RestResponse.EscapeJson(Application.unityVersion)}\",");
            sb.Append($"\"isTestRunning\":{RestResponse.FormatBool(UnionAirTestRunGate.IsActive)},");
            sb.Append("\"testRunSource\":").Append(RestResponse.FormatNullableString(UnionAirTestRunGate.PublicSource));
            sb.Append(",\"testRunId\":").Append(RestResponse.FormatNullableString(UnionAirTestRunGate.PublicRunId));
            sb.Append(",\"sessionId\":").Append(RestResponse.FormatNullableString(UnionAirSession.SessionId));
            sb.Append($",\"lifecycleGeneration\":{UnionAirSession.Generation.ToString(CultureInfo.InvariantCulture)}");
            // A queued or running build settles nothing: it is about to take the main thread for a
            // minute or more, and dependent calls made now would be answered only after it ends.
            var buildActive = BuildService.IsBusy;
            sb.Append($",\"settled\":{RestResponse.FormatBool(!isCompiling && !isUpdating && !UnionAirCompileGate.IsActive && !buildActive)}");
            sb.Append($",\"hasCompileErrors\":{RestResponse.FormatBool(EditorUtility.scriptCompilationFailed)}");

            var compile = CompileService.Current;
            var compileActive = compile != null && compile.IsActive;
            sb.Append(",\"compileState\":")
              .Append(RestResponse.FormatNullableString(compileActive ? compile.state : null));
            sb.Append(",\"compileId\":")
              .Append(RestResponse.FormatNullableString(compileActive ? compile.id : null));
            sb.Append(",\"compileSource\":")
              .Append(RestResponse.FormatNullableString(compileActive ? compile.source : null));

            var build = BuildService.Current;
            var buildRecordActive = build != null && build.IsActive;
            sb.Append(",\"buildState\":")
              .Append(RestResponse.FormatNullableString(buildRecordActive ? build.state : null));
            sb.Append(",\"buildId\":")
              .Append(RestResponse.FormatNullableString(buildRecordActive ? build.id : null));

            // One field naming what the Editor is busy with, so a client does not have to infer it
            // from isCompiling, isUpdating, isPlaying, and isTestRunning and get the priority wrong.
            sb.Append(",\"activeActivity\":");
            UnionAirActivityDecision.AppendActivity(sb, UnionAirActivityCoordinator.Current());
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }
    }
}
