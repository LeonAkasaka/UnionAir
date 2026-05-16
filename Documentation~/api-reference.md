# API リファレンス

ベース URL: `http://localhost:<port>/api/`（デフォルトポート: **8765**）

すべてのレスポンスは `Content-Type: application/json; charset=utf-8` で返され、CORS ヘッダー (`Access-Control-Allow-Origin: *`) が付与されます。

---

## GET /api/health

サーバーの稼働確認。

### レスポンス

```json
{
  "status": "ok",
  "unityVersion": "6000.3.5f2"
}
```

---

## GET /api/editor/status

Unity Editor の実行状態を返します。

### レスポンス

```json
{
  "isPlaying":   false,
  "isPaused":    false,
  "isCompiling": false,
  "isUpdating":  false,
  "unityVersion": "6000.3.5f2"
}
```

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `isPlaying` | bool | Play モードが有効か (`EditorApplication.isPlaying`) |
| `isPaused` | bool | Play モードで一時停止中か (`EditorApplication.isPaused`) |
| `isCompiling` | bool | スクリプトをコンパイル中か (`EditorApplication.isCompiling`) |
| `isUpdating` | bool | アセット更新処理中か (`EditorApplication.isUpdating`) |
| `unityVersion` | string | Unity バージョン文字列 |

---

## GET /api/editor/logs

Unity Console のログを返します。エディター起動（または最後のドメインリロード）以降に記録されたログが対象です。最大 1000 件をリングバッファで保持します。

### クエリパラメーター

| パラメーター | デフォルト | 説明 |
|-------------|-----------|------|
| `type` | `all` | `log` / `warning` / `error` / `exception` / `assert` / `all` |
| `search` | ―  | メッセージへの部分一致フィルター（大文字小文字無視） |
| `limit` | `100` | 最大返却件数（上限: 1000） |

### レスポンス

```json
{
  "count": 2,
  "logs": [
    {
      "type": "error",
      "message": "NullReferenceException: Object reference not set...",
      "stackTrace": "MyScript.Update () (at Assets/MyScript.cs:42)",
      "timestamp": "2026-05-16T04:12:00"
    },
    {
      "type": "warning",
      "message": "Shader 'Custom/Foo' has no shadows pass",
      "stackTrace": "",
      "timestamp": "2026-05-16T04:11:58"
    }
  ]
}
```

> ログは新しい順（`timestamp` 降順）で返されます。  
> ドメインリロード前に `StopCapturing()` が呼ばれるため、リロードをまたいだログは保持されません。

### 使用例

```bash
# エラーと例外のみ最新 20 件
curl "http://localhost:8765/api/editor/logs?type=error&limit=20"

# "NullReference" を含むログ
curl "http://localhost:8765/api/editor/logs?search=NullReference"
```

---

## GET /api/cameras

シーン内の全 Camera コンポーネントの一覧を返します。

### レスポンス

```json
{
  "count": 1,
  "cameras": [
    {
      "path": "Main Camera",
      "name": "Main Camera",
      "enabled": true,
      "depth": -1,
      "fieldOfView": 60.0,
      "isOrthographic": false,
      "tag": "MainCamera"
    }
  ]
}
```

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `path` | string | GameObject の階層パス（`/api/cameras/capture` の `path` パラメーターに使用） |
| `depth` | float | 描画順序（数値が大きいほど後から描画） |
| `fieldOfView` | float | 垂直視野角（`isOrthographic: true` のときは無意味） |

---

## GET /api/cameras/capture

指定カメラで `camera.Render()` を実行し、結果を base64 エンコード画像として返します。  
Edit モード・Play モード両方で動作します。

### クエリパラメーター

| パラメーター | デフォルト | 説明 |
|-------------|-----------|------|
| `path` | **必須** | カメラが付いた GameObject の階層パス（例: `Main Camera`） |
| `width` | `640` | 出力幅（px）、上限 1920 |
| `height` | `360` | 出力高さ（px）、上限 1080 |
| `format` | `jpeg` | `png` または `jpeg` |
| `quality` | `85` | JPEG 品質（1–100、`format=jpeg` のとき有効） |

### レスポンス

