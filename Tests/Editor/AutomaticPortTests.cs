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
            var result = UnionAirPortAllocator.TryStartAutomatic(
                49152,
                port =>
                {
                    tried.Add(port);
                    return UnionAirPortStartResult.Started;
                },
                () => 50000,
                out assigned);

            Assert.AreEqual(UnionAirPortStartResult.Started, result);
            Assert.AreEqual(49152, assigned);
            CollectionAssert.AreEqual(new[] { 49152 }, tried);
        }

        [Test]
        public void Automatic_FallsBackAfterARetainedConflict()
        {
            var tried = new List<int>();
            int assigned;
            var result = UnionAirPortAllocator.TryStartAutomatic(
                49152,
                port =>
                {
                    tried.Add(port);
                    return port == 49152
                        ? UnionAirPortStartResult.AddressInUse
                        : UnionAirPortStartResult.Started;
                },
                () => 50000,
                out assigned);

            Assert.AreEqual(UnionAirPortStartResult.Started, result);
            Assert.AreEqual(50000, assigned);
            CollectionAssert.AreEqual(new[] { 49152, 50000 }, tried);
        }

        [Test]
        public void Automatic_DoesNotRetryANonAddressFailure()
        {
            var allocationCount = 0;
            int assigned;
            var result = UnionAirPortAllocator.TryStartAutomatic(
                49152,
                _ => UnionAirPortStartResult.Failed,
                () =>
                {
                    allocationCount++;
                    return 50000;
                },
                out assigned);

            Assert.AreEqual(UnionAirPortStartResult.Failed, result);
            Assert.AreEqual(0, assigned);
            Assert.AreEqual(0, allocationCount);
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
                out assigned);

            Assert.AreEqual(UnionAirPortStartResult.Started, result);
            return assigned;
        }
    }
}
