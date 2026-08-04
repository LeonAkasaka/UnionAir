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
        Failed
    }

    /// <summary>Selects concrete loopback ports for Automatic mode.</summary>
    internal static class UnionAirPortAllocator
    {
        internal const int MaximumFreshCandidates = 8;

        internal static bool IsValidConfiguredPort(int port)
            => port >= 0 && port <= 65535;

        internal static bool IsValidConcretePort(int port)
            => port >= 1 && port <= 65535;

        internal static int AllocateLoopbackPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                probe.Start();
                return ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
        }

        internal static UnionAirPortStartResult TryStartAutomatic(
            int retainedPort,
            Func<int, UnionAirPortStartResult> tryStart,
            Func<int> allocate,
            out int assignedPort)
        {
            assignedPort = 0;
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
            }

            for (var i = 0; i < MaximumFreshCandidates; i++)
            {
                var candidate = allocate();
                if (!IsValidConcretePort(candidate) || !tried.Add(candidate))
                    continue;

                var result = tryStart(candidate);
                if (result == UnionAirPortStartResult.Started)
                {
                    assignedPort = candidate;
                    return result;
                }
                if (result == UnionAirPortStartResult.Failed)
                    return result;
            }

            return UnionAirPortStartResult.AddressInUse;
        }
    }
}
