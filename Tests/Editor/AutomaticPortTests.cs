using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class AutomaticPortTests
    {
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(65535, true)]
        [TestCase(-1, false)]
        [TestCase(65536, false)]
        public void ConfiguredPort_ValidatesAutomaticAndFixedRanges(int port, bool expected)
        {
            Assert.AreEqual(expected, UnionAirPortAllocator.IsValidConfiguredPort(port));
        }

        [Test]
        public void Automatic_TriesTheRetainedPortFirst()
        {
            var tried = new List<int>();
            int assigned;
            Exception allocationError;
            var result = UnionAirPortAllocator.TryStartAutomatic(
                49152,
                port =>
                {
                    tried.Add(port);
                    return UnionAirPortStartResult.Started;
                },
                _ => 50000,
                out assigned,
                out allocationError);

            Assert.AreEqual(UnionAirPortStartResult.Started, result);
            Assert.AreEqual(49152, assigned);
            Assert.IsNull(allocationError);
            CollectionAssert.AreEqual(new[] { 49152 }, tried);
        }

        [Test]
        public void Automatic_FallsBackAfterARetainedConflict()
        {
            var tried = new List<int>();
            int assigned;
            Exception allocationError;
            var result = UnionAirPortAllocator.TryStartAutomatic(
                49152,
                port =>
                {
                    tried.Add(port);
                    return port == 49152
                        ? UnionAirPortStartResult.AddressInUse
                        : UnionAirPortStartResult.Started;
                },
                _ => 50000,
                out assigned,
                out allocationError);

            Assert.AreEqual(UnionAirPortStartResult.Started, result);
            Assert.AreEqual(50000, assigned);
            Assert.IsNull(allocationError);
            CollectionAssert.AreEqual(new[] { 49152, 50000 }, tried);
        }

        [Test]
        public void Automatic_DefersFreshAllocationAfterTheFirstRetainedConflict()
        {
            var allocationCount = 0;
            int assigned;
            Exception allocationError;
            var result = UnionAirPortAllocator.TryStartAutomatic(
                49152,
                _ => UnionAirPortStartResult.AddressInUse,
                _ =>
                {
                    allocationCount++;
                    return 50000;
                },
                out assigned,
                out allocationError,
                deferRetainedAddressInUse: true);

            Assert.AreEqual(UnionAirPortStartResult.AddressInUse, result);
            Assert.AreEqual(0, assigned);
            Assert.IsNull(allocationError);
            Assert.AreEqual(0, allocationCount);
        }

        [Test]
        public void Automatic_TriesAnotherCandidateAfterPortSpecificFailure()
        {
            var tried = new List<int>();
            var candidates = new Queue<int>(new[] { 50000, 50001 });
            int assigned;
            Exception allocationError;
            var result = UnionAirPortAllocator.TryStartAutomatic(
                0,
                port =>
                {
                    tried.Add(port);
                    return port == 50000
                        ? UnionAirPortStartResult.CandidateUnavailable
                        : UnionAirPortStartResult.Started;
                },
                _ => candidates.Dequeue(),
                out assigned,
                out allocationError);

            Assert.AreEqual(UnionAirPortStartResult.Started, result);
            Assert.AreEqual(50001, assigned);
            Assert.IsNull(allocationError);
            CollectionAssert.AreEqual(new[] { 50000, 50001 }, tried);
        }

        [Test]
        public void Automatic_DuplicateAllocationsDoNotConsumeCandidateSlots()
        {
            var allocated = new Queue<int>(new[] { 50000, 50000, 50000, 50001 });
            var tried = new List<int>();
            int assigned;
            Exception allocationError;
            var result = UnionAirPortAllocator.TryStartAutomatic(
                0,
                port =>
                {
                    tried.Add(port);
                    return port == 50000
                        ? UnionAirPortStartResult.AddressInUse
                        : UnionAirPortStartResult.Started;
                },
                _ => allocated.Dequeue(),
                out assigned,
                out allocationError);

            Assert.AreEqual(UnionAirPortStartResult.Started, result);
            Assert.AreEqual(50001, assigned);
            Assert.IsNull(allocationError);
            CollectionAssert.AreEqual(new[] { 50000, 50001 }, tried);
        }

        [Test]
        public void Automatic_StopsAfterEightDistinctCandidateFailures()
        {
            var nextPort = 50000;
            var tried = new List<int>();
            int assigned;
            Exception allocationError;
            var result = UnionAirPortAllocator.TryStartAutomatic(
                0,
                port =>
                {
                    tried.Add(port);
                    return UnionAirPortStartResult.CandidateUnavailable;
                },
                _ => nextPort++,
                out assigned,
                out allocationError);

            Assert.AreEqual(UnionAirPortStartResult.CandidateUnavailable, result);
            Assert.AreEqual(0, assigned);
            Assert.IsNull(allocationError);
            Assert.AreEqual(UnionAirPortAllocator.MaximumFreshCandidates, tried.Count);
            Assert.AreEqual(50008, nextPort);
        }

        [Test]
        public void Automatic_AllocationExceptionReturnsFailed()
        {
            var expected = new SocketException((int)SocketError.NoBufferSpaceAvailable);
            int assigned;
            Exception allocationError;
            var result = UnionAirPortAllocator.TryStartAutomatic(
                0,
                _ => AssertAndReturnUnexpectedStart(),
                _ => throw expected,
                out assigned,
                out allocationError);

            Assert.AreEqual(UnionAirPortStartResult.Failed, result);
            Assert.AreEqual(0, assigned);
            Assert.AreSame(expected, allocationError);
        }

        [Test]
        public void Automatic_DoesNotRetryAFatalStartFailure()
        {
            var allocationCount = 0;
            int assigned;
            Exception allocationError;
            var result = UnionAirPortAllocator.TryStartAutomatic(
                49152,
                _ => UnionAirPortStartResult.Failed,
                _ =>
                {
                    allocationCount++;
                    return 50000;
                },
                out assigned,
                out allocationError);

            Assert.AreEqual(UnionAirPortStartResult.Failed, result);
            Assert.AreEqual(0, assigned);
            Assert.IsNull(allocationError);
            Assert.AreEqual(0, allocationCount);
        }

        [Test]
        public void ListenerStartClassification_TreatsAccessDeniedAsCandidateSpecific()
        {
            Assert.AreEqual(
                UnionAirPortStartResult.CandidateUnavailable,
                RestHttpServer.ClassifyListenerStartException(
                    new HttpListenerException(5)));
            Assert.AreEqual(
                UnionAirPortStartResult.Failed,
                RestHttpServer.ClassifyListenerStartException(
                    new InvalidOperationException("fatal")));
        }

        [Test]
        public void LoopbackAllocation_ExcludesPreviouslyRejectedPorts()
        {
            var first = UnionAirPortAllocator.AllocateLoopbackPort();
            var second = UnionAirPortAllocator.AllocateLoopbackPort(new[] { first });

            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void Automatic_TwoLiveListenersReceiveDifferentPorts()
        {
            var listeners = new List<TcpListener>();
            try
            {
                var first = StartAutomaticListener(listeners);
                var second = StartAutomaticListener(listeners);
                Assert.AreNotEqual(first, second);
            }
            finally
            {
                foreach (var listener in listeners)
                    listener.Stop();
            }
        }

        private static int StartAutomaticListener(List<TcpListener> listeners)
        {
            int assigned;
            Exception allocationError;
            var result = UnionAirPortAllocator.TryStartAutomatic(
                0,
                port =>
                {
                    var listener = new TcpListener(IPAddress.Loopback, port);
                    try
                    {
                        listener.Start();
                        listeners.Add(listener);
                        return UnionAirPortStartResult.Started;
                    }
                    catch (SocketException ex)
                    {
                        listener.Stop();
                        return ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                            ? UnionAirPortStartResult.AddressInUse
                            : UnionAirPortStartResult.Failed;
                    }
                },
                UnionAirPortAllocator.AllocateLoopbackPort,
                out assigned,
                out allocationError);

            Assert.AreEqual(UnionAirPortStartResult.Started, result);
            Assert.IsNull(allocationError);
            return assigned;
        }

        private static UnionAirPortStartResult AssertAndReturnUnexpectedStart()
        {
            Assert.Fail("The start delegate must not run when allocation fails.");
            return UnionAirPortStartResult.Failed;
        }
    }
}
