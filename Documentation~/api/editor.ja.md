# API リファレンス — Editor

[English](editor.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](editor.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`(デフォルトポート: **8765**)。レスポンスの規約とカテゴリ/セキュリティの注意事項は [API リファレンス索引](../api-reference.ja.md) を参照してください。

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
| `isPlaying` | bool | Play モード中かどうか(`EditorApplication.isPlaying`) |
| `isPaused` | bool | Play モードで一時停止中かどうか(`EditorApplication.isPaused`) |
| `isCompiling` | bool | スクリプトのコンパイル中かどうか(`EditorApplication.isCompiling`) |
| `isUpdating` | bool | アセット更新処理中かどうか(`EditorApplication.isUpdating`) |
| `unityVersion` | string | Unity バージョン文字列 |

---

## GET /api/editor/logs

Unity Console のログを返します。エディタ起動以降(または直近の domain reload 以降)に記録されたログを含みます。リングバッファに最大1000件保持されます。

### クエリパラメータ

| パラメータ | 既定値 | 説明 |
|-------------|-----------|------|
| `type` | `all` | 大文字小文字を区別しない `log` / `warning` / `error` / `exception` / `assert` / `all` |
| `search` | ―  | メッセージに対する大文字小文字を区別しない部分一致フィルタ |
| `limit` | `100` | 返す結果の最大数(最大: 1000) |

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

> ログは新しい順(`timestamp` 降順)で返されます。
> domain reload の前に `StopCapturing()` が呼ばれるため、ログはリロードをまたいで保持されません。

不明な `type` 値はフィルターを暗黙に無効化せず、`400 Bad Request` を返します。

### 例

```bash
# 最新のエラー・例外 20 件
curl "http://localhost:8765/api/editor/logs?type=error&limit=20"

# "NullReference" を含むログ
curl "http://localhost:8765/api/editor/logs?search=NullReference"
```

---

## GET /api/editor/selection

現在の Unity Editor の選択状態を返します。

> Editor Actions カテゴリが有効な場合のみ呼び出せます。
> エンドポイントのリスクは `editorState` です。

### レスポンス

```json
{
  "count": 1,
  "activeIndex": 0,
  "active": {
    "kind": "sceneObject",
    "name": "Main Camera",
    "type": "UnityEngine.GameObject",
    "globalObjectId": "GlobalObjectId_V1-...",
    "scenePath": "Assets/Scenes/SampleScene.unity"
  },
  "objects": [
    {
      "kind": "sceneObject",
      "name": "Main Camera",
      "type": "UnityEngine.GameObject",
      "globalObjectId": "GlobalObjectId_V1-...",
      "scenePath": "Assets/Scenes/SampleScene.unity"
    }
  ],
  "assetGuids": []
}
```

| フィールド | 説明 |
|-------|-------------|
| `kind` | `sceneObject`、`asset`、または `unknown` |
| `globalObjectId` | シーンの GameObject と Component に存在 |
| `scenePath` | シーンオブジェクトのロード済みシーンパス |
| `assetGuid` / `assetPath` | プロジェクトアセットに存在 |
| `entityId` | サポート外の Editor オブジェクト種別に対するフォールバック(Unity が Editor オブジェクトのエンティティ ID を公開する場合) |

---

## POST /api/editor/selection

Unity Editor の選択状態を設定またはクリアします。

> Editor Actions カテゴリが有効な場合のみ呼び出せます。
> エンドポイントのリスクは `editorState` です。

### リクエストボディ(JSON)

単一ターゲットの設定:

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/Button" }
}
```

複数ターゲットの設定:

```json
{
  "targets": [
    { "type": "hierarchyPath", "value": "Canvas/Button", "scenePath": "Assets/Scenes/Main.unity" },
    { "assetPath": "Assets/Textures/Icon.png" }
  ],
  "activeIndex": 0
}
```

選択のクリア:

```json
{ "clear": true }
```

ターゲットには、シーンオブジェクト参照フィールド(`type`、`value`、任意の `scenePath`)またはアセット参照フィールド(`assetGuid`、`assetPath`、任意の `assetType`)のいずれかを指定します。1つのターゲットオブジェクト内でシーン参照とアセット参照のフィールドを混在させないでください。

### レスポンス

`GET /api/editor/selection` と同じ形式です。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | ターゲットフィールドの欠落、不正なターゲット、シーン/アセットフィールドの混在、または不正な `activeIndex` |
| 403 | Editor Actions カテゴリが無効 |
| 404 | シーンオブジェクトまたはアセットが見つからない |
| 409 | シーン名が曖昧 |
| 422 | ターゲットがサポート外のオブジェクト種別に解決される |

---

## POST /api/editor/ping

現在の選択状態を変更せずに、`EditorGUIUtility.PingObject()` で Unity Editor のオブジェクトをハイライトします。

> Editor Actions カテゴリが有効な場合のみ呼び出せます。
> エンドポイントのリスクは `editorState` です。

### リクエストボディ(JSON)

```json
{
  "target": { "assetGuid": "a1b2c3..." }
}
```

`target` は `POST /api/editor/selection` の単一ターゲットと同じ形式を受け付けます。

### レスポンス

```json
{
  "pinged": true,
  "target": {
    "kind": "asset",
    "name": "Icon",
    "type": "UnityEngine.Texture2D",
    "assetGuid": "a1b2c3...",
    "assetPath": "Assets/Textures/Icon.png"
  }
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `target` の欠落または不正な形式 |
| 403 | Editor Actions カテゴリが無効 |
| 404 | ターゲットのオブジェクトまたはアセットが見つからない |
| 409 | シーン名が曖昧 |
| 422 | ターゲットがサポート外のオブジェクト種別に解決される |

---

## GET /api/cameras

シーン内のすべての Camera コンポーネントの一覧を返します。

### レスポンス

```json
{
  "count": 1,
  "cameras": [
    {
      "path": "Main Camera",
      "globalObjectId": "GlobalObjectId_V1-...",
      "componentGlobalObjectId": "GlobalObjectId_V1-...",
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
| `path` | string | GameObject の階層パス(`/api/cameras/capture` の `path` パラメータで使用) |
| `globalObjectId` | string | カメラ GameObject の GlobalObjectId |
| `componentGlobalObjectId` | string | Camera コンポーネントの GlobalObjectId |
| `depth` | float | 描画順(値が大きいほど後に描画) |
| `fieldOfView` | float | 垂直視野角(`isOrthographic: true` の場合は意味を持ちません) |

---

## GET /api/cameras/capture

指定したカメラで `camera.Render()` を実行し、結果を base64 エンコードした画像として返します。
Edit モードと Play モードの両方で動作します。

### クエリパラメータ

| パラメータ | 既定値 | 説明 |
|-------------|-----------|------|
| `target` | **必須** | カメラの GameObject または Camera コンポーネントに解決されるオブジェクト参照 |
| `scenePath` | アクティブシーン | パスベースのターゲット解決に使う、ロード済みシーンのアセットパスまたは一意に定まるシーン名 |
| `width` | `640` | 出力幅(px)、最大 1920 |
| `height` | `360` | 出力高さ(px)、最大 1080 |
| `format` | `jpeg` | `png` または `jpeg` |
| `quality` | `85` | JPEG 品質(1–100、`format=jpeg` の場合に有効) |

### レスポンス

```json
{
  "cameraPath": "Main Camera",
  "globalObjectId": "GlobalObjectId_V1-...",
  "componentGlobalObjectId": "GlobalObjectId_V1-...",
  "width": 640,
  "height": 360,
  "format": "jpeg",
  "mimeType": "image/jpeg",
  "image": "<base64-encoded image data>"
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `target` の欠落または不正な形式 |
| 404 | `target` に対応する Camera コンポーネントが存在しない |
| 422 | `target` がサポート外のオブジェクト型に解決される |

### 例

```bash
# カメラ一覧からパスを確認
curl "http://localhost:8765/api/cameras"

# Main Camera をデフォルト解像度の JPEG でキャプチャ
curl --get "http://localhost:8765/api/cameras/capture" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}'

# HD 解像度の PNG でキャプチャ
curl --get "http://localhost:8765/api/cameras/capture" \
  --data-urlencode 'target={"type":"componentPath","value":"Main Camera:UnityEngine.Camera"}' \
  --data-urlencode "width=1280" \
  --data-urlencode "height=720" \
  --data-urlencode "format=png"
```

### LLM / MCP ブリッジでの利用

レスポンスの `mimeType` と `image` フィールドは、そのまま MCP の画像コンテンツブロックに変換できます。

---

## GET /api/cameras/capture/image

`/api/cameras/capture` と同じパラメータで、バイナリ画像を直接返します。
ブラウザで開けばそのまま表示され、`curl -o` でファイルに保存できます。

### クエリパラメータ

`/api/cameras/capture` と同じ(`target` 必須、`scenePath` / `width` / `height` / `format` / `quality` は任意)。

### レスポンス

`Content-Type: image/jpeg`(または `image/png`)のバイナリストリーム。JSON ラッパーはありません。

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `target` の欠落または不正な形式 |
| 404 | `target` に対応する Camera コンポーネントが存在しない |
| 422 | `target` がサポート外のオブジェクト型に解決される |

### 例

```bash
# ブラウザで直接表示(target は URL エンコード)
open "http://localhost:8765/api/cameras/capture/image?target=%7B%22type%22%3A%22hierarchyPath%22%2C%22value%22%3A%22Main%20Camera%22%7D"

# curl でファイルに保存
curl --get -o screenshot.png "http://localhost:8765/api/cameras/capture/image" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}' \
  --data-urlencode "format=png"

# HD JPEG で保存
curl --get -o hd.jpg "http://localhost:8765/api/cameras/capture/image" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}' \
  --data-urlencode "width=1280" --data-urlencode "height=720" --data-urlencode "quality=90"
```

---

## POST /api/editor/refresh

`AssetDatabase.Refresh()` を呼び出し、スクリプトやアセットの変更を Unity に認識させます。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### レスポンス

```json
{
  "refreshed": true,
  "isCompiling": true,
  "isUpdating": false,
  "isPlaying": false
}
```

> 新しく書き込んだアセットを使用したり、新しいスクリプトコンポーネントをアタッチしたりする前に、`GET /api/editor/status` をポーリングし、`isUpdating: false` と `isCompiling: false` の両方を待ってください。スクリプト変更により domain reload 中にサーバーが再起動する場合があります。接続失敗はバックオフ付きで再試行し、サーバー復帰後にアイドル状態をもう一度確認してください。

---

## GET /api/editor/menu-items

`POST /api/editor/menu-item` で使用できる、現在検出可能な Unity Editor メニュー項目のパスを一覧します。

> Editor Actions カテゴリが有効な場合のみ呼び出せます。
> エンドポイントのリスクは `editorState` です。
> Unity はメニューの完全な列挙のための安定した公開 API を提供していません。このエンドポイントは、Unity 内部のメニュー API を使用したか、`[MenuItem]` 属性のフォールバックスキャンを使用したかを報告します。

### クエリパラメータ

| パラメータ | 既定値 | 説明 |
|-----------|---------|-------------|
| `root` | ― | `Window`、`Assets`、`GameObject` などのメニュールート(任意) |
| `search` | ― | メニュー項目パスに対する大文字小文字を区別しない部分一致 |
| `includeFolders` | `true` | 内部メニュー列挙が利用可能な場合、メニューフォルダのエントリを含めます |
| `includeAttributeFallback` | `true` | 内部メニュー列挙の結果に `[MenuItem]` 属性のパスを追加します |
| `limit` | `1000` | 返す項目の最大数(1〜5000 に制限) |

### レスポンス

```json
{
  "enumerationMode": "unsupportedApi",
  "isComplete": true,
  "root": "Window",
  "count": 1,
  "items": [
    {
      "path": "Window/UnionAir/REST Bridge",
      "name": "REST Bridge",
      "parent": "Window/UnionAir",
      "depth": 2,
      "isFolder": false,
      "source": "unityMenu"
    }
  ],
  "warnings": []
}
```

Unity 内部のメニュー列挙メソッドが利用できない場合、エンドポイントはロード済みアセンブリの `[MenuItem]` 属性のスキャンにフォールバックします:

```json
{
  "enumerationMode": "menuItemAttributes",
  "isComplete": false,
  "root": "",
  "count": 1,
  "items": [
    {
      "path": "Window/UnionAir/REST Bridge",
      "name": "REST Bridge",
      "parent": "Window/UnionAir",
      "depth": 2,
      "isFolder": false,
      "source": "menuItemAttribute"
    }
  ],
  "warnings": [
    "UnityEditor.Unsupported.GetSubmenus was not available; built-in Unity menu items may be incomplete."
  ]
}
```

| フィールド | 説明 |
|-------|-------------|
| `enumerationMode` | Unity 内部のメニュー列挙を使用した場合は `unsupportedApi`、それ以外は `menuItemAttributes` |
| `isComplete` | Unity 組み込みメニューを完全にカバーできていると見込まれるかどうか |
| `items[].path` | `POST /api/editor/menu-item` に渡すためのメニューパス |
| `items[].isFolder` | 実行可能な項目ではなくメニューフォルダかどうか |
| `items[].source` | `unityMenu` または `menuItemAttribute` |

### 例

```bash
curl "http://localhost:8765/api/editor/menu-items?search=UnionAir"
curl "http://localhost:8765/api/editor/menu-items?root=Window&includeFolders=false"
```

---

## POST /api/editor/menu-item

`EditorApplication.ExecuteMenuItem()` を使って Unity Editor のメニュー項目を実行します。

> Editor Actions カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。
> 副作用が要求されたメニュー項目パスに依存するため、リスクは `requestDependent` として報告されます。

### リクエストボディ(JSON)

```json
{
  "path": "Window/UnionAir/REST Bridge"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `path` | ✅ | `Window/UnionAir/REST Bridge` のような Unity Editor メニュー項目パス |

### レスポンス

```json
{
  "executed": true,
  "path": "Window/UnionAir/REST Bridge"
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `path` の欠落または空 |
| 403 | Editor Actions カテゴリが無効 |
| 404 | メニュー項目が見つからない、無効化されている、または実行できなかった |
| 409 | Unity Editor が Play モード中 |

### 例

```bash
curl -X POST http://localhost:8765/api/editor/menu-item \
  -H "Content-Type: application/json" \
  -d '{"path":"Window/UnionAir/REST Bridge"}'
```

---

## GET /api/editor/capture

現在のビューをキャプチャし、base64 エンコードした画像を返します。

- **Play モード**: Unity 内部のリフレクション経由で `GameView.m_RenderTexture` を読み取ります — Screen Space Overlay の Canvas UI を含む、合成済みの現在の GameView フレームです。リフレクションが利用できない場合は `ScreenCapture.CaptureScreenshotAsTexture()` にフォールバックします。`width` と `height` はキャプチャした出力画像をリサイズするものであり、GameView・Canvas・ビューポートのリサイズや、その解像度での再レンダリングは行いません。
- **Edit モード**: 最後にアクティブだった Scene View カメラを `camera.Render()` でレンダリングします。

`target` パラメータは不要です。エンドポイントは現在の Editor の状態に応じて適切なソースを自動選択します。

### クエリパラメータ

| パラメータ | 既定値 | 説明 |
|-------------|-----------|------|
| `width` | ネイティブ幅(Play)/ `640`(Edit) | 出力幅(px)、最大 1920。Play モードでは再レンダリングではなく、キャプチャした GameView フレームをスケーリングします |
| `height` | ネイティブ高さ(Play)/ `360`(Edit) | 出力高さ(px)、最大 1080。Play モードでは再レンダリングではなく、キャプチャした GameView フレームをスケーリングします |
| `format` | `jpeg` | `png` または `jpeg` |
| `quality` | `85` | JPEG 品質(1–100、`format=jpeg` の場合に有効) |

### レスポンス

```json
{
  "source": "screen",
  "cameraName": "Main Camera",
  "width": 1920,
  "height": 1080,
  "format": "jpeg",
  "mimeType": "image/jpeg",
  "image": "<base64-encoded image data>"
}
```

| フィールド | 説明 |
|-------|-------------|
| `source` | Play モードでは `"screen"`、Edit モードでは `"sceneView"` |
| `cameraName` | `Camera.main`(Play モード)または Scene View カメラ(Edit モード)の名前。`Camera.main` が `null` の場合は省略 |
| `width` / `height` | 実際の出力サイズ |
| `image` | base64 エンコードされた画像データ |

### エラー

| ステータス | 原因 |
|--------|-------|
| 500 | 画面キャプチャに失敗(Play モード) |
| 503 | Scene View が開かれていない(Edit モード) |

### 例

```bash
# 現在のビューをデフォルト解像度でキャプチャ
curl http://localhost:8765/api/editor/capture

# 指定解像度の PNG に出力画像をリサイズしてキャプチャ
curl "http://localhost:8765/api/editor/capture?width=1280&height=720&format=png"
```

### LLM / MCP ブリッジでの利用

`mimeType` と `image` フィールドは `/api/cameras/capture` と同様に、そのまま MCP の画像コンテンツブロックに渡せます。

---

## GET /api/editor/capture/image

`GET /api/editor/capture` と同じですが、JSON ラッパーではなくバイナリ画像を直接返します。
Play モードでは `width` と `height` はキャプチャした GameView フレームをリサイズするものであり、その解像度で GameView を再レンダリングすることはありません。

### クエリパラメータ

`GET /api/editor/capture` と同じ(`width`、`height`、`format`、`quality` — すべて任意)。

### レスポンス

`Content-Type: image/jpeg`(または `image/png`)のバイナリストリーム。JSON ラッパーはありません。

### エラー

| ステータス | 原因 |
|--------|-------|
| 500 | 画面キャプチャに失敗(Play モード) |
| 503 | Scene View が開かれていない(Edit モード) |

### 例

```bash
# ブラウザで直接表示
open "http://localhost:8765/api/editor/capture/image"

# ファイルに保存
curl -o screenshot.jpg "http://localhost:8765/api/editor/capture/image"

# 指定解像度に出力画像をリサイズして PNG で保存
curl -o view.png "http://localhost:8765/api/editor/capture/image?format=png&width=1280&height=720"
```
