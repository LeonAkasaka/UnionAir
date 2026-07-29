using System.Collections.Generic;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>An event that has come due on the current frame.</summary>
    internal readonly struct InputReplayDueEvent
    {
        internal InputReplayDueEvent(int index, int scheduledFrame, bool late)
        {
            Index = index;
            ScheduledFrame = scheduledFrame;
            Late = late;
        }

        /// <summary>Index into the request array, which is the caller's handle on the event.</summary>
        internal int Index { get; }

        internal int ScheduledFrame { get; }

        /// <summary>Whether the observed frame was later than the scheduled one.</summary>
        internal bool Late { get; }
    }

    /// <summary>
    /// Orders a replay list by frame and hands out the events due on each observed frame.
    /// </summary>
    /// <remarks>
    /// Events are ordered by <c>(frame, request index)</c>, so events sharing a frame come out in
    /// the order the caller wrote them — which is what makes a chord's component presses land in a
    /// defined order within the single device snapshot they merge into.
    /// <para>
    /// Nothing is ever dropped. An event whose frame has already passed — because the player loop
    /// skipped frames — comes out on the next observed frame marked late, rather than being
    /// silently discarded, so a client can tell a degraded run from a clean one.
    /// </para>
    /// </remarks>
    internal sealed class InputReplaySchedule
    {
        private readonly int[] _order;
        private readonly int[] _frames;
        private int _cursor;

        internal InputReplaySchedule(IList<InputReplayEventSpec> events)
        {
            var count = events == null ? 0 : events.Count;
            _order = new int[count];
            _frames = new int[count];
            for (var i = 0; i < count; i++)
            {
                _order[i] = i;
                _frames[i] = events[i].frame;
            }

            var frames = _frames;
            System.Array.Sort(_order, (a, b) =>
            {
                var byFrame = frames[a].CompareTo(frames[b]);
                // Tie-break on the request index so equal frames keep request order regardless of
                // whether the underlying sort is stable.
                return byFrame != 0 ? byFrame : a.CompareTo(b);
            });
        }

        /// <summary>Number of scheduled events.</summary>
        internal int Count => _order.Length;

        /// <summary>Number of events not yet handed out.</summary>
        internal int PendingCount => _order.Length - _cursor;

        /// <summary>Whether every event has been handed out.</summary>
        internal bool IsComplete => _cursor >= _order.Length;

        /// <summary>
        /// Fills <paramref name="due"/> with every event scheduled at or before
        /// <paramref name="relativeFrame"/> that has not been handed out yet.
        /// </summary>
        /// <param name="relativeFrame">Observed frame, relative to the first player frame.</param>
        /// <param name="due">Destination list; cleared before use.</param>
        internal void TakeDue(int relativeFrame, List<InputReplayDueEvent> due)
        {
            due.Clear();
            while (_cursor < _order.Length)
            {
                var index = _order[_cursor];
                var scheduled = _frames[index];
                if (scheduled > relativeFrame) break;

                due.Add(new InputReplayDueEvent(index, scheduled, relativeFrame > scheduled));
                _cursor++;
            }
        }
    }
}