```json
{
  "cameraPath": "Main Camera",
  "width": 640,
  "height": 360,
  "format": "jpeg",
  "mimeType": "image/jpeg",
  "data": "<base64エンコード済み画像データ>"
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `path` が未指定 |
| 404 | 指定パスに Camera コンポーネントが存在しない |

### 使用例

```bash
# カメラ一覧でパスを確認
curl "http://localhost:8765/api/cameras"

# Main Camera をデフォルト解像度で JPEG キャプチャ
curl "http://localhost:8765/api/cameras/capture?path=Main+Camera"

# PNG で HD キャプチャ
curl "http://localhost:8765/api/cameras/capture?path=Main+Camera&width=1280&height=720&format=png"
```

### LLM / MCP ブリッジでの利用

レスポンスの `mimeType` と `data` フィールドをそのまま MCP の image content ブロックに変換できます。

---

## GET /api/cameras/capture/image

`/api/cameras/capture` と同じパラメーターで、バイナリ画像を直接返します。  
ブラウザで開けばそのまま表示され、`curl -o` でファイル保存できます。

### クエリパラメーター

`/api/cameras/capture` と同一（`path` 必須、`width` / `height` / `format` / `quality` 任意）。

### レスポンス

`Content-Type: image/jpeg`（または `image/png`）のバイナリストリーム。JSON ラッパーなし。

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `path` が未指定 |
| 404 | 指定パスに Camera コンポーネントが存在しない |

### 使用例

```bash
# ブラウザで開いてそのまま確認
open "http://localhost:8765/api/cameras/capture/image?path=Main+Camera"

# curl でファイル保存
curl -o screenshot.png \
  "http://localhost:8765/api/cameras/capture/image?path=Main+Camera&format=png"

# HD JPEG で保存
curl -o hd.jpg \
  "http://localhost:8765/api/cameras/capture/image?path=Main+Camera&width=1280&height=720&quality=90"
