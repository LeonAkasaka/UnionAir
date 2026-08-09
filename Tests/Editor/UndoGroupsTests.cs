using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;

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

        /// <summary>
        /// The animation handlers went the other way round from the scene handlers: they
        /// register no undo of their own, because the <c>UnityEditor.Animations</c> editing
        /// APIs register theirs. That is why the group was missed here -- "we add no undo"
        /// reads as "we need no undo code", and the group is not registration. Unity's
        /// registration lands in whichever group is current, so without a boundary every
        /// controller write since the user last touched the Editor accumulated into one
        /// entry. Measured on 6000.0.80f1: four consecutive POST .../states calls were all
        /// taken back by a single Ctrl+Z.
        ///
        /// The guard is the signature. Each animation handler saves through a private
        /// <c>Save(AnimatorController, int)</c>, so a new write path cannot reach the save
        /// without naming a group, and the parameterless form that let this happen is gone.
        /// Asserting it here keeps someone from reintroducing the convenient overload.
        /// </summary>
        [TestCase(typeof(AnimatorControllerHandler))]
        [TestCase(typeof(BlendTreeHandler))]
        [TestCase(typeof(AnimatorStateMachineHandler))]
        public void AnimationHandlers_CannotSaveWithoutAnUndoGroup(System.Type handler)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance
                                     | BindingFlags.Public | BindingFlags.NonPublic;

            var saves = handler.GetMethods(flags);
            var withGroup = 0;

            foreach (var method in saves)
            {
                if (method.Name != "Save") continue;

                var parameters = method.GetParameters();
                Assert.AreEqual(2, parameters.Length,
                    handler.Name + ".Save must take the controller and the undo group to collapse to");
                Assert.AreEqual(typeof(AnimatorController), parameters[0].ParameterType);
                Assert.AreEqual(typeof(int), parameters[1].ParameterType,
                    "the second parameter is the group index from UndoGroups.Begin");
                withGroup++;
            }

            Assert.AreEqual(1, withGroup, handler.Name + " must have exactly one Save");
        }
    }
}
