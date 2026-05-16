using System.Net;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Implemented by each API endpoint handler.
    /// </summary>
    internal interface IRequestHandler
    {
        /// <summary>Returns true if this handler should process the given request.</summary>
        bool CanHandle(HttpListenerRequest request);

        /// <summary>Writes the response for the given request.</summary>
        void Handle(HttpListenerRequest request, HttpListenerResponse response);
    }
}
