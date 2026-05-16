# UnionAir — Unity REST Bridge

UnionAir exposes Unity Editor state as a simple **REST API** over HTTP, making it easy to integrate with LLM MCP bridges, development bots, CI tooling, or any HTTP client.

## Setup

1. The package auto-starts an HTTP server when the Unity Editor loads.
2. Open **Window > UnionAir > REST Bridge** to view status and configure the port.

## Default Port

`8765` — configurable via the EditorWindow.

## Endpoints

| Group | Scope | Security |
|-------|-------|----------|
| **Read** | Scene hierarchy, GameObjects, assets, cameras, logs, search | Always enabled |
| **Scene Write** | Create/update/delete GameObjects and components, save scene | Disabled by default |
| **Asset Write** | Prefabs, materials, asset files, AssetDatabase refresh | Disabled by default |
| **Play Mode** | Enter/exit/pause/step play mode | Disabled by default |

> All write operations are Undo-able in the Unity Editor (Ctrl+Z).  
> See **[API Reference](Documentation~/api-reference.md)** for the full endpoint list and request/response details.

## Quick Example

```bash
# Health check
curl http://localhost:8765/api/health

# Scene hierarchy
curl http://localhost:8765/api/scene/hierarchy

# Specific GameObject
curl "http://localhost:8765/api/gameobjects?path=Main Camera"

# All assets of type Texture2D
curl "http://localhost:8765/api/assets?type=Texture2D"

# Create a new empty GameObject (requires Write API enabled)
curl -X POST http://localhost:8765/api/gameobjects \
  -H "Content-Type: application/json" \
  -d '{"name":"MyObject","parentPath":"Canvas"}'
```

## MCP Bridge

To use with an LLM MCP client, run a separate Node.js MCP bridge that calls these REST endpoints. The Unity package itself has no MCP dependency.

## Documentation

- **[Getting Started](Documentation~/index.md)** — Setup, EditorWindow guide, lifecycle
- **[API Reference](Documentation~/api-reference.md)** — Full endpoint reference with request/response examples