using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>Event kinds accepted in a replay list, mirroring the immediate input endpoints.</summary>
    internal static class InputReplayEventType
    {
        internal const string Perform = "perform";
        internal const string Set = "set";
        internal const string Pointer = "pointer";
    }

    /// <summary>Lifecycle states of a replay, mirroring <see cref="CompileRecord"/>.</summary>
    internal static class InputReplayState
    {
        internal const string Queued = "queued";
        internal const string Running = "running";
        internal const string Completed = "completed";
        internal const string Aborted = "aborted";
    }

    /// <summary>Per-event outcomes within a replay.</summary>
    internal static class InputReplayEventStatus
    {
        internal const string Pending = "pending";
        internal const string Applied = "applied";
        internal const string Failed = "failed";
    }

    /// <summary>Kind of value carried by a <c>set</c> event.</summary>
    internal enum InputReplayValueKind
    {
        None = 0,
        Scalar = 1,
        Vector2 = 2
    }

    /// <summary>Kind of screen coordinate carried by a <c>pointer</c> event.</summary>
    internal enum InputReplayPointKind
    {
        None = 0,
        Pixel = 1,
        Normalized = 2
    }

    /// <summary>
    /// One scheduled input event, as validated from the request body.
    /// </summary>
    /// <remarks>
    /// Fields are flat and public because the record round-trips through <c>JsonUtility</c> to
    /// survive the domain reload that entering Play mode causes.
    /// </remarks>
    [Serializable]
    internal sealed class InputReplayEventSpec
    {
        /// <summary>Scheduled frame, relative to the first player frame of Play mode.</summary>
        public int frame;

        /// <summary>One of <see cref="InputReplayEventType"/>.</summary>
        public string type = "";

        /// <summary>Action identifier for <c>perform</c> and <c>set</c>; <c>Map/Action</c> or a bare name.</summary>
        public string action = "";

        /// <summary><c>press</c> or <c>release</c> for <c>perform</c>; plus <c>move</c> for <c>pointer</c>.</summary>
        public string mode = "";

        /// <summary>Mouse button for <c>pointer</c>: <c>left</c>, <c>right</c>, or <c>middle</c>.</summary>
        public string button = "";

        public InputReplayValueKind valueKind = InputReplayValueKind.None;
        public float valueX;
        public float valueY;

        public InputReplayPointKind pointKind = InputReplayPointKind.None;
        public float pointX;
        public float pointY;
        public bool originTopLeft;
    }

    /// <summary>
    /// What actually happened to one scheduled event. The frame is the observed one, never the
    /// requested one, so a client can prove the schedule held rather than assume it.
    /// </summary>
    [Serializable]
    internal sealed class InputReplayEventResult
    {
        /// <summary>Index in the request array; the caller's handle on this event.</summary>
        public int index;

        /// <summary>Observed relative frame, or -1 while pending.</summary>
        public int frame = -1;

        /// <summary><c>Time.frameCount</c> when the event was applied, or 0 while pending.</summary>
        public int unityFrame;

        /// <summary>Whether the event was applied later than its scheduled frame.</summary>
        public bool late;

        /// <summary>One of <see cref="InputReplayEventStatus"/>.</summary>
        public string status = InputReplayEventStatus.Pending;

        /// <summary>Resolved control path, for diagnostics.</summary>
        public string control = "";

        /// <summary>Failure description when <see cref="status"/> is <c>failed</c>.</summary>
        public string error = "";
    }

    /// <summary>
    /// A durable record of one input replay: the schedule it was given and what came of it.
    /// </summary>
    /// <remarks>
    /// Two axes, as in <see cref="CompileRecord"/>: <see cref="state"/> tracks the replay's
    /// lifecycle while each <see cref="InputReplayEventResult.status"/> tracks one event. A single
    /// failed event does not abort the replay, so a completed replay can still carry failures —
    /// which is why <see cref="failedCount"/> is part of the API response.
    /// </remarks>
    [Serializable]
    internal sealed class InputReplayRecord
    {
        public string id = "";
        public string state = InputReplayState.Queued;
        public string sessionId = "";
        public string requestedAt = "";
        public string startedAt = "";
        public string finishedAt = "";
        public double durationSeconds;
        public int lifecycleGenerationAtRequest;
        public int lifecycleGenerationAtFinish;

        /// <summary><c>Time.frameCount</c> of the first observed player input update.</summary>
        public int baseFrame = -1;

        /// <summary>Most recent observed relative frame.</summary>
        public int lastObservedFrame = -1;

        /// <summary>Input System update mode the replay ran under, for diagnostics.</summary>
        public string updateMode = "";

        public int eventCount;
        public int appliedCount;
        public int lateCount;
        public int failedCount;

        /// <summary>Machine-readable abort token, empty when the replay was not aborted.</summary>
        public string abortCode = "";

        /// <summary>Human-readable abort sentence, empty when the replay was not aborted.</summary>
        public string abortReason = "";

        public List<InputReplayEventSpec> inputs = new List<InputReplayEventSpec>();
        public List<InputReplayEventResult> events = new List<InputReplayEventResult>();

        internal bool IsActive => state == InputReplayState.Queued || state == InputReplayState.Running;

        /// <summary>
        /// Serializes the record for the HTTP API.
        /// </summary>
        /// <remarks>
        /// Hand-written because <c>JsonUtility</c> cannot emit <c>null</c>, and absent frames must
        /// be <c>null</c> rather than <c>-1</c> or <c>0</c> so a client does not mistake a pending
        /// event for one observed at frame 0.
        /// </remarks>
        internal string ToApiJson()
        {
            var sb = new StringBuilder();
            sb.Append("{");
            AppendString(sb, "state", state);

            sb.Append(",\"events\":[");
            for (var i = 0; i < events.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var e = events[i];
                sb.Append("{\"index\":").Append(Int(e.index));
                sb.Append(",\"frame\":").Append(e.frame >= 0 ? Int(e.frame) : "null");
                sb.Append(",\"late\":").Append(RestResponse.FormatBool(e.late));
                sb.Append(",\"unityFrame\":").Append(e.unityFrame > 0 ? Int(e.unityFrame) : "null");
                sb.Append(",");
                AppendString(sb, "status", e.status);
                sb.Append(",\"control\":").Append(NullableString(e.control));
                sb.Append(",\"error\":").Append(NullableString(e.error));
                sb.Append("}");
            }
            sb.Append("]");

            sb.Append(",\"lateCount\":").Append(Int(lateCount));
            sb.Append(",\"failedCount\":").Append(Int(failedCount));
            sb.Append(",\"abortReason\":").Append(NullableString(abortReason));
            sb.Append(",\"abortCode\":").Append(NullableString(abortCode));
            sb.Append(",");
            AppendString(sb, "id", id);
            sb.Append(",\"eventCount\":").Append(Int(eventCount));
            sb.Append(",\"appliedCount\":").Append(Int(appliedCount));
            sb.Append(",\"baseFrame\":").Append(baseFrame >= 0 ? Int(baseFrame) : "null");
            sb.Append(",\"lastObservedFrame\":").Append(lastObservedFrame >= 0 ? Int(lastObservedFrame) : "null");
            sb.Append(",\"updateMode\":").Append(NullableString(updateMode));
            sb.Append(",\"sessionId\":").Append(NullableString(sessionId));
            sb.Append(",\"requestedAt\":").Append(NullableString(requestedAt));
            sb.Append(",\"startedAt\":").Append(NullableString(startedAt));
            sb.Append(",\"finishedAt\":").Append(NullableString(finishedAt));
            sb.Append(",\"durationSeconds\":")
              .Append(durationSeconds.ToString("G17", CultureInfo.InvariantCulture));
            sb.Append(",\"lifecycleGenerationAtRequest\":").Append(Int(lifecycleGenerationAtRequest));
            sb.Append(",\"lifecycleGenerationAtFinish\":").Append(Int(lifecycleGenerationAtFinish));
            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendString(StringBuilder sb, string name, string value)
        {
            sb.Append("\"").Append(name).Append("\":\"").Append(RestResponse.EscapeJson(value)).Append("\"");
        }

        private static string NullableString(string value)
            => string.IsNullOrEmpty(value) ? "null" : RestResponse.FormatNullableString(value);

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