```

---

## GET /api/scene

現在開いているシーンのメタ情報を返します。

### レスポンス

```json
{
  "name": "SampleScene",
  "path": "Assets/Scenes/SampleScene.unity",
  "isDirty": false,
  "isLoaded": true,
  "rootCount": 4
}
```

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `name` | string | シーン名 |
| `path` | string | Assets/ 以下のパス |
| `isDirty` | bool | 未保存の変更があるか |
| `isLoaded` | bool | シーンがロード済みか |
| `rootCount` | int | ルート GameObject の数 |

---

## GET /api/scene/hierarchy

シーン全体の GameObject ツリーを返します。

### レスポンス

```json
{
  "scene": "SampleScene",
  "objects": [ <GameObjectNode>, ... ]
}
```

#### GameObjectNode

```json
{
  "name": "Canvas",
  "path": "Canvas",
  "isActive": true,
  "tag": "Untagged",
  "layer": 5,
  "transform": {
    "position": { "x": 0, "y": 0, "z": 0 },
    "rotation": { "x": 0, "y": 0, "z": 0 },
    "scale":    { "x": 1, "y": 1, "z": 1 }
  },
  "children": [
    {
      "name": "Panel",
      "path": "Canvas/Panel",
      ...
    }
  ]
}
```

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `name` | string | GameObject 名 |
| `path` | string | ルートからの `/` 区切りパス |
| `isActive` | bool | `activeInHierarchy`（親も含む） |
| `tag` | string | タグ |
| `layer` | int | レイヤー番号 |
| `transform` | object | ローカル座標系の position / rotation (EulerAngles) / scale |
| `children` | array | 子 GameObjectNode の配列（再帰） |

---

## GET /api/gameobjects

指定パスの GameObject の詳細情報（コンポーネント含む）を返します。

### クエリパラメーター

| パラメーター | 必須 | 説明 |
|-------------|------|------|
| `path` | ✅ | ルートからの `/` 区切りパス（例: `Canvas/Panel/Button`） |

### レスポンス

```json
{
  "name": "Button",
  "path": "Canvas/Panel/Button",
  "isActive": true,
  "tag": "Untagged",
  "layer": 5,
  "transform": { ... },
  "components": [
    {
      "type": "UnityEngine.RectTransform",
      "properties": {
        "m_LocalPosition": { "x": 0, "y": 0, "z": 0 },
        "m_LocalScale":    { "x": 1, "y": 1, "z": 1 }
      }
    },
    {
      "type": "UnityEngine.UI.Button",
      "properties": {
        "m_Interactable": true,
        "m_Transition": 1
      }
    }
  ]
}
```

`components[].properties` は `SerializedObject` 経由で取得したプロパティです。  
対応する `SerializedPropertyType`: `bool`, `int`, `float`, `string`, `Color`, `Vector2/3/4`, `Rect`, `ObjectReference`。それ以外は `null`。

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `path` パラメーターがない |
| 404 | 指定パスに GameObject が存在しない |

---

## GET /api/assets

プロジェクト内のアセット一覧を返します。

### クエリパラメーター

| パラメーター | 必須 | 説明 |
|-------------|------|------|
| `path` | ❌ | 検索対象フォルダ（例: `Assets/UI`）。省略時は `Assets/` 全体 |
| `type` | ❌ | アセットタイプ名（例: `Texture2D`, `Material`, `Scene`） |
| `search` | ❌ | `AssetDatabase.FindAssets` に渡す追加フィルター文字列 |

### レスポンス

```json
{
  "assets": [
    {
      "guid": "a1b2c3d4e5f6...",
      "path": "Assets/UI/logo.png",
      "type": "Texture2D"
    }
  ],
  "total": 42,
  "returned": 42
}
```

> 最大 **500 件**を返します。`total` が 500 を超える場合はフィルターを絞ってください。

---

## GET /api/assets/{guid}

GUID を指定してアセットの詳細情報を返します。

### パスパラメーター

| パラメーター | 説明 |
|-------------|------|
| `guid` | `AssetDatabase` の GUID 文字列 |

### レスポンス

```json
{
  "guid": "a1b2c3d4e5f6...",
  "path": "Assets/UI/logo.png",
  "type": "UnityEngine.Texture2D",
  "dependencies": [
    "Assets/UI/logo.png"
  ],
  "labels": ["UI", "Icon"]
}
```

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `guid` | string | アセットの GUID |
| `path` | string | Assets/ 以下のパス |
| `type` | string | 完全修飾型名 |
| `dependencies` | string[] | 直接依存するアセットのパス（`GetDependencies(recursive: false)`） |
| `labels` | string[] | アセットラベル |

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | GUID が空 |
| 404 | 該当アセットが存在しない |

---

## GET /api/search/gameobjects

シーン内の GameObject を複数条件で AND 検索します。全パラメーターはオプションです。

### クエリパラメーター

| パラメーター | 型 | 説明 |
|-------------|-----|------|
| `name` | string | 名前の部分一致（大文字小文字無視） |
| `component` | string | コンポーネント型名の部分一致（例: `Camera`, `MeshRenderer`） |
| `tag` | string | タグの完全一致 |
| `layer` | int | レイヤー番号 |
| `active` | bool | `true`/`false`（省略 = どちらも） |
| `assetGuid` | string | 指定 GUID のアセットをいずれかのコンポーネントで参照している |
| `includeComponents` | bool | `true` のとき各 GameObject のコンポーネント型名一覧を含める（デフォルト: `false`） |

### レスポンス

```json
{
  "count": 2,
  "gameObjects": [
    {
      "name": "Main Camera",
      "path": "Main Camera",
      "isActive": true,
      "tag": "MainCamera",
      "layer": 0,
      "transform": { "position": {...}, "rotation": {...}, "scale": {...} },
      "components": [
        { "type": "UnityEngine.Camera" },
        { "type": "UnityEngine.AudioListener" }
      ]
    }
  ]
}
```

> `components` フィールドは `includeComponents=true` を指定した場合のみ含まれます。

### 使用例

```bash
# 名前に "Enemy" を含む GameObject
curl "http://localhost:8765/api/search/gameobjects?name=Enemy"

# Camera コンポーネントを持つもの（コンポーネント一覧付き）
curl "http://localhost:8765/api/search/gameobjects?component=Camera&includeComponents=true"

