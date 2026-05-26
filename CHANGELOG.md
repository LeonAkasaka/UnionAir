# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- `GET /api/help` — attribute-generated API manifest for LLMs, MCP bridges, and tools that cannot access the documentation directly
- ASP.NET-style attribute routing with `[UnionAirController]` and `[UnionAirEndpoint]` as the source of truth for routing, help, category state, and the EditorWindow endpoint list
- Custom handler discovery under `/api/custom/...`, managed separately in the UnionAir EditorWindow
- Category-level API enablement metadata and endpoint risk reporting for built-in and custom API discovery
- `PATCH /api/gameobjects/components` can now set and clear serialized object references, including scene GameObjects, Components, and assets such as TextAsset.
- Multi-scene API support: `GET /api/scenes`, `POST /api/scenes/new`, `POST /api/scenes/open`, `POST /api/scenes/unload`, and `POST /api/scenes/active`.
- Existing scene, search, GameObject, component, and prefab APIs now accept optional `scenePath` targeting for loaded scenes.
- Scene GameObject and Component APIs now expose `globalObjectId` values and accept them as stable target identifiers.
- GameObject, component, camera, and prefab write APIs now use typed object references (`target`, `parent`, `source`) instead of parallel path and ID fields.
- Serialized component object reference payloads now use typed `hierarchyPath`, `componentPath`, and `globalObjectId` references.
- Custom controller authors can now reuse UnionAir's scene, object reference, GlobalObjectId, and asset reference resolution through the public `UnionAirReferenceResolver` helper.
- Added `Documentation~/custom-controllers.md` with setup, request parsing, reference resolution, Play Mode policy, and security guidance for custom API implementers.
- Endpoint metadata now declares Play Mode safety policy, and write APIs are centrally blocked or require both Editor-side Play Mode scene-change permission and `allowWhilePlaying=true` while the Editor is in Play Mode.
- Play Mode scene-object writes now skip scene dirty marking and Undo registration because they are transient runtime changes.
- `POST /api/editor/menu-item` can now execute Unity Editor menu items through a disabled-by-default Editor Actions category.
- Endpoint risk metadata now includes `requestDependent` for APIs whose side effects depend on request parameters.
- `GET /api/editor/selection`, `POST /api/editor/selection`, and `POST /api/editor/ping` expose Unity Editor selection and object highlighting operations.
- `POST /api/assets/open` opens project assets in the Unity Editor, and `POST /api/assets/reimport` reimports individual assets.
- Endpoint risk metadata now includes `editorState`, and endpoints can override their category risk when they have a narrower side-effect profile.
- Built-in and custom API endpoint lists in the EditorWindow can now expand and collapse by category.

### Fixed

- Hardened JSON string escaping in API responses to correctly encode control characters and prevent malformed JSON output from string fields.
- Normalized non-finite float values (`NaN`, `Infinity`, `-Infinity`) to `null` in JSON responses to keep numeric fields JSON-compliant.

## [0.1.0] - 2026-05-17

### Added

#### Read API

- `GET /api/health` — health check
- `GET /api/scene` — current scene info (name, path, isDirty, rootCount)
- `GET /api/scene/hierarchy` — full GameObject tree with transform data (supports `?depth`, `?compact`, `?limit`, `?path`)
- `GET /api/scene/stats` — scene statistics (object counts, component/tag/layer breakdown)
- `GET /api/gameobjects` — GameObject details with serialized component properties
- `GET /api/editor/status` — Editor state (isPlaying, isPaused, isCompiling, isUpdating)
- `GET /api/editor/logs` — console log capture with type/search/limit filters
- `GET /api/cameras` — camera list with depth, FOV, and path
- `GET /api/cameras/capture` — render camera to base64 image (JPEG/PNG)
- `GET /api/cameras/capture/image` — render camera as binary image stream
- `GET /api/assets` — asset list with path/type/search filters
- `GET /api/assets/{guid}` — asset detail with dependencies and labels
- `GET /api/assets/dependents` — reverse dependency lookup
- `GET /api/search/gameobjects` — multi-criteria GameObject search
- `GET /api/search/asset-refs` — find scene references to an asset

#### Scene Write API (disabled by default)

- `POST /api/gameobjects` — create a new empty GameObject
- `POST /api/gameobjects/primitive` — create a primitive GameObject (Cube, Sphere, Capsule, Cylinder, Plane, Quad)
- `DELETE /api/gameobjects` — delete a GameObject
- `PATCH /api/gameobjects` — update GameObject properties (name, isActive, tag, layer, transform)
- `POST /api/gameobjects/duplicate` — duplicate a GameObject
- `POST /api/gameobjects/reparent` — move a GameObject to a new parent
- `POST /api/gameobjects/batch` — bulk create/update/delete in a single Undo group (HTTP 207)
- `POST /api/gameobjects/components` — add a component to a GameObject
- `DELETE /api/gameobjects/components` — remove a component from a GameObject
- `PATCH /api/gameobjects/components` — update serialized component properties
- `POST /api/scene/save` — save the current scene to disk

#### Asset Write API (disabled by default, separate toggle)

- `POST /api/editor/refresh` — trigger `AssetDatabase.Refresh()`
- `POST /api/assets/prefabs` — create a prefab from a scene GameObject
- `POST /api/assets/prefabs/apply` — apply instance overrides to the prefab asset
- `POST /api/assets/prefabs/revert` — revert a prefab instance to match the asset
- `POST /api/assets/materials` — create a new material
- `PATCH /api/assets/materials` — update material properties (Color, Float, Vector, Texture)
- `DELETE /api/assets/{guid}` — delete an asset and its `.meta` file
- `POST /api/assets/move` — move/rename an asset preserving GUID and references

#### Play Mode API (disabled by default, separate toggle)

- `POST /api/editor/play` — enter play mode
- `POST /api/editor/stop` — exit play mode
- `POST /api/editor/pause` — set or toggle pause state
- `POST /api/editor/step` — advance one frame (requires pause)

#### Infrastructure

- HTTP server via `HttpListener` (no external dependencies), default port 8765
- CORS headers (`Access-Control-Allow-Origin: *`) for cross-origin access
- Per-phase permission gating (Write / Asset Write / Play Mode toggles)
- EditorWindow UI for server control, port configuration, and request log
- Auto-start on Editor load via `[InitializeOnLoad]`
- Graceful shutdown on domain reload; auto-restart after domain reload and play mode exit
- Console log capture with 1000-entry ring buffer (`LogStore`)
