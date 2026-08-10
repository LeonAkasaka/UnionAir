using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Pins the wire format of a state machine path.
    ///
    /// The contract the documentation states is a round trip: the read reports the path as
    /// <c>path</c>, the writes echo it as <c>stateMachinePath</c>, and a path read out of a
    /// response goes straight back into a request. That only holds while every endpoint
    /// emits the identical form, and it used to be emitted by two identical private copies
    /// -- the state a format diverges from without failing to compile.
    /// </summary>
    internal sealed class AnimatorStateMachinePathJsonTests
    {
        [Test]
        public void AnEmptyPathIsAnEmptyArray()
        {
            // The layer's root, which is what an omitted stateMachinePath means. It has to
            // be [] rather than null: a request sends [] for the root, so a response that
            // said null would not round trip.
            Assert.AreEqual("[]", AnimatorStateMachineAddress.PathJson(new string[0]));
        }

        [Test]
        public void SegmentsAreQuotedAndCommaSeparated()
        {
            Assert.AreEqual("[\"Combat\",\"Melee\"]",
                AnimatorStateMachineAddress.PathJson(new[] { "Combat", "Melee" }));
        }

        [Test]
        public void ASegmentNeedingEscapesIsEscaped()
        {
            // Unity does not forbid much in a state machine name, which is the reason the
            // address is an array rather than a joined string. A quote or a backslash in a
            // segment has to survive as content rather than closing the string, or the
            // response is not parseable JSON.
            Assert.AreEqual(@"[""say \""hi\"""",""back\\slash""]",
                AnimatorStateMachineAddress.PathJson(new[] { @"say ""hi""", @"back\slash" }));
        }

        [Test]
        public void ASegmentWithASlashIsNotTreatedAsASeparator()
        {
            // The case that makes a joined path unworkable: a name holding the separator a
            // joined form would use. It is one segment and stays one segment.
            Assert.AreEqual("[\"Combat/Melee\"]",
                AnimatorStateMachineAddress.PathJson(new[] { "Combat/Melee" }));
        }

        [Test]
        public void ANullSegmentIsNullRatherThanAnEmptyString()
        {
            // A state machine whose object was destroyed reports a null name, and the read
            // already emits null for it. Rendering it as "" would make a broken controller
            // read as one holding a machine named nothing.
            Assert.AreEqual("[\"Combat\",null]",
                AnimatorStateMachineAddress.PathJson(new[] { "Combat", null }));
        }
    }
}
