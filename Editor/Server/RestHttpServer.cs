using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// HTTP server that listens on a background thread and dispatches requests
    /// to <see cref="RestRouter"/> on the Unity main thread via EditorApplication.update.
    /// </summary>
    public class RestHttpServer
    {
        private sealed class ListenerState
        {
            internal readonly HttpListener Listener;
            internal readonly ConcurrentQueue<HttpListenerContext> Pending;
            internal readonly RestRouter Router;
            internal Thread Thread;
            internal volatile bool Stopping;
            internal volatile bool ThreadExited;

            internal ListenerState(
                HttpListener listener,
                ConcurrentQueue<HttpListenerContext> pending,
                RestRouter router)
            {
                Listener = listener;
                Pending = pending;
                Router = router;
            }
        }

        private readonly string _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);
        private ListenerState _state;
        private int _lifecycleGeneration;
        private UnionAirPortStartResult _lastStartResult = UnionAirPortStartResult.Failed;
        private Exception _lastStartException;

        /// <summary>Raised on the main thread after an unexpected listener thread exit is cleaned up.</summary>
        internal event Action<string> UnexpectedlyStopped;

        /// <summary>
        /// Gets whether the HTTP listener is currently running.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                var state = _state;
                if (state == null)
                    return false;

                if (state.ThreadExited)
                    return false;

                try
                {
                    return state.Listener.IsListening &&
                           state.Thread != null &&
                           state.Thread.IsAlive;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Gets the port currently assigned to the server.
        /// </summary>
        public int Port { get; private set; }

        /// <summary>Raised on the main thread for each incoming request path.</summary>
        public event Action<string> OnRequest;

        internal bool LastStartFailureWasAddressInUse =>
            _lastStartResult == UnionAirPortStartResult.AddressInUse;

        /// <summary>
        /// Starts the HTTP listener on <c>localhost</c>.
        /// </summary>
        /// <param name="port">TCP port to bind, usually from <see cref="UnionAirSettings.Port"/>.</param>
        public void Start(int port)
        {
            TryStart(port, "manual", false);
        }

        internal void SetLifecycleGeneration(int generation)
            => _lifecycleGeneration = generation;

        internal bool TryStart(
            int port,
            string reason,
            bool suppressAddressInUseError,
            bool deferAutomaticFallback = false)
        {
            if (!UnionAirPortAllocator.IsValidConfiguredPort(port))
            {
                _lastStartResult = UnionAirPortStartResult.Failed;
                Debug.LogError(
                    $"[UnionAir] Invalid configured port {port}. Use 0 for Automatic or 1..65535 for Fixed.");
                return false;
            }

            if (port != 0)
                return TryStartConcrete(port, reason, suppressAddressInUseError);

            var retained = UnionAirSession.LoadAutomaticPort();
            int assigned;
            Exception allocationError;
            var result = UnionAirPortAllocator.TryStartAutomatic(
                retained,
                candidate =>
                {
                    if (TryStartConcrete(
                            candidate,
                            reason + "-automatic",
                            true,
                            true))
                        return UnionAirPortStartResult.Started;
                    return _lastStartResult;
                },
                UnionAirPortAllocator.AllocateLoopbackPort,
                out assigned,
                out allocationError,
                deferAutomaticFallback);

            if (result == UnionAirPortStartResult.Started)
            {
                UnionAirSession.SaveAutomaticPort(assigned);
                LogLifecycle(
                    $"automatic port assigned configuredPort=0 retainedPort={retained} assignedPort={assigned}");
                return true;
            }

            _lastStartResult = result;
            if (allocationError != null)
            {
                _lastStartException = allocationError;
                var message =
                    $"[UnionAir] Failed to allocate an Automatic loopback port: {allocationError.Message}";
                Debug.LogError(message);
                UnionAirLifecycleDiagnostics.Record(
                    $"{LifecyclePrefix} automatic port allocation failed " +
                    DescribeException(allocationError));
                UnionAirLifecycleDiagnostics.DumpFailure(
                    "automatic port allocation failed");
                return false;
            }

            if (result == UnionAirPortStartResult.CandidateUnavailable)
            {
                var detail = _lastStartException == null
                    ? "the listener rejected every candidate"
                    : _lastStartException.Message;
                Debug.LogError(
                    $"[UnionAir] Automatic server startup could not use any fresh port candidate: {detail}");
                UnionAirLifecycleDiagnostics.DumpFailure(
                    "automatic port candidates were unavailable");
                return false;
            }

            if (LastStartFailureWasAddressInUse && !suppressAddressInUseError)
                Debug.LogError(
                    $"[UnionAir] Automatic server startup exhausted " +
                    $"{UnionAirPortAllocator.MaximumFreshCandidates} fresh port candidates.");
            return false;
        }

        private bool TryStartConcrete(
            int port,
            string reason,
            bool suppressAddressInUseError,
            bool suppressCandidateUnavailableError = false)
        {
            if (_state != null)
                StopInternal("replacement-start");

            _lastStartResult = UnionAirPortStartResult.Failed;
            _lastStartException = null;
            var listener = new HttpListener();
            var pending = new ConcurrentQueue<HttpListenerContext>();
            var state = new ListenerState(listener, pending, new RestRouter());

            LogLifecycle($"start begin reason={reason} port={port}");

            try
            {
                listener.Prefixes.Add($"http://localhost:{port}/");
                listener.Start();
            }
            catch (Exception ex)
            {
                CloseAfterFailedStart(
                    listener,
                    port,
                    ex,
                    suppressAddressInUseError,
                    suppressCandidateUnavailableError);
                return false;
            }

            var listenerThread = new Thread(() => ListenLoop(state))
            {
                IsBackground = true,
                Name = $"UnionAir-HttpListener-g{_lifecycleGeneration}"
            };
            state.Thread = listenerThread;

            try
            {
                listenerThread.Start();
            }
            catch (Exception ex)
            {
                _lastStartResult = UnionAirPortStartResult.Failed;
                _lastStartException = ex;
                var message =
                    $"{LifecyclePrefix} listener thread failed to start port={port} {DescribeException(ex)}";
                UnionAirLifecycleDiagnostics.Record(message);
                Debug.LogError(
                    $"[UnionAir] Failed to start the listener thread on port {port}: {ex.Message}");
                UnionAirLifecycleDiagnostics.DumpFailure("listener thread failed to start");
                CloseListener(listener, "failed-thread-start");
                return false;
            }

            _state = state;
            Port = port;
            _lastStartResult = UnionAirPortStartResult.Started;
            EditorApplication.update -= ProcessPending;
            EditorApplication.update += ProcessPending;
            LogLifecycle(
                $"start complete reason={reason} port={port} thread={listenerThread.ManagedThreadId}");
            Debug.Log($"[UnionAir] REST API server started on http://localhost:{port}/");
            UnionAirEndpointDiscovery.Publish(port);
            return true;
        }

        private void ListenLoop(ListenerState state)
        {
            var exitReason = "listener-stopped";
            Exception exitException = null;

            while (SafeIsListening(state.Listener))
            {
                try
                {
                    var ctx = state.Listener.GetContext();
                    state.Pending.Enqueue(ctx);
                }
                catch (HttpListenerException ex)
                {
                    exitReason = "http-listener-exception";
                    exitException = ex;
                    break;
                }
                catch (ObjectDisposedException ex)
                {
                    exitReason = "listener-disposed";
                    exitException = ex;
                    break;
                }
                catch (Exception ex)
                {
                    exitReason = "unexpected-exception";
                    exitException = ex;
                    break;
                }
            }

            try
            {
                var exceptionDetails =
                    exitException == null ? "" : " " + DescribeException(exitException);
                UnionAirLifecycleDiagnostics.RecordFromBackground(
                    $"{LifecyclePrefix} listener thread exit thread={Thread.CurrentThread.ManagedThreadId} " +
                    $"reason={exitReason}{exceptionDetails}");
            }
            finally
            {
                state.ThreadExited = true;
            }
        }

        private void ProcessPending()
        {
            UnionAirLifecycleDiagnostics.FlushBackground();
            var state = _state;
            if (state == null)
                return;

            if (state.ThreadExited && !state.Stopping)
            {
                const string reason = "listener-thread-died";
                LogLifecycle($"unexpected stop detected reason={reason}");
                StopInternal(reason);
                try
                {
                    UnexpectedlyStopped?.Invoke(reason);
                }
                finally
                {
                    UnionAirLifecycleDiagnostics.DumpFailure(
                        "listener thread exited unexpectedly");
                }
                return;
            }

            while (state.Pending.TryDequeue(out var ctx))
            {
                var completed = true;
                try
                {
                    var requestLine = $"{ctx.Request.HttpMethod} {ctx.Request.Url.AbsolutePath}";
                    OnRequest?.Invoke(requestLine);
                    completed = state.Router.Handle(ctx);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UnionAir] Error processing request: {ex.Message}");
                    try { RestResponse.SendError(ctx.Response, ex.Message); } catch { /* ignored */ }
                }
                finally
                {
                    if (completed)
                    {
                        try { ctx.Response.Close(); } catch { /* ignored */ }
                    }
                }
            }
        }

        /// <summary>
        /// Stops the HTTP listener and detaches pending request processing from the Unity editor update loop.
        /// </summary>
        public void Stop()
            => StopInternal("manual");

        internal void Stop(string reason)
            => StopInternal(reason);

        private void StopInternal(string reason)
        {
            EditorApplication.update -= ProcessPending;

            var state = _state;
            _state = null;
            if (state == null)
            {
                LogLifecycle($"stop skipped reason={reason} state=none");
                return;
            }

            state.Stopping = true;
            var listener = state.Listener;
            var listenerThread = state.Thread;
            int pendingCount;
            try { pendingCount = state.Pending.Count; }
            catch { pendingCount = -1; }
            LogLifecycle(
                $"stop begin reason={reason} port={Port} listening={SafeIsListening(listener)} " +
                $"pending={pendingCount} thread={DescribeThread(listenerThread)}");

            try
            {
                listener.Stop();
                LogLifecycle("listener.Stop succeeded");
            }
            catch (Exception ex)
            {
                var message = $"{LifecyclePrefix} listener.Stop failed {DescribeException(ex)}";
                UnionAirLifecycleDiagnostics.Record(message);
                Debug.LogWarning(message);
                UnionAirLifecycleDiagnostics.DumpFailure("listener.Stop failed");
            }

            CloseListener(listener, reason);

            if (listenerThread != null &&
                listenerThread != Thread.CurrentThread &&
                listenerThread.IsAlive)
            {
                try
                {
                    if (listenerThread.Join(1000))
                        LogLifecycle($"listener thread joined thread={listenerThread.ManagedThreadId}");
                    else
                    {
                        var message =
                            $"{LifecyclePrefix} listener thread join timed out " +
                            $"thread={listenerThread.ManagedThreadId} state={listenerThread.ThreadState}";
                        UnionAirLifecycleDiagnostics.Record(message);
                        Debug.LogWarning(message);
                        UnionAirLifecycleDiagnostics.DumpFailure("listener thread join timed out");
                    }
                }
                catch (Exception ex)
                {
                    var message =
                        $"{LifecyclePrefix} listener thread join failed {DescribeException(ex)}";
                    UnionAirLifecycleDiagnostics.Record(message);
                    Debug.LogWarning(message);
                    UnionAirLifecycleDiagnostics.DumpFailure("listener thread join failed");
                }
            }

            UnionAirLifecycleDiagnostics.FlushBackground();
            var closedPending = 0;
            while (state.Pending.TryDequeue(out var context))
            {
                try { context.Response.Close(); }
                catch { /* The listener may already have aborted the response. */ }
                closedPending++;
            }

            LogLifecycle(
                $"stop complete reason={reason} port={Port} closedPending={closedPending} " +
                $"thread={DescribeThread(listenerThread)}");
            UnionAirEndpointDiscovery.RemoveOwned(Port);
            Debug.Log("[UnionAir] REST API server stopped.");
        }

        private void CloseAfterFailedStart(
            HttpListener listener,
            int port,
            Exception startException,
            bool suppressAddressInUseError,
            bool suppressCandidateUnavailableError)
        {
            _lastStartResult = ClassifyListenerStartException(startException);
            _lastStartException = startException;
            var diagnosticMessage =
                $"{LifecyclePrefix} start failed result={_lastStartResult} " +
                $"{DescribeException(startException)}";
            UnionAirLifecycleDiagnostics.Record(diagnosticMessage);
            var suppress =
                (_lastStartResult == UnionAirPortStartResult.AddressInUse &&
                 suppressAddressInUseError) ||
                (_lastStartResult == UnionAirPortStartResult.CandidateUnavailable &&
                 suppressCandidateUnavailableError);
            if (!suppress)
            {
                Debug.LogError(
                    $"[UnionAir] Failed to start server on port {port}: {startException.Message}");
                UnionAirLifecycleDiagnostics.DumpFailure(
                    $"server startup failed on port {port}: {startException.Message}");
            }
            CloseListener(listener, "failed-start");
        }

        private void CloseListener(HttpListener listener, string reason)
        {
            try
            {
                listener.Close();
                LogLifecycle($"listener.Close succeeded reason={reason}");
                return;
            }
            catch (Exception ex)
            {
                var message =
                    $"{LifecyclePrefix} listener.Close failed reason={reason} {DescribeException(ex)}";
                UnionAirLifecycleDiagnostics.Record(message);
                Debug.LogWarning(message);
                UnionAirLifecycleDiagnostics.DumpFailure("listener.Close failed");
            }

            try
            {
                listener.Abort();
                LogLifecycle($"listener.Abort succeeded reason={reason}");
            }
            catch (Exception ex)
            {
                var message =
                    $"{LifecyclePrefix} listener.Abort failed reason={reason} {DescribeException(ex)}";
                UnionAirLifecycleDiagnostics.Record(message);
                Debug.LogError(message);
                UnionAirLifecycleDiagnostics.DumpFailure("listener.Abort failed");
            }
        }

        private static bool SafeIsListening(HttpListener listener)
        {
            try { return listener != null && listener.IsListening; }
            catch { return false; }
        }

        private static string DescribeThread(Thread thread)
        {
            if (thread == null)
                return "none";

            try
            {
                return $"{thread.ManagedThreadId}:{thread.ThreadState}";
            }
            catch
            {
                return "unavailable";
            }
        }

        internal static UnionAirPortStartResult ClassifyListenerStartException(
            Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is SocketException socketException &&
                    socketException.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    return UnionAirPortStartResult.AddressInUse;

                if (current is HttpListenerException listenerException)
                {
                    if (listenerException.NativeErrorCode == 10048 ||
                        listenerException.NativeErrorCode == 183 ||
                        listenerException.NativeErrorCode == 32)
                        return UnionAirPortStartResult.AddressInUse;

                    if (listenerException.NativeErrorCode == 5)
                        return UnionAirPortStartResult.CandidateUnavailable;
                }
            }

            return UnionAirPortStartResult.Failed;
        }

        private static string DescribeException(Exception exception)
        {
            var sb = new StringBuilder();
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (sb.Length > 0)
                    sb.Append(" inner=");

                sb.Append(current.GetType().FullName);
                sb.Append(" message=\"");
                sb.Append(current.Message.Replace("\"", "\\\""));
                sb.Append("\" hresult=0x");
                sb.Append(current.HResult.ToString("X8"));

                if (current is SocketException socketException)
                {
                    sb.Append(" socketError=");
                    sb.Append(socketException.SocketErrorCode);
                    sb.Append(" nativeCode=");
                    sb.Append(socketException.NativeErrorCode);
                }
                else if (current is HttpListenerException listenerException)
                {
                    sb.Append(" nativeCode=");
                    sb.Append(listenerException.NativeErrorCode);
                }
            }

            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                sb.Append("\nstack=");
                sb.Append(exception.StackTrace);
            }
            return sb.ToString();
        }

        private string LifecyclePrefix =>
            $"[UnionAir] lifecycle process={System.Diagnostics.Process.GetCurrentProcess().Id} " +
            $"generation={_lifecycleGeneration} server={_instanceId}";

        private void LogLifecycle(string message)
            => UnionAirLifecycleDiagnostics.Record($"{LifecyclePrefix} {message}");
    }
}
