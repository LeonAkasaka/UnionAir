using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// The path rule on its own: what an array of names resolves to, and what it does when
    /// the controller cannot answer. Built from bare AnimatorStateMachine objects rather
    /// than an asset, because none of this depends on being saved.
    /// </summary>
    internal sealed class AnimatorStateMachineRulesTests
    {
        private AnimatorStateMachine _root;

        [SetUp]
        public void CreateTree()
        {
            // AnimatorStateMachine derives from Object rather than ScriptableObject, so the
            // generic CreateInstance does not accept it; the constructor does the job here,
            // where nothing needs to be a sub-asset of anything.
            _root = new AnimatorStateMachine { name = "Base Layer" };
            var combat = _root.AddStateMachine("Combat");
            combat.AddStateMachine("Melee");
            combat.AddStateMachine("Ranged");
        }

        [TearDown]
        public void DestroyTree()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void AnEmptyPathIsTheRootItself()
        {
            var result = AnimatorStateMachineRules.TryResolve(_root, new string[0], out var machine, out _, out _);

            Assert.AreEqual(AnimatorStateMachineRules.PathResult.Resolved, result);
            Assert.AreSame(_root, machine);
        }

        [Test]
        public void ANullPathIsTheRootToo()
        {
            // Which is what an omitted stateMachinePath reaches here, and it has to mean the
            // same thing as [] or every request written before the field existed changes.
            var result = AnimatorStateMachineRules.TryResolve(_root, null, out var machine, out _, out _);

            Assert.AreEqual(AnimatorStateMachineRules.PathResult.Resolved, result);
            Assert.AreSame(_root, machine);
        }

        [Test]
        public void ATwoSegmentPathReachesTheNestedMachine()
        {
            var result = AnimatorStateMachineRules.TryResolve(
                _root, new[] { "Combat", "Melee" }, out var machine, out _, out _);

            Assert.AreEqual(AnimatorStateMachineRules.PathResult.Resolved, result);
            Assert.AreEqual("Melee", machine.name);
        }

        [Test]
        public void ASegmentThatNamesNothingReportsItsDepth()
        {
            var result = AnimatorStateMachineRules.TryResolve(
                _root, new[] { "Combat", "Nope" }, out var machine, out var depth, out _);

            Assert.AreEqual(AnimatorStateMachineRules.PathResult.NotFound, result);
            Assert.AreEqual(1, depth, "the first segment resolved; the second did not");
            Assert.IsNull(machine);
            StringAssert.Contains("depth 1",
                AnimatorStateMachineRules.NotFoundMessage(new[] { "Combat", "Nope" }, depth, "stateMachinePath"));
        }

        [Test]
        public void AddStateMachineRenamesRatherThanDuplicatingASiblingName()
        {
            // Measured: asking for a name a sibling already carries gives a different one
            // back. So AddStateMachine is not how a duplicate arises -- renaming afterwards
            // is, which is what a human does in the Animator window.
            var second = _root.AddStateMachine("Combat");

            Assert.AreNotEqual("Combat", second.name);
        }

        [Test]
        public void ANameSeveralSiblingsCarryIsAmbiguousRatherThanTheFirst()
        {
            // A duplicate reached the way a human reaches it: by renaming. Resolving to the
            // first would write to whichever happened to be first in the array, and
            // reordering would silently change which one that is.
            _root.AddStateMachine("Other").name = "Combat";

            var result = AnimatorStateMachineRules.TryResolve(
                _root, new[] { "Combat" }, out var machine, out var depth, out var matches);

            Assert.AreEqual(AnimatorStateMachineRules.PathResult.Ambiguous, result);
            Assert.AreEqual(2, matches);
            Assert.IsNull(machine);
            StringAssert.Contains("2 sibling",
                AnimatorStateMachineRules.AmbiguousMessage(new[] { "Combat" }, depth, matches, "stateMachinePath"));
        }

        [Test]
        public void ANameWithASlashInItResolvesLikeAnyOther()
        {
            // The reason the address is an array: Unity permits the separator a joined path
            // would need, and an escaping rule is a thing clients get wrong quietly.
            _root.AddStateMachine("Combat/Melee");

            var result = AnimatorStateMachineRules.TryResolve(
                _root, new[] { "Combat/Melee" }, out var machine, out _, out _);

            Assert.AreEqual(AnimatorStateMachineRules.PathResult.Resolved, result);
            Assert.AreEqual("Combat/Melee", machine.name);
        }

        [Test]
        public void ContentsAreCountedThroughTheWholeSubtree()
        {
            // A machine that holds no states directly but holds one that holds several must
            // not be reported as costing nothing to remove.
            var combat = _root.stateMachines[0].stateMachine;
            var melee = combat.stateMachines[0].stateMachine;
            melee.AddState("Swing");
            melee.AddState("Recover");

            AnimatorStateMachineRules.CountContents(combat, out var states, out var machines);

            Assert.AreEqual(0, combat.states.Length, "Combat holds no state directly");
            Assert.AreEqual(2, states, "but two go with it");
            Assert.AreEqual(2, machines);
        }

        [Test]
        public void DescribeNamesTheRootWhenThePathIsEmpty()
        {
            StringAssert.Contains("root", AnimatorStateMachineRules.Describe(new string[0]));
            StringAssert.Contains("\"Combat\"", AnimatorStateMachineRules.Describe(new[] { "Combat" }));
        }

        [Test]
        public void AMessageNamesTheFieldTheBadPathCameFrom()
        {
            // Two request fields carry a path and they resolve from different roots, so the
            // message has to say which one failed. It said "stateMachinePath" for both, and
            // a client sending a correct stateMachinePath with a wrong toStateMachine was
            // told to look at the field that was right.
            var path = new[] { "Combat", "Nope" };

            var source = AnimatorStateMachineRules.NotFoundMessage(path, 1, "stateMachinePath");
            var destination = AnimatorStateMachineRules.NotFoundMessage(path, 1, "toStateMachine");

            StringAssert.StartsWith("stateMachinePath does not resolve", source);
            StringAssert.StartsWith("toStateMachine does not resolve", destination);
            Assert.AreNotEqual(source, destination,
                "the two failures must not read identically");
        }

        [Test]
        public void AnAmbiguousPathAlsoNamesItsField()
        {
            var path = new[] { "Combat" };

            StringAssert.Contains("toStateMachine",
                AnimatorStateMachineRules.AmbiguousMessage(path, 0, 2, "toStateMachine"));
            StringAssert.Contains("stateMachinePath",
                AnimatorStateMachineRules.AmbiguousMessage(path, 0, 2, "stateMachinePath"));
        }
    }
}
