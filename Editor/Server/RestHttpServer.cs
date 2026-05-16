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

        public bool IsRunning => _listener != null && _listener.IsListening;
        public int Port { get; private set; }

        /// <summary>Raised on the main thread for each incoming request path.</summary>
        public event Action<string> OnRequest;

        public void Start(int port)
        {
            if (IsRunning) Stop();

            Port = port;
            _pending = new ConcurrentQueue<HttpListenerContext>();

            _router = new RestRouter();
            _router.Register(new HealthHandler());
            _router.Register(new EditorStatusHandler());
            _router.Register(new EditorRefreshHandler());
            _router.Register(new EditorPlayHandler());
            _router.Register(new EditorLogsHandler());
            _router.Register(new CameraHandler());
            _router.Register(new SceneHandler());
            _router.Register(new SceneStatsHandler());
            _router.Register(new SceneSaveHandler());
            _router.Register(new PrefabCreateHandler());      // before AssetHandler (/api/assets/prefabs)
            _router.Register(new PrefabOverrideHandler());    // before AssetHandler (/api/assets/prefabs/...)
            _router.Register(new MaterialWriteHandler());     // before AssetHandler (/api/assets/materials)
            _router.Register(new AssetDeleteHandler());       // before AssetHandler (DELETE /api/assets/<guid>)
            _router.Register(new AssetMoveHandler());         // before AssetHandler (/api/assets/move)
            _router.Register(new GameObjectDuplicateHandler()); // before GameObjectWriteHandler (more specific path)
            _router.Register(new GameObjectReparentHandler());  // before GameObjectWriteHandler (more specific path)
            _router.Register(new GameObjectPrimitiveHandler()); // before GameObjectWriteHandler (more specific path)
            _router.Register(new GameObjectInstantiateHandler()); // before GameObjectWriteHandler (more specific path)
            _router.Register(new GameObjectBatchHandler());      // before GameObjectWriteHandler (/api/gameobjects/batch)
            _router.Register(new ComponentWriteHandler());      // before GameObjectWriteHandler (/api/gameobjects/components)
            _router.Register(new GameObjectWriteHandler());
            _router.Register(new GameObjectHandler());
            _router.Register(new AssetDependentsHandler()); // must be before AssetHandler (more specific path)
            _router.Register(new AssetHandler());
            _router.Register(new SearchGameObjectsHandler());
            _router.Register(new SearchAssetRefsHandler());

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
                try
                {
                    var requestLine = $"{ctx.Request.HttpMethod} {ctx.Request.Url.AbsolutePath}";
                    OnRequest?.Invoke(requestLine);
                    _router.Handle(ctx);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UnionAir] Error processing request: {ex.Message}");
                    try { RestResponse.SendError(ctx.Response, ex.Message); } catch { /* ignored */ }
                }
                finally
                {
                    try { ctx.Response.Close(); } catch { /* ignored */ }
                }
            }
        }

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
