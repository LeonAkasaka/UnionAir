using NUnit.Framework;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the group boundary that keeps one request's undo entry out of the next
    /// one's. The defect this guards against was a missing Undo.IncrementCurrentGroup:
    /// without it every write since the user last touched the Editor collapsed into a
    /// single entry, and one Ctrl+Z took back all of them.
    ///
    /// These assert the boundary itself rather than the end-to-end behavior. Reverting a
    /// real request would mean creating scene objects and popping the undo stack, which
    /// no test in this suite does and which would mutate the open scene and the undo
    /// history of whoever runs it. Opening a group registers nothing and destroys
    /// nothing, so it is safe to assert here; the end-to-end case is verified live and
    /// recorded on the pull request.
    /// </summary>
    internal sealed class UndoGroupsTests
    {
        [Test]
        public void Begin_OpensANewGroupForEachCall()
        {
            // Two requests arriving back to back. Nothing advances the undo group between
            // two HTTP-triggered callbacks, so if Begin does not advance it itself, both
            // land in one group and collapsing the second swallows the first.
            var first = UndoGroups.Begin("UnionAir test: first request");
            var second = UndoGroups.Begin("UnionAir test: second request");

            Assert.AreNotEqual(first, second,
                "consecutive requests must not share an undo group");
        }

        [Test]
        public void Begin_ReturnsTheGroupItOpened()
        {
            // The contract callers rely on: the returned index is the group to collapse
            // to, so a handler that collapses to it cannot reach further back than the
            // work it just did.
            var group = UndoGroups.Begin("UnionAir test: contract");

            Assert.AreEqual(Undo.GetCurrentGroup(), group);
        }
    }
}
