using System.Collections.Generic;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles play mode control endpoints:
    ///   POST /api/editor/play   — enter play mode, optionally replaying a scheduled input list
    ///   POST /api/editor/stop   — exit play mode
    ///   POST /api/editor/pause  — set pause state (body: {"paused": bool}, or toggle if omitted)
    ///   POST /api/editor/step   — advance one frame (requires isPaused == true)
    /// All require the Play Mode category to be enabled.
    /// </summary>
    internal class EditorPlayHandler
    {
        /// <summary>
        /// Play mode request that has been scheduled but not yet run, kept so it can be withdrawn.
        /// </summary>
        private static EditorApplication.CallbackFunction _scheduledPlay;

        public void Handle(UnionAirRequest request, UnionAirResponse response)
        {
            switch (request.Url.AbsolutePath)
            {
                case "/api/editor/play":  HandlePlay(request, response);  break;
                case "/api/editor/stop":  HandleStop(response);           break;
                case "/api/editor/pause": HandlePause(request, response); break;
                case "/api/editor/step":  HandleStep(response);           break;
            }
        }

        private static void HandlePlay(UnionAirRequest request, UnionAirResponse response)
        {
            var body = RequestBodyReader.ReadString(request);

            List<InputReplayEventSpec> inputs;
            bool hasInputs;
            string error;
            if (!InputReplayRequestParser.TryParse(body, out inputs, out hasInputs, out error))
            {
                RestResponse.SendError(response, error, 400);
                return;
            }

            if (!hasInputs)
            {
                EditorApplication.isPlaying = true;
                RestResponse.Send(response,
                    "{\"playing\":true," +
                    "\"note\":\"Domain reload may occur. Poll GET /api/editor/status until isPlaying is true.\"}");
                return;
            }

            StartReplay(inputs, response);
        }

        /// <summary>
        /// Arms a validated replay and requests Play mode.
        /// </summary>
        /// <remarks>
        /// Every rejection happens here, before Play mode is requested. Entering Play mode causes
        /// a domain reload and this response has already been sent by the time the replay would
        /// run, so a problem found later could not be reported to the caller at all.
        /// </remarks>
        private static void StartReplay(List<InputReplayEventSpec> inputs, UnionAirResponse response)
        {
            if (!InputReplayService.HasDriver)
            {
                RestResponse.SendError(response,
                    "Input replay requires the com.unity.inputsystem package.", 400);
                return;
            }

            if (EditorApplication.isPlaying)
            {
                RestResponse.SendError(response,
                    "Already in Play mode; a replay is timed from the first Play mode frame. Stop first with POST /api/editor/stop.",
                    409);
                return;
            }

            var activeId = UnionAirInputReplayGate.PublicId;
            if (activeId != null)
            {
                RestResponse.Send(response,
                    "{\"error\":\"An input replay is already active.\",\"activeReplay\":{\"id\":\"" +
                    RestResponse.EscapeJson(activeId) + "\"}}", 409);
                return;
            }

            var record = InputReplayService.Arm(inputs);
            if (record == null)
            {
                RestResponse.SendError(response,
                    "Could not persist the input replay, so Play mode was not entered. Check that Library/UnionAir is writable.",
                    500);
                return;
            }

            SchedulePlay();

            RestResponse.Send(response,
                "{\"playing\":true,\"replay\":{" +
                "\"id\":\"" + RestResponse.EscapeJson(record.id) + "\"," +
                "\"state\":\"" + RestResponse.EscapeJson(record.state) + "\"," +
                "\"eventCount\":" + record.eventCount + "," +
                "\"statusUrl\":\"/api/playmode/input/result?id=" + RestResponse.EscapeJson(record.id) + "\"}," +
                "\"note\":\"Poll GET /api/playmode/input/result until state leaves queued and running.\"}",
                202);
        }

        /// <summary>
        /// Requests Play mode on the next Editor update rather than immediately.
        /// </summary>
        /// <remarks>
        /// Answering before touching <c>isPlaying</c> matters because setting it starts a domain
        /// reload that would otherwise drop the connection before the caller learned the replay
        /// id. Scheduling through <c>update</c> rather than <c>delayCall</c> keeps this working
        /// while the Editor is in the background, and matches how the compile pipeline defers its
        /// own start.
        /// <para>
        /// The callback is retained so that <c>POST /api/editor/stop</c> can withdraw it. Requests
        /// are drained from one queue inside a single Editor update, so a stop arriving right
        /// after a play would otherwise clear <c>isPlaying</c> only for this callback to set it
        /// again on the following tick.
        /// </para>
        /// </remarks>
        private static void SchedulePlay()
        {
            CancelScheduledPlay();

            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                EditorApplication.update -= callback;

                // Unsubscribing alone does not cancel anything: EditorApplication.update
                // snapshots its invocation list, so a callback withdrawn while that same update
                // is running still gets called. The identity check is what actually withdraws it.
                if (!ReferenceEquals(_scheduledPlay, callback)) return;

                _scheduledPlay = null;
                EditorApplication.isPlaying = true;
            };

            _scheduledPlay = callback;
            EditorApplication.update += callback;
        }

        private static void CancelScheduledPlay()
        {
            if (_scheduledPlay == null) return;
            EditorApplication.update -= _scheduledPlay;
            _scheduledPlay = null;
        }

        private static void HandleStop(UnionAirResponse response)
        {
            // Withdraw a Play mode request that has not run yet, and abandon the replay it was
            // going to start; otherwise stop would be undone a tick later.
            CancelScheduledPlay();
            InputReplayService.CancelQueued(
                "The replay was cancelled by POST /api/editor/stop before Play mode started.");

            EditorApplication.isPlaying = false;
            RestResponse.Send(response, "{\"playing\":false}");
        }

        private static void HandlePause(UnionAirRequest request, UnionAirResponse response)
        {
            // Body is optional; if omitted, toggle current pause state
            var body = RequestBodyReader.ReadString(request);
            bool targetPaused;

            var pausedStr = RequestBodyReader.GetString(body, "paused");
            if (pausedStr == "true" || pausedStr == "1")
                targetPaused = true;
            else if (pausedStr == "false" || pausedStr == "0")
                targetPaused = false;
            else
                targetPaused = !EditorApplication.isPaused; // toggle

            EditorApplication.isPaused = targetPaused;
            RestResponse.Send(response, $"{{\"paused\":{(targetPaused ? "true" : "false")}}}");
        }

        private static void HandleStep(UnionAirResponse response)
        {
            if (!EditorApplication.isPaused)
            {
                RestResponse.SendError(response,
                    "EditorApplication.Step() requires isPaused == true. Pause first with POST /api/editor/pause.", 400);
                return;
            }

            EditorApplication.Step();
            RestResponse.Send(response, "{\"stepped\":true}");
        }
    }
}
