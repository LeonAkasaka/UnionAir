using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// A <see cref="StateMachineBehaviour"/> for the state read tests to attach.
    ///
    /// In its own file with a matching name because Unity resolves a ScriptableObject to a
    /// MonoScript that way; a type nested in a test class has no MonoScript to find, and
    /// <c>AnimatorState.AddStateMachineBehaviour</c> then attaches nothing.
    /// </summary>
    internal sealed class UnionAirProbeStateBehaviour : StateMachineBehaviour
    {
    }
}