# 特定アセットを参照している + 非アクティブ
curl "http://localhost:8765/api/search/gameobjects?assetGuid=abc123&active=false"
```

---

## GET /api/search/asset-refs

シーン内のコンポーネントが特定アセットを参照している場所を列挙します。

### クエリパラメーター

| パラメーター | 必須 | 説明 |
|-------------|------|------|
| `guid` | ✅ | 検索対象アセットの GUID |

### レスポンス

```json
{
  "asset": {
    "guid": "a1b2c3...",
    "path": "Assets/Materials/PlayerMat.mat",
    "type": "Material"
  },
  "references": [
    {
      "gameObjectPath": "Player/Body",
      "componentType": "UnityEngine.MeshRenderer",
      "propertyName": "m_Materials"
    }
  ],
  "count": 1
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `guid` が未指定 |
| 404 | 該当アセットが存在しない |

> **注意**: シーン全 GameObject の全コンポーネントを SerializedObject で走査します。大規模シーンでは処理に時間がかかる場合があります。

---

## GET /api/assets/dependents

指定アセットを依存先として持つアセット（逆依存）を返します。

### クエリパラメーター

| パラメーター | 必須 | 説明 |
|-------------|------|------|
| `guid` | ✅ | 被依存アセットの GUID |

### レスポンス

```json
{
  "asset": {
    "guid": "a1b2c3...",
    "path": "Assets/Materials/PlayerMat.mat",
    "type": "Material"
  },
  "dependents": [
    {
      "guid": "d4e5f6...",
      "path": "Assets/Prefabs/Player.prefab",
      "type": "GameObject"
    }
  ],
  "count": 1
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `guid` が未指定 |
| 404 | 該当アセットが存在しない |

> **注意**: `Assets/` 内の全アセットに対して `GetDependencies()` を呼び出します。アセット数が多い場合は処理に時間がかかります。

---

## GET /api/scene/stats

現在のシーンの集計統計情報を返します。

### レスポンス

```json
{
  "scene": "SampleScene",
  "totalGameObjects": 42,
  "activeGameObjects": 38,
  "inactiveGameObjects": 4,
  "componentCounts": {
    "Camera": 1,
    "MeshRenderer": 15,
    "Light": 3,
    "Rigidbody": 8
  },
  "tagCounts": {
    "Untagged": 30,
    "Player": 1,
    "Enemy": 8
  },
  "layerCounts": {
    "Default": 35,
    "UI": 7
  }
}
```

> `Transform` / `RectTransform` はノイズになるため `componentCounts` から除外されています。  
> `layerCounts` のキーはレイヤー名（未設定のレイヤーは番号）です。

---

## Write API — 共通事項

> **セキュリティ:** Write 系エンドポイントはデフォルトで**無効**です。  
> **Window > UnionAir > REST Bridge** の各トグルで有効化してください。  
> すべての書き込み操作は Unity Editor の Undo（Ctrl+Z）で元に戻せます。

---

## POST /api/gameobjects

新しい空の GameObject をシーンに作成します。

### リクエスト Body (JSON)

```json
{
  "name": "MyObject",
  "parentPath": "Canvas"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `name` | ✅ | 作成する GameObject の名前 |
| `parentPath` | ❌ | 親 GameObject のパス。省略するとシーンルートに配置 |

### レスポンス

```json
{
  "path": "Canvas/MyObject",
  "name": "MyObject"
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `name` が未指定 |
| 404 | `parentPath` が存在しない |
| 403 | Write API が無効 |

---

## POST /api/gameobjects/primitive

プリミティブ型の GameObject を作成します。

### リクエスト Body (JSON)

```json
{
  "type": "Cube",
  "name": "MyCube",
  "parentPath": "Stage"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `type` | ✅ | `Cube` \| `Sphere` \| `Capsule` \| `Cylinder` \| `Plane` \| `Quad` |
| `name` | ❌ | 省略時はタイプ名がそのまま使用される |
| `parentPath` | ❌ | 親 GameObject のパス。省略するとシーンルート |

### レスポンス

```json
{
  "path": "Stage/MyCube",
  "name": "MyCube"
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `type` が未指定または不正 |
| 403 | Write API が無効 |

---

## DELETE /api/gameobjects

指定パスの GameObject をシーンから削除します。

### クエリパラメーター

| パラメーター | 必須 | 説明 |
|-------------|------|------|
| `path` | ✅ | 削除する GameObject のパス |

### レスポンス

```json
{ "deleted": "Canvas/MyObject" }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `path` が未指定 |
| 404 | 指定パスが存在しない |
| 403 | Write API が無効 |

---

## PATCH /api/gameobjects

指定パスの GameObject のプロパティを更新します。

### クエリパラメーター

| パラメーター | 必須 | 説明 |
|-------------|------|------|
| `path` | ✅ | 対象 GameObject のパス |

### リクエスト Body (JSON)

```json
{
  "name": "RenamedObject",
  "isActive": false,
  "tag": "Player",
  "layer": 3,
  "transform": {
    "position": { "x": 1, "y": 0, "z": 0 },
    "rotation": { "x": 0, "y": 45, "z": 0 },
    "scale":    { "x": 1, "y": 1,  "z": 1 }
  }
}
```

すべてのフィールドはオプションです。省略したフィールドは変更されません。`transform` の各サブフィールドも同様にオプションです。

### レスポンス

```json
{ "path": "Canvas/RenamedObject", "name": "RenamedObject" }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `path` が未指定 |
| 404 | 指定パスが存在しない |
| 403 | Write API が無効 |

---

## POST /api/gameobjects/duplicate

指定パスの GameObject を複製します。

### クエリパラメーター

| パラメーター | 必須 | 説明 |
|-------------|------|------|
| `path` | ✅ | 複製元 GameObject のパス |

### レスポンス

```json
{ "path": "Canvas/MyObject (1)", "name": "MyObject (1)" }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `path` が未指定 |
| 404 | 指定パスが存在しない |
| 403 | Write API が無効 |

---

## POST /api/gameobjects/reparent

GameObject を別の親に移動します。

### リクエスト Body (JSON)

```json
{
  "path": "Canvas/Panel/Button",
  "parentPath": "Canvas/NewPanel"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `path` | ✅ | 移動する GameObject のパス |
| `parentPath` | ❌ | 新しい親のパス。省略するとシーンルートへ移動 |

### レスポンス

```json
{ "path": "Canvas/NewPanel/Button" }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `path` が未指定 |
| 404 | `path` または `parentPath` が存在しない |
| 403 | Write API が無効 |

---

## POST /api/gameobjects/batch

複数の create / update / delete 操作を**1 つの Undo グループ**としてまとめて実行します。

### リクエスト Body (JSON)

```json
{
  "operations": [
    { "op": "create", "name": "EmptyGO", "parentPath": "Stage" },
    { "op": "create_primitive", "type": "Cube", "name": "Cube_0", "transform": { "position": { "x": 0, "y": 0, "z": 0 } } },
    { "op": "update", "path": "Stage/OldObject", "isActive": false },
    { "op": "delete", "path": "Stage/Trash" }
  ]
}
```

#### `op` の種類

| `op` | 必須フィールド | オプションフィールド |
|------|--------------|------------------|
| `create` | `name` | `parentPath`, `transform` |
| `create_primitive` | `type` (`Cube`\|`Sphere`\|`Capsule`\|`Cylinder`\|`Plane`\|`Quad`) | `name`, `parentPath`, `transform` |
| `update` | `path` | `name`, `isActive`, `tag`, `layer`, `transform` |
| `delete` | `path` | — |

`transform` shape: `{"position":{"x":0,"y":0,"z":0},"rotation":{...},"scale":{...}}`

### レスポンス (HTTP 207)

```json
{
  "processed": 4,
  "failed": 1,
  "results": [
    { "index": 0, "success": true,  "path": "Stage/EmptyGO" },
    { "index": 1, "success": true,  "path": "Cube_0" },
    { "index": 2, "success": true,  "path": "Stage/OldObject" },
    { "index": 3, "success": false, "error": "GameObject not found: Stage/Trash" }
  ]
}
```

1 つの操作が失敗しても残りの操作は継続されます。すべての成功操作は単一の Undo グループにまとめられます。

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `operations` が未指定または空 |
| 403 | Write API が無効 |

---

## POST /api/gameobjects/components

指定 GameObject にコンポーネントを追加します。

### リクエスト Body (JSON)

```json
{
  "path": "Canvas/MyObject",
  "type": "UnityEngine.BoxCollider"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `path` | ✅ | 対象 GameObject のパス |
| `type` | ✅ | 追加するコンポーネントの完全修飾型名 |

### レスポンス

```json
{ "path": "Canvas/MyObject", "component": "UnityEngine.BoxCollider" }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `path` または `type` が未指定 |
| 404 | 指定パスが存在しない |
| 422 | 型名が解決できない、またはコンポーネントの追加に失敗 |
| 403 | Write API が無効 |

---

## DELETE /api/gameobjects/components

指定 GameObject からコンポーネントを削除します。

### クエリパラメーター

| パラメーター | 必須 | 説明 |
|-------------|------|------|
| `path` | ✅ | 対象 GameObject のパス |
| `type` | ✅ | 削除するコンポーネントの完全修飾型名 |

### レスポンス

```json
{ "path": "Canvas/MyObject", "removed": "UnityEngine.BoxCollider" }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `path` または `type` が未指定 |
| 404 | 指定パスが存在しない、またはコンポーネントが存在しない |
| 403 | Write API が無効 |

---

## PATCH /api/gameobjects/components

指定コンポーネントのシリアライズ済みプロパティを更新します。

### クエリパラメーター

| パラメーター | 必須 | 説明 |
|-------------|------|------|
| `path` | ✅ | 対象 GameObject のパス |
| `type` | ✅ | 対象コンポーネントの完全修飾型名 |

### リクエスト Body (JSON)

```json
{
  "properties": {
    "m_Intensity": 2.0,
    "m_Color": { "r": 1, "g": 0.9, "b": 0.8, "a": 1 }
  }
}
```

`properties` の各キーは `SerializedProperty` のプロパティパス（`SerializedObject` のフィールド名）です。

### レスポンス

```json
{ "path": "Directional Light", "component": "UnityEngine.Light", "updated": ["m_Intensity", "m_Color"] }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `path` / `type` / `properties` が未指定 |
| 404 | GameObject またはコンポーネントが存在しない |
| 403 | Write API が無効 |

---

## POST /api/scene/save

現在のシーンをディスクに保存します。

> Asset Write API が有効の場合のみ呼び出せます。

### レスポンス

```json
{ "saved": true, "path": "Assets/Scenes/SampleScene.unity" }
```

---

## POST /api/editor/refresh

`AssetDatabase.Refresh()` を呼び出し、スクリプトや素材の変更を Unity に認識させます。

> Asset Write API が有効の場合のみ呼び出せます。

### レスポンス

```json
{
  "refreshed": true,
  "isCompiling": true,
  "isUpdating": false,
  "isPlaying": false
}
```

> 新しいスクリプトコンポーネントをアタッチする前に `GET /api/editor/status` をポーリングし、`isCompiling: false` になるまで待機してください。

---

## POST /api/assets/prefabs

シーン内の GameObject からプレハブを作成します。

> Asset Write API が有効の場合のみ呼び出せます。

### リクエスト Body (JSON)

```json
{
  "goPath": "Stage/Player",
  "assetPath": "Assets/Prefabs/Player.prefab",
  "mode": "new"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `goPath` | ✅ | ソース GameObject のパス |
| `assetPath` | ✅ | 保存先のアセットパス（`Assets/` から始まる `.prefab` ファイル） |
| `mode` | ✅ | `new`（インスタンスを接続して作成）または `replace`（既存プレハブを上書き） |

### レスポンス

```json
{ "assetPath": "Assets/Prefabs/Player.prefab", "guid": "a1b2c3..." }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | 必須フィールドが不足、または `mode` が不正 |
| 404 | `goPath` が存在しない |
| 403 | Asset Write API が無効 |

---

## POST /api/assets/prefabs/apply

プレハブインスタンスのオーバーライドをプレハブアセットに適用します。

> Asset Write API が有効の場合のみ呼び出せます。

### リクエスト Body (JSON)

```json
{ "goPath": "Stage/Player" }
```

### レスポンス

```json
{ "applied": true, "goPath": "Stage/Player" }
```

---

## POST /api/assets/prefabs/revert

プレハブインスタンスをプレハブアセットの状態に戻します。

> Asset Write API が有効の場合のみ呼び出せます。

### リクエスト Body (JSON)

```json
{ "goPath": "Stage/Player" }
```

### レスポンス

```json
{ "reverted": true, "goPath": "Stage/Player" }
```

---

## POST /api/assets/materials

新しいマテリアルアセットを作成します。

> Asset Write API が有効の場合のみ呼び出せます。

### リクエスト Body (JSON)

```json
{
  "assetPath": "Assets/Materials/MyMat.mat",
  "shader": "Universal Render Pipeline/Lit"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `assetPath` | ✅ | 保存先（`Assets/` から始まる `.mat` ファイル） |
| `shader` | ✅ | シェーダー名 |

### レスポンス

```json
{ "guid": "d4e5f6...", "assetPath": "Assets/Materials/MyMat.mat" }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | 必須フィールドが不足 |
| 422 | シェーダーが見つからない |
| 403 | Asset Write API が無効 |

---

## PATCH /api/assets/materials

マテリアルのプロパティを更新します。

> Asset Write API が有効の場合のみ呼び出せます。

### クエリパラメーター

| パラメーター | 必須 | 説明 |
|-------------|------|------|
| `guid` | ✅ | 対象マテリアルの GUID |

### リクエスト Body (JSON)

```json
{
  "properties": {
    "_BaseColor": { "r": 1, "g": 0, "b": 0, "a": 1 },
    "_Metallic": 0.5,
    "_BumpMap": "b1c2d3..."
  }
}
```

`properties` の値の型:

| 型 | 形式 |
|----|------|
| Color | `{"r":float,"g":float,"b":float,"a":float}` |
| Float | `float` |
| Vector | `{"x":float,"y":float,"z":float,"w":float}` |
| Texture | GUID 文字列 |

### レスポンス

```json
{ "guid": "d4e5f6...", "updated": ["_BaseColor", "_Metallic"] }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `guid` が未指定 |
| 404 | 該当マテリアルが存在しない |
| 403 | Asset Write API が無効 |

---

## DELETE /api/assets/{guid}

アセットとその `.meta` ファイルを削除します。

> Asset Write API が有効の場合のみ呼び出せます。

### パスパラメーター

| パラメーター | 説明 |
|-------------|------|
| `guid` | 削除するアセットの GUID |

### レスポンス

```json
{ "deleted": "Assets/Textures/old_icon.png" }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | GUID が空 |
| 404 | 該当アセットが存在しない |
| 403 | Asset Write API が無効 |

---

## POST /api/assets/move

アセットを移動/リネームします。GUID およびプロジェクト内の参照は保持されます。

> Asset Write API が有効の場合のみ呼び出せます。

### リクエスト Body (JSON)

```json
{
  "guid": "a1b2c3...",
  "newPath": "Assets/Textures/Renamed/icon.png"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `guid` | ✅ | 移動するアセットの GUID |
| `newPath` | ✅ | 移動先のパス（`Assets/` から始まる） |

### レスポンス

```json
{ "guid": "a1b2c3...", "newPath": "Assets/Textures/Renamed/icon.png" }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `guid` または `newPath` が未指定 |
| 404 | 該当アセットが存在しない |
| 422 | 移動操作が失敗（パスの重複など） |
| 403 | Asset Write API が無効 |

---

## POST /api/editor/play

プレイモードに入ります（`EditorApplication.isPlaying = true`）。

> Play Mode API が有効の場合のみ呼び出せます。  
> ドメインリロードが発生する場合、HTTP サーバーは一時的に再起動します。`GET /api/editor/status` をポーリングして `isPlaying: true` になるまで待機してください。

### レスポンス

```json
{ "requested": true, "action": "play" }
```

---

## POST /api/editor/stop

プレイモードを終了します（`EditorApplication.isPlaying = false`）。

> Play Mode API が有効の場合のみ呼び出せます。

### レスポンス

```json
{ "requested": true, "action": "stop" }
```

---

## POST /api/editor/pause

一時停止状態を設定します。Body 省略時は現在の状態をトグルします。

> Play Mode API が有効の場合のみ呼び出せます。

### リクエスト Body (JSON、オプション)

```json
{ "paused": true }
```

### レスポンス

```json
{ "isPaused": true }
```

---

## POST /api/editor/step

1 フレーム進めます。`isPaused: true` のときのみ有効です。

> Play Mode API が有効の場合のみ呼び出せます。

### レスポンス

```json
{ "stepped": true }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | プレイモードでない、または一時停止していない |
| 403 | Play Mode API が無効 |
