namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles <c>GET /api/playmode/input/result</c>, reporting what an input replay actually did.
    /// </summary>
    /// <remarks>
    /// The endpoint doubles as the completion signal: without it a client cannot tell when the
    /// replay finished, and so cannot tell when it is meaningful to inspect the game's state.
    /// Like the Test Runner, only the current replay and the latest completed one are retained.
    /// </remarks>
    internal class PlayModeInputReplayHandler
    {
        public void Handle(UnionAirRequestContext context)
        {
            var response = context.Response;
            var id = context.Request.QueryString["id"];

            if (!string.IsNullOrEmpty(id))
            {
                var match = Find(id);
                if (match == null)
                {
                    RestResponse.SendNotFound(response,
                        $"No input replay with id '{id}'. UnionAir retains only the current replay and the latest completed result.");
                    return;
                }

                RestResponse.Send(response, match.ToApiJson());
                return;
            }

            var record = InputReplayService.Current ?? InputReplayService.Latest;
            if (record == null)
            {
                RestResponse.SendNotFound(response, "No input replay has been recorded.");
                return;
            }

            RestResponse.Send(response, record.ToApiJson());
        }

        private static InputReplayRecord Find(string id)
        {
            var current = InputReplayService.Current;
            if (current != null && current.id == id) return current;

            var latest = InputReplayService.Latest;
            return latest != null && latest.id == id ? latest : null;
        }
    }
}
