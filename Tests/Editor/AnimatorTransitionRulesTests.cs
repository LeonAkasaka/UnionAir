using NUnit.Framework;
using UnityEditor.Animations;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class AnimatorTransitionRulesTests
    {
        [TestCase("None", TransitionInterruptionSource.None)]
        [TestCase("source", TransitionInterruptionSource.Source)]
        [TestCase("DESTINATION", TransitionInterruptionSource.Destination)]
        [TestCase("SourceThenDestination", TransitionInterruptionSource.SourceThenDestination)]
        [TestCase("destinationthensource", TransitionInterruptionSource.DestinationThenSource)]
        public void InterruptionSourceIsParsedWithoutRegardToCase(string name, TransitionInterruptionSource expected)
        {
            Assert.IsTrue(AnimatorTransitionRules.TryParseInterruptionSource(name, out var parsed));
            Assert.AreEqual(expected, parsed);
        }

        [TestCase("Nope")]
        [TestCase("")]
        [TestCase(null)]
        public void AnUnknownInterruptionSourceIsRejected(string name)
        {
            Assert.IsFalse(AnimatorTransitionRules.TryParseInterruptionSource(name, out _));
        }

        [Test]
        public void EveryAcceptedInterruptionSourceIsNamedInTheErrorText()
        {
            foreach (TransitionInterruptionSource value in
                     System.Enum.GetValues(typeof(TransitionInterruptionSource)))
            {
                StringAssert.Contains(value.ToString(), AnimatorTransitionRules.InterruptionSourceNames);
            }
        }

        [TestCase("if", AnimatorConditionMode.If)]
        [TestCase("IfNot", AnimatorConditionMode.IfNot)]
        [TestCase("GREATER", AnimatorConditionMode.Greater)]
        [TestCase("less", AnimatorConditionMode.Less)]
        [TestCase("Equals", AnimatorConditionMode.Equals)]
        [TestCase("notequal", AnimatorConditionMode.NotEqual)]
        public void ConditionModeIsParsedWithoutRegardToCase(string name, AnimatorConditionMode expected)
        {
            Assert.IsTrue(AnimatorTransitionRules.TryParseConditionMode(name, out var parsed));
            Assert.AreEqual(expected, parsed);
        }

        [Test]
        public void AnUnknownConditionModeIsRejected()
        {
            Assert.IsFalse(AnimatorTransitionRules.TryParseConditionMode("Bigger", out _));
        }

        [Test]
        public void CanTransitionToSelfIsUnsupportedOnAStateTransition()
        {
            var unsupported = AnimatorTransitionRules.CollectUnsupported(
                canTransitionToSelfSet: true, isAnyStateTransition: false);

            Assert.AreEqual(1, unsupported.Count);
            StringAssert.Contains("canTransitionToSelf", unsupported[0]);
            StringAssert.Contains("AnyState", unsupported[0]);
        }

        [Test]
        public void CanTransitionToSelfIsSupportedOnAnAnyStateTransition()
        {
            Assert.IsEmpty(AnimatorTransitionRules.CollectUnsupported(
                canTransitionToSelfSet: true, isAnyStateTransition: true));
        }

        [Test]
        public void AFieldThatWasNotSentIsNotReported()
        {
            Assert.IsEmpty(AnimatorTransitionRules.CollectUnsupported(
                canTransitionToSelfSet: false, isAnyStateTransition: false));
        }

        [Test]
        public void TheAmbiguityMessageNamesTheCountAndTheWayOut()
        {
            var message = AnimatorTransitionRules.AmbiguousAddressMessage("Idle", "Walk", 3);

            StringAssert.Contains("3", message);
            StringAssert.Contains("Idle -> Walk", message);
            StringAssert.Contains("transitionId", message);
        }
    }
}
