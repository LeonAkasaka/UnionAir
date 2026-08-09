using System;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Pins <see cref="ObjectRefUtils.ResolveType"/> against an assembly whose types cannot
    /// all be loaded.
    ///
    /// The condition is induced here rather than waited for. In the Editor it arrives on
    /// its own -- Unity's expression evaluator compiles into a dynamic assembly whose type
    /// is left uncreated -- which made the failure look like flakiness: the same branch
    /// passed and failed across runs with nothing in the project to explain it. A test that
    /// only fails when the Editor happens to be in that state is not a regression test.
    /// </summary>
    internal sealed class ResolveTypeAssemblyLoadTests
    {
        /// <summary>
        /// A dynamic assembly holding a TypeBuilder that is never created, which is what
        /// makes <c>GetTypes</c> throw.
        ///
        /// It cannot be unloaded -- a Run assembly lives until the domain reloads -- so it
        /// stays for the rest of the test run by design. That is the point: every test after
        /// this one runs with the condition present, and with the guard in place none of
        /// them cares.
        /// </summary>
        [OneTimeSetUp]
        public void AddAnAssemblyThatCannotEnumerateItsTypes()
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("UnionAirResolveTypeGuardTests"), AssemblyBuilderAccess.Run);
            var module = assembly.DefineDynamicModule("Probe");

            module.DefineType("UnionAirUnfinishedProbeType", TypeAttributes.Public);
            module.DefineType("UnionAirFinishedProbeType", TypeAttributes.Public).CreateType();

            Assert.Throws<ReflectionTypeLoadException>(() => assembly.GetTypes(),
                "the probe assembly must be one whose types cannot all be loaded, " +
                "or this fixture is not testing anything");
        }

        [TestCase("NoSuchTypeAnywhere")]
        [TestCase("System.String")]
        public void ANameThatResolvesToNothingStillAnswersNull(string typeName)
        {
            // The defect: this threw ReflectionTypeLoadException out of the endpoint, so a
            // request that should have answered "Unknown type" answered 500 with the
            // exception text in the body.
            Assert.IsNull(ObjectRefUtils.ResolveType(typeName, typeof(UnityEngine.Object)));
        }

        [Test]
        public void ANameThatResolvesStillResolves()
        {
            Assert.AreEqual(typeof(Transform), ObjectRefUtils.ResolveType("Transform", typeof(Component)));
            Assert.AreEqual(typeof(Rigidbody), ObjectRefUtils.ResolveType("UnityEngine.Rigidbody"));
        }

        [Test]
        public void TheTypesThatDidLoadAreStillSearched()
        {
            // The assembly is not skipped whole. Falling back to `continue` on a
            // ReflectionTypeLoadException would lose every type that loaded fine, which for
            // a real assembly is nearly all of them.
            Assert.IsNotNull(ObjectRefUtils.ResolveType("UnionAirFinishedProbeType"));
        }

        [Test]
        public void ANullEntryAmongTheLoadedTypesDoesNotThrow()
        {
            // ReflectionTypeLoadException.Types has a null wherever a type failed, so the
            // scan has to skip them rather than dereference them.
            Assert.DoesNotThrow(() => ObjectRefUtils.ResolveType("UnionAirUnfinishedProbeType"));
        }
    }
}
