namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Applies replay events to virtual input devices on the frames they come due.
    /// </summary>
    /// <remarks>
    /// This is the seam across the assembly boundary. The scheduling, persistence, and lifecycle
    /// decisions live in the main assembly where they can be unit-tested; the implementation lives
    /// in the optional Input System assembly and does nothing but mutate devices, so a project
    /// without <c>com.unity.inputsystem</c> simply has no driver registered.
    /// </remarks>
    internal interface IInputReplayDriver
    {
        /// <summary>
        /// Begins driving a replay. Called once Play mode has been entered.
        /// </summary>
        /// <param name="record">The armed record, whose <c>inputs</c> hold the schedule.</param>
        /// <param name="error">Why the replay cannot start, when this returns false.</param>
        /// <returns>False when the Input System is configured in a way the replay cannot use.</returns>
        bool TryBegin(InputReplayRecord record, out string error);

        /// <summary>
        /// Stops driving, whether the replay completed or was abandoned.
        /// </summary>
        /// <param name="releaseHeld">
        /// Whether to release input the replay left held. False when Play mode is ending, because
        /// exiting Play mode resets the virtual devices anyway, and false on normal completion,
        /// because a press with no scheduled release is meant to stay held.
        /// </param>
        void Stop(bool releaseHeld);
    }
}
