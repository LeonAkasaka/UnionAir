# UnionAir — Unity REST Bridge
**English** | [日本語](index.ja.md)

UnionAir is an Editor-only package that exposes the state of the Unity Editor externally as a simple **REST API**.  
It allows any HTTP-capable client—such as LLM MCP bridges, development bots, and CI tools—to retrieve information from Unity.

---

## Setup

### 1. Install the package

Open **Window > Package Manager**, click **+**, choose **Install package from git URL...**, and enter:

```
https://github.com/LeonAkasaka/UnionAir.git
```

Alternatively, add the dependency to `Packages/manifest.json` directly:

```json
{
  "dependencies": {
    "com.leonakasaka.unionair": "https://github.com/LeonAkasaka/UnionAir.git"
  }
}
```

If the package is placed in the project's `Packages/com.leonakasaka.unionair/` folder instead, Unity detects it automatically as an embedded package.

### 2. Check the server

When you open the Unity Editor, the REST server starts automatically (default port: **8765**).

```
Window > UnionAir > REST Bridge
```

Open the EditorWindow from the menu above and check the server status.

### 3. Verify operation

```bash
curl http://localhost:8765/api/health
# => {"status":"ok","unityVersion":"6000.3.5f2"}
```

---

## Quick Start

### Get the scene hierarchy

```bash
curl http://localhost:8765/api/scene/hierarchy
```

```json
{
  "scene": "SampleScene",
  "objects": [
    {
      "name": "Main Camera",
      "path": "Main Camera",
      "isActive": true,
      "tag": "MainCamera",
      "layer": 0,
      "transform": {
        "position": { "x": 0, "y": 1, "z": -10 },
        "rotation": { "x": 0, "y": 0,  "z": 0  },
        "scale":    { "x": 1, "y": 1,  "z": 1  }
      },
      "children": []
    }
  ]
}
```

### Check the components of a specific GameObject

```bash
curl --get "http://localhost:8765/api/gameobjects" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}'
```

### Search the asset list

```bash
# All Texture2D
curl "http://localhost:8765/api/assets?type=Texture2D"

# Filter by path
curl "http://localhost:8765/api/assets?path=Assets/UI"
```

---

## Using the EditorWindow

| Item | Description |
|------|------|
| **Status** | Displays the server running state and port number |
| **Port** | The server listening port (can only be changed while stopped) |
| **Auto Start on Load** | Whether to start the server automatically when the Editor starts |
| **Start / Stop / Restart** | Manual control of the server |
| **Request Log** | Log of received requests (latest 100 entries) |

---

## Documentation

- [API Reference](api-reference.md) — Detailed specifications for all endpoints
- [Custom Controllers](custom-controllers.md) — Extension guide for application-side UnionAir APIs

---

## Lifecycle

- **When the Editor starts**: The server starts automatically via `[InitializeOnLoad]`
- **During Domain reload**: Releases the port and stops the thread, then restarts automatically after the reload
- **In Play Mode**: The server continues running. If it was stopped after Exit Play Mode, it restarts automatically
