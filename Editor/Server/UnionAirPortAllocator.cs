using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace LeonAkasaka.UnionAir.Editor
{
    internal enum UnionAirPortStartResult
    {
        Started,
        AddressInUse,
        CandidateUnavailable,
        Failed
    }

    /// <summary>Selects concrete loopback ports for Automatic mode.</summary>
    internal static class UnionAirPortAllocator
    {
        internal const int MaximumFreshCandidates = 8;
        internal const int MaximumAllocationAttempts = MaximumFreshCandidates * 4;

        internal static bool IsValidConfiguredPort(int port)
            => port >= 0 && port <= 65535;

        internal static bool IsValidConcretePort(int port)
            => port >= 1 && port <= 65535;

        internal static int AllocateLoopbackPort()
            => AllocateLoopbackPort(null);

        internal static int AllocateLoopbackPort(IEnumerable<int> excludedPorts)
        {
            var blockers = new List<TcpListener>();
            TcpListener probe = null;
            try
            {
                if (excludedPorts != null)
                {
                    foreach (var port in excludedPorts)
                    {
                        if (!IsValidConcretePort(port))
                            continue;

                        var blocker = new TcpListener(IPAddress.Loopback, port);
                        try
                        {
                            blocker.Start();
                            blockers.Add(blocker);
                        }
                        catch (SocketException ex)
                        {
                            blocker.Stop();
                            if (ex.SocketErrorCode != SocketError.AddressAlreadyInUse)
                                throw;
                        }
                    }
                }

                probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                return ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                if (probe != null)
                    probe.Stop();
                foreach (var blocker in blockers)
                    blocker.Stop();
            }
        }

        internal static UnionAirPortStartResult TryStartAutomatic(
            int retainedPort,
            Func<int, UnionAirPortStartResult> tryStart,
            Func<IEnumerable<int>, int> allocate,
            out int assignedPort,
            out Exception allocationError,
            bool deferRetainedAddressInUse = false)
        {
            assignedPort = 0;
            allocationError = null;
            var tried = new HashSet<int>();

            if (IsValidConcretePort(retainedPort))
            {
                tried.Add(retainedPort);
                var retainedResult = tryStart(retainedPort);
                if (retainedResult == UnionAirPortStartResult.Started)
                {
                    assignedPort = retainedPort;
                    return retainedResult;
                }
                if (retainedResult == UnionAirPortStartResult.Failed)
                    return retainedResult;
                if (retainedResult == UnionAirPortStartResult.AddressInUse &&
                    deferRetainedAddressInUse)
                    return retainedResult;
            }

            var attemptedCandidates = 0;
            var allocationAttempts = 0;
            var lastCandidateResult = UnionAirPortStartResult.CandidateUnavailable;
            while (attemptedCandidates < MaximumFreshCandidates &&
                   allocationAttempts < MaximumAllocationAttempts)
            {
                int candidate;
                allocationAttempts++;
                try
                {
                    candidate = allocate(tried);
                }
                catch (Exception ex)
                {
                    allocationError = ex;
                    return UnionAirPortStartResult.Failed;
                }

                if (!IsValidConcretePort(candidate) || !tried.Add(candidate))
                    continue;

                attemptedCandidates++;
                var result = tryStart(candidate);
                if (result == UnionAirPortStartResult.Started)
                {
                    assignedPort = candidate;
                    return result;
                }
                if (result == UnionAirPortStartResult.Failed)
                    return result;
                lastCandidateResult = result;
            }

            if (attemptedCandidates == 0)
            {
                allocationError = new InvalidOperationException(
                    $"Automatic port allocation did not produce a distinct valid port after " +
                    $"{MaximumAllocationAttempts} attempts.");
                return UnionAirPortStartResult.Failed;
            }

            return lastCandidateResult;
        }
    }
}
