using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Opens the undo group that a write handler's operations belong to.
    /// </summary>
    internal static class UndoGroups
    {
        /// <summary>
        /// Starts a new undo group, names it, and returns its index for a later
        /// <see cref="Undo.CollapseUndoOperations"/>.
        ///
        /// The increment is the load-bearing part. <see cref="Undo.SetCurrentGroupName"/>
        /// renames the current group; it does not open one. Without the increment,
        /// <see cref="Undo.GetCurrentGroup"/> returns the group the *previous* request is
        /// already in, and collapsing to it merges that request into this one. Unity
        /// advances the group after a human interaction with the Editor, so a hand edit is
        /// fenced off, but nothing advances it between two HTTP-triggered main-thread
        /// callbacks: every write since the user last touched the Editor accumulated into
        /// one undo entry, and a single Ctrl+Z took back all of them.
        ///
        /// This exists as one function rather than three lines at each call site because
        /// the two-line form was written eleven times and the increment was missing from
        /// all eleven.
        /// </summary>
        internal static int Begin(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }
    }
}
