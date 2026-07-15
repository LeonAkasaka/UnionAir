using System;
using System.Collections.Concurrent;
using System.Net;
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
        private HttpListener _listener;
        private Thread _listenerThread;
        private ConcurrentQueue<HttpListenerContext> _pending;
        private RestRouter _router;

        /// <summary>
        /// Gets whether the HTTP listener is currently running.
        /// </summary>
        public bool IsRunning => _listener != null && _listener.IsListening;

        /// <summary>
        /// Gets the port currently assigned to the server.
        /// </summary>
        public int Port { get; private set; }

        /// <summary>Raised on the main thread for each incoming request path.</summary>
        public event Action<string> OnRequest;

        /// <summary>
        /// Starts the HTTP listener on <c>localhost</c>.
        /// </summary>
        /// <param name="port">TCP port to bind, usually from <see cref="UnionAirSettings.Port"/>.</param>
        public void Start(int port)
        {
            if (IsRunning) Stop();

            Port = port;
            _pending = new ConcurrentQueue<HttpListenerContext>();

            _router = new RestRouter();

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");

            try
            {
                _listener.Start();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnionAir] Failed to start server on port {port}: {ex.Message}");
                _listener = null;
                return;
            }

            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "UnionAir-HttpListener"
            };
            _listenerThread.Start();

            EditorApplication.update += ProcessPending;
            Debug.Log($"[UnionAir] REST API server started on http://localhost:{port}/");
        }

        private void ListenLoop()
        {
            while (_listener != null && _listener.IsListening)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    _pending.Enqueue(ctx);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private void ProcessPending()
        {
            while (_pending != null && _pending.TryDequeue(out var ctx))
            {
                var completed = true;
                try
                {
                    var requestLine = $"{ctx.Request.HttpMethod} {ctx.Request.Url.AbsolutePath}";
                    OnRequest?.Invoke(requestLine);
                    completed = _router.Handle(ctx);
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
        {
            EditorApplication.update -= ProcessPending;

            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch { /* ignored */ }

            _listener = null;
            _listenerThread = null;
            _pending = null;

            Debug.Log("[UnionAir] REST API server stopped.");
        }
    }
}
