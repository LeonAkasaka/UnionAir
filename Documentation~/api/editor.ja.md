# API リファレンス — Editor

[English](editor.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](editor.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`。実際の URL は接続時に `<project>/.unionair/endpoint.txt` から読み取ってください。エンドポイントの発見手順、レスポンスの規約、カテゴリ/セキュリティの注意事項は [API リファレンス索引](../api-reference.ja.md) を参照してください。

このページのシェル例は `BASE_URL="$(tr -d '\r\n' < .unionair/endpoint.txt)"` を前提としています。`${BASE_URL}` は末尾の `/api/` まで含みます。

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
  "unityVersion": "6000.3.5f2",
  "isTestRunning": false,
  "testRunSource": null,
  "testRunId": null,
  "sessionId": "f40cbf3fc3224a97b5b7ac7aa3b1ea38",
  "lifecycleGeneration": 3,
  "settled": true,
  "hasCompileErrors": false,
  "compileState": null,
  "compileId": null,
  "compileSource": null,
  "buildState": null,
  "buildId": null,
  "activeActivity": null
}
```

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `isPlaying` | bool | Play モード中かどうか(`EditorApplication.isPlaying`) |
| `isPaused` | bool | Play モードで一時停止中かどうか(`EditorApplication.isPaused`) |
| `isCompiling` | bool | スクリプトのコンパイル中かどうか(`EditorApplication.isCompiling`) |
| `isUpdating` | bool | アセット更新処理中かどうか(`EditorApplication.isUpdating`) |
| `unityVersion` | string | Unity バージョン文字列 |
| `isTestRunning` | bool | Unity Test Framework の run が実行中か |
| `testRunSource` | string \| null | API 開始 run は `unionAir`、別ツールから開始した run は `external`、アイドル時は `null` |
| `testRunId` | string \| null | UnionAir run ID。外部 run とアイドル時は `null` |
| `sessionId` | string | Editor プロセスごとに再生成される識別子 |
| `lifecycleGeneration` | number | 現在の Editor プロセスにおける assembly domain のカウンタ。1 から開始 |
| `settled` | bool | コンパイル中でもアセット更新中でもプレイヤービルド中でもビルドターゲット切り替え中でもないかどうか |
| `hasCompileErrors` | bool | `EditorUtility.scriptCompilationFailed` に基づくヒント |
| `compileState` | string \| null | 実行中のコンパイルの `queued` / `running`。それ以外は `null` |
| `compileId` / `compileSource` | string \| null | 実行中のコンパイルの識別情報 |
| `buildState` | string \| null | 実行中のプレイヤービルドの `queued` / `running`。それ以外は `null` |
| `buildId` | string \| null | 実行中のビルドの識別子 |
| `activeActivity` | object \| null | Editor が実行中のアクティビティ。[Editor アクティビティ](activities.ja.md) を参照 |

このエンドポイントはテスト実行中も利用できます。health、help、logs、Test Runner の status/result/cancel 操作以外のエンドポイントは、active run が終了するまで `409` を返します。

### domain reload の検出

assembly domain のリロード中はサーバーが停止するため、その間のリクエストは接続に失敗します。`lifecycleGeneration` はこれをクラッシュと区別するためのものです。domain がロードされるたびに増加するため、接続が切れる前に観測した値より大きければ、リロードが完了したと確認できます。これまでのリロード回数は `lifecycleGeneration - 1` です。

`sessionId` は Editor プロセスが再起動したときにのみ変化し、そのとき `lifecycleGeneration` も 1 にリセットされます。

> `settled` はスナップショットであり、リロードが差し迫っていないことを保証するものではありません。コンパイルが完了して `isCompiling` が false になった直後に、domain reload が始まることがあります。クライアントはどのリクエストでも接続断を許容してリトライすべきであり、`settled` を完了シグナルとして扱ってはいけません。
>
> リクエストが `409` を返したときに読むべきフィールドは `activeActivity` です。アクティビティ名・source・所有する id を報告し、拒否応答と同じ優先順位を適用します。そのためクライアントは `isCompiling` / `isUpdating` / `isPlaying` / `isTestRunning` からどれを待てばよいかを推測する必要がありません。[Editor アクティビティ](activities.ja.md) を参照してください。
>
> `hasCompileErrors` は Console のログから導出されます。Unity は Console の設定によっては再コンパイル時にログを消去します。確定的な結果ではなくヒントとして扱ってください。

---

## GET /api/editor/logs

Unity Console のログを返します。インメモリのリングバッファに最大1000件保持され、すべてのエントリは NDJSON ファイルにも追記されるため、Editor プロセスが動作している間は **domain reload をまたいで保持されます**。

### クエリパラメータ

| パラメータ | 既定値 | 説明 |
|-------------|-----------|------|
| `type` | `all` | 大文字小文字を区別しない `log` / `warning` / `error` / `exception` / `assert` / `all` |
| `search` | ―  | メッセージに対する大文字小文字を区別しない部分一致フィルタ |
| `limit` | `100` | 返す結果の最大数(最大: 1000) |
| `since` | ―  | 排他的なシーケンスカーソル。この値より大きい `sequence` のエントリのみを返します |

### レスポンス

```json
{
  "sessionId": "f40cbf3fc3224a97b5b7ac7aa3b1ea38",
  "count": 2,
  "oldestSequence": 0,
  "latestSequence": 42,
  "truncated": false,
  "hasMore": false,
  "logs": [
    {
      "sequence": 42,
      "type": "error",
      "message": "NullReferenceException: Object reference not set...",
      "stackTrace": "MyScript.Update () (at Assets/MyScript.cs:42)",
      "timestamp": "2026-05-16T04:12:00.1234567Z"
    },
    {
      "sequence": 41,
      "type": "warning",
      "message": "Shader 'Custom/Foo' has no shadows pass",
      "stackTrace": "",
      "timestamp": "2026-05-16T04:11:58.7654321Z"
    }
  ]
}
```

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `sessionId` | string | Editor プロセスごとに再生成される識別子 |
| `oldestSequence` | number | メモリ上に残る最古の sequence。空の場合は `-1` |
| `latestSequence` | number | メモリ上に残る最新の sequence。空の場合は `-1` |
| `truncated` | bool | `since` 以降のエントリが既にインメモリバッファから追い出されていたか |
| `hasMore` | bool | `limit` を超えて一致するエントリが残っていたか |
| `sequence` | number | 現在の Editor セッション内で単調増加するエントリ番号 |

> ログは新しい順(`sequence` 降順)で返されます。`since` を指定した場合も同じです。
> `timestamp` は UTC の ISO 8601 です。

### カーソルによるポーリング

前回のレスポンスの `latestSequence` を `since` に渡すと、新しいエントリだけを取得できます。`since` は **排他的** で、`type` と `search` のフィルタよりも **先に** 適用されます。そのため `truncated` は、フィルタで除外されたエントリではなく、失われたエントリを示します。

`sequence` は Editor プロセスごとに 0 から振り直されます。前回のレスポンスと `sessionId` を比較し、変化していればカーソルを破棄してください。

`truncated` が `true` の場合、保持中の2ファイルの NDJSON 範囲に残っているエントリは [`GET /api/editor/logs.ndjson`](#get-apieditorlogsndjson) で回収できます。

不明な `type` 値はフィルターを暗黙に無効化せず、`400 Bad Request` を返します。`since` が非負整数でない場合も `400` を返します。

### 例

```bash
# 最新のエラー・例外 20 件
curl "${BASE_URL}editor/logs?type=error&limit=20"

# "NullReference" を含むログ
curl "${BASE_URL}editor/logs?search=NullReference"

# sequence 42 より新しいエントリのみ
curl "${BASE_URL}editor/logs?since=42"
```

---

## GET /api/editor/logs.ndjson

現在の Editor セッションで保持中の NDJSON ログをダウンロードします。インメモリのリングバッファから既に追い出されたエントリも含みます。1行につき1つの JSON オブジェクトで、古い順に並び、フィールドは上記の `logs` 配列と同じです。

- Content type: `application/x-ndjson`
- Content disposition: `attachment; filename="console.ndjson"`
- ログファイルが利用できない場合は `404` を返します

アクティブなファイルは約 8 MiB に達するとローテーションされます。レスポンスは同じセッションの前世代(`console.1.ndjson`)にアクティブファイル(`console.ndjson`)を続けて連結するため、ローテーション境界をまたいでも JSON 行は古い順です。保持されるのは最大でこの2ファイルで、それ以前のエントリは回収できません。以前の Editor プロセスが残した前世代ファイルはレスポンスに含まれません。

```bash
curl -O "${BASE_URL}editor/logs.ndjson"
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
curl "${BASE_URL}cameras"

# Main Camera をデフォルト解像度の JPEG でキャプチャ
curl --get "${BASE_URL}cameras/capture" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}'

# HD 解像度の PNG でキャプチャ
curl --get "${BASE_URL}cameras/capture" \
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
open "${BASE_URL}cameras/capture/image?target=%7B%22type%22%3A%22hierarchyPath%22%2C%22value%22%3A%22Main%20Camera%22%7D"

# curl でファイルに保存
curl --get -o screenshot.png "${BASE_URL}cameras/capture/image" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}' \
  --data-urlencode "format=png"

# HD JPEG で保存
curl --get -o hd.jpg "${BASE_URL}cameras/capture/image" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}' \
  --data-urlencode "width=1280" --data-urlencode "height=720" --data-urlencode "quality=90"
```

---

## POST /api/previews/render

ユーザーのシーン内 Camera に依存せず、シーン GameObject、prefab、import 済み model を描画します。target を隔離 preview scene にコピーし、任意で animation を評価し、Renderer bounds を frame に収め、専用 camera と light で各時刻を描画してから preview scene を閉じます。

常時有効な Read カテゴリの endpoint ですが、Play mode、test run、compile、asset update、build、build target switch の実行中は拒否されます。sampling と rendering は同じ request 内で原子的に行われます。animation pose は後続の capture request まで保持されません。

### リクエスト

```json
{
  "target": { "assetPath": "Assets/Characters/Hero.prefab" },
  "focusPath": "Rig/Head",
  "width": 640,
  "height": 640,
  "format": "png",
  "times": [0.0, 0.5, 1.0],
  "view": {
    "preset": "front",
    "fieldOfView": 30.0,
    "padding": 0.1
  },
  "background": { "r": 0.18, "g": 0.18, "b": 0.18, "a": 1.0 },
  "lighting": {
    "keyIntensity": 1.0,
    "fillIntensity": 0.5,
    "keyColor": { "r": 1.0, "g": 1.0, "b": 1.0 },
    "fillColor": { "r": 0.65, "g": 0.72, "b": 1.0 }
  },
  "animation": {
    "mode": "state",
    "state": "Base Layer.Idle",
    "layer": 0
  }
}
```

request 内のすべての object は未知または重複した field を拒否します。数値は有限値でなければなりません。

### Target と focus

`target` は必須で、次のいずれかの参照を受け付けます。

- `{ "type": "hierarchyPath", "value": "Character" }` のような scene object 参照、または GameObject の `globalObjectId`。path 参照では `scenePath` で loaded scene を選択できます。
- `{ "assetGuid": "..." }` または `{ "assetPath": "Assets/Character.prefab" }` のような、prefab または import 済み model の root GameObject に解決される asset 参照。

prefab/model は `PrefabUtility.InstantiatePrefab` で instantiate します。prefab connection を持たないものを含む scene object は `Object.Instantiate` で copy し、preview root として detach して preview scene に移動します。source scene object 自体を移動または sample することはありません。

`focusPath` は copy 後の target を起点とする任意の `/` 区切り Transform path です。その subtree 内で active かつ enabled な Renderer のみが bounds に含まれます。target 全体を frame に収める場合は省略します。path がない場合は `404`、有限で non-zero の renderer bounds がない subtree は `422` です。

### View と framing

`view` は任意です。

| フィールド | 既定値 | 説明 |
|---|---:|---|
| `preset` | `front` | `front`、`back`、`left`、`right`、`top`、`bottom`、`isometric` |
| `yaw` / `pitch` | — | `preset` の代わりとなる degree 単位の明示的 orbit。yaw 0 は正面(camera は +Z 側)、正の yaw は +X 側、正の pitch は target 上方へ移動 |
| `distance` | auto | 1,000,000 以下の正の world-space 距離。省略時は bounds の全 corner を水平/垂直 field of view の両方に fit |
| `fieldOfView` | `30` | 1–120 degree の垂直 perspective field of view |
| `padding` | `0.1` | 各 image edge に確保する割合。0 以上 0.5 未満。`0.1` は中央 80% に fit |

preset と yaw/pitch は同時指定できません。automatic framing は axis-aligned Renderer bounds の 8 corner 全てを解決済み camera axis に投影し、bounding sphere では近似しません。各 frame の animation 評価後に framing を再計算するため、pose により bounds が変わる場合は frame ごとに距離も変わります。

### Animation mode

copy された target を authored state のまま描画するには `animation` を省略するか `{ "mode": "none" }` を指定します。それ以外の mode は Animator をちょうど 1 つ必要とします。target に複数ある場合は `animation.animatorPath` で選択し、曖昧な場合は `409` です。

| Mode | フィールド | 各 `times` 値の意味 |
|---|---|---|
| `clip` | `clip`: AnimationClip asset 参照、任意の `clipName` | Animator 経由で評価する `AnimationClipPlayable` 上の秒数 |
| `state` | `state`: 完全な state 名、任意の `layer`(既定 0) | `Animator.Play` に渡す normalized state time |
| `parameters` | `parameters`: `{name,value}` の配列 | rebind と parameter set 全体の適用後に進める秒数 |

parameter の value type は Animator から決まります。Float は有限 JSON number、Int は整数、Bool は boolean、Trigger は boolean(`true` は set、`false` は reset)です。未知または重複した name、不正な value type は描画前に拒否します。state / parameters mode は RuntimeAnimatorController を必要とします。

clip 評価には `AnimationMode.SampleAnimationClip` ではなく `AnimationClipPlayable` を使います。playable は copy 側 Animator を target とするため、humanoid retargeting は Animator が担当し、source Animator や Avatar の assignment を変更しません。寄与する clip ごとに、copy 上に path と component がある binding を `appliedBindings`、ないものを `skippedBindings` に返します。

`.anim` file と AnimationClip を 1 つ含む import file では `clipName` は不要です。import file に複数 clip がある場合、省略すると 1 つを黙って選ばず、利用可能な name とともに `409` を返します。sub-asset を選択するには正確な name を指定してください。

### サイズ、background、lighting

| フィールド | 既定値 | 制限 / 動作 |
|---|---:|---|
| `width`, `height` | `640`, `640` | 1–1920 × 1–1080 |
| `format` | `png` | `png` または `jpeg` |
| `quality` | `85` | JPEG quality、1–100 |
| `times` | `[0]` | 0 以上 1,000,000 以下の値を 1–16 個。width × height × frame 数は 16,777,216 pixel 以下 |

colour は 0–1 の必須 `r`、`g`、`b` と、任意の `a`(既定 1)を使います。camera は solid background です。lighting はユーザーの scene から独立し、shadow なしの directional light 2 灯です。既定値は白色 key intensity 1.0 と青みのある fill intensity 0.5 です。`keyIntensity` / `fillIntensity` は 0–8。response は実際に使った background と light model を返します。

preview scene の culling-mask bit は有限なため、同時に所有できる request は最大 8 件です。9 件目は `429` です。成功/失敗を問わず `finally` で scene を閉じ、clone、camera、light を破棄して bit を解放します。active scene、dirty state、selection、user camera、Animator assignment、asset、Undo history、AnimationMode は変更しません。

### レスポンス

```json
{
  "target": {
    "kind": "asset",
    "name": "Hero",
    "assetGuid": "...",
    "assetPath": "Assets/Characters/Hero.prefab"
  },
  "focusPath": "Rig/Head",
  "width": 640,
  "height": 640,
  "format": "png",
  "mimeType": "image/png",
  "rigType": "humanoid",
  "animatorPath": "",
  "animation": { "mode": "state", "state": "Base Layer.Idle", "layer": 0 },
  "view": {
    "preset": "front",
    "yaw": 0.0,
    "pitch": 0.0,
    "requestedDistance": null,
    "fieldOfView": 30.0,
    "padding": 0.1
  },
  "background": { "r": 0.18, "g": 0.18, "b": 0.18, "a": 1.0 },
  "lighting": {
    "model": "twoDirectionalNoShadows",
    "keyIntensity": 1.0,
    "keyColor": { "r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0 },
    "fillIntensity": 0.5,
    "fillColor": { "r": 0.65, "g": 0.72, "b": 1.0, "a": 1.0 }
  },
  "frames": [{
    "time": 0.0,
    "framing": {
      "bounds": {
        "center": { "x": 0.0, "y": 1.0, "z": 0.0 },
        "size": { "x": 0.7, "y": 0.8, "z": 0.6 }
      },
      "cameraPosition": { "x": 0.0, "y": 1.0, "z": 2.5 },
      "cameraRotation": { "x": 0.0, "y": 1.0, "z": 0.0, "w": 0.0 },
      "distance": 2.5
    },
    "states": [{
      "layer": 0,
      "fullPathHash": 1168970017,
      "shortNameHash": 987654321,
      "normalizedTime": 0.0,
      "length": 1.0,
      "loop": true,
      "clips": [{ "name": "Idle", "weight": 1.0 }]
    }],
    "appliedBindings": [{ "path": "Rig/Hips", "type": "UnityEngine.Transform", "property": "m_LocalPosition.x" }],
    "skippedBindings": [],
    "mimeType": "image/png",
    "image": "<base64>"
  }]
}
```

`rigType` は `humanoid`、`generic`、`none` のいずれかです。`states` は state/parameter 評価時の全 Animator layer を含み、AnimatorController state を持たない direct clip 評価では空です。hash は request の echo ではなく、解決済み `AnimatorStateInfo` の値です。frame 順は `times` 順です。

### エラー

| ステータス | 原因 |
|---|---|
| 400 | JSON shape、field、type、range、mode、preset、format、time 数、総 pixel 数が不正 |
| 404 | target、focus path、Animator path、clip asset、指定した `clipName` が見つからない |
| 409 | Editor activity conflict、または `animatorPath` なしで Animator が複数ある |
| 422 | target が GameObject asset/object でない、bounds がない、Animator/controller/state がない、animation input が非互換 |
| 429 | 8 件の preview request が既に preview scene を所有中 |
| 500 | clone、評価、描画、encode 中に Unity が失敗 |

### 例

```bash
curl -X POST "${BASE_URL}previews/render" \
  -H "Content-Type: application/json" \
  -d '{
    "target":{"assetPath":"Assets/Characters/Hero.prefab"},
    "times":[0,0.5,1],
    "animation":{"mode":"clip","clip":{"assetPath":"Assets/Animations/Idle.anim"}}
  }'
```

---

## POST /api/previews/render/image

`POST /api/previews/render` と同じ body と isolation rule を使いますが、`times` はちょうど 1 値でなければならず、encode 済み画像を `Content-Type: image/png` または `image/jpeg` で直接返します。framing、resolved state、binding diagnostics が必要な場合は JSON endpoint を使用してください。

```bash
curl -X POST "${BASE_URL}previews/render/image" \
  -H "Content-Type: application/json" \
  -d '{"target":{"type":"hierarchyPath","value":"Character"},"times":[0],"format":"png"}' \
  -o preview.png
```

---

## POST /api/editor/refresh

`AssetDatabase.Refresh()` を呼び出し、スクリプトやアセットの変更を Unity に認識させます。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。
> 読み込み中のシーンファイルが外部で変更されている場合は、`AssetDatabase.Refresh()` を呼ばずに `409 Conflict` を返します。これにより、Unity の対話的な Reload ダイアログが API を停止させることを防ぎます。

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

### 読み込み中シーンの競合 — 409

```json
{
  "error": "Cannot refresh assets while loaded scenes have external file changes. Unload them before retrying to avoid Unity's interactive Reload dialog.",
  "code": "loaded_scene_external_change_blocked",
  "loadedScenes": [
    {
      "path": "Assets/Scenes/Level.unity",
      "name": "Level",
      "isDirty": true,
      "isActive": true,
      "reason": "modified"
    }
  ]
}
```

| フィールド | 説明 |
|-----------|------|
| `code` | 安定した機械可読識別子: `loaded_scene_external_change_blocked` |
| `loadedScenes` | Scene Manager の順序で並んだ競合中の読み込み済みシーン |
| `isDirty` | メモリ上のシーンに未保存の Editor 変更があるか |
| `isActive` | アクティブシーンか |
| `reason` | `modified`、`missing`、`unreadable`、`untracked` のいずれか。`untracked` は信頼できるディスク基準値が記録されていない状態 |

UnionAir はシーンを自動的に保存、破棄、unload、reload しません。再試行する前に、どちらの内容を残すかを明示的に決めてください。

- Editor のメモリ上の内容を残す場合は、シーンを明示的に保存して外部のファイル変更を上書きした後、refresh します。
- 外部ファイルの内容を残す場合は、先にシーンを unload します。dirty の場合は明示的に保存するか、`discardUnsaved: true` で unload してから refresh し、シーンを再度開きます。

`untracked` のシーンは、実際の外部変更を見落とす可能性があるため、refresh時に新しい基準値として自動採用されません。メモリ上の内容を残す場合は明示的に保存し、ディスク上の内容を残す場合はunloadして開き直してください。

Editorのコールド起動直後は、バックグラウンド対応の基準値bootstrapがシーン復元とアセット更新の完了を待っている間、一時的に`untracked`になることがあります。復旧操作を選ぶ前に、数回のEditor update後に再試行してください。継続する場合は、上記の明示的な保存またはunloadして開き直す手順を使用します。

基準値はシーンを開くか保存したときに更新され、assembly domain reload をまたいで保持されます。このガードは UnionAir が開始する refresh に適用されます。Unity 自身のフォーカス時の自動 refresh と Editor からの手動 refresh は、引き続き Unity の通常動作に従います。

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
curl "${BASE_URL}editor/menu-items?search=UnionAir"
curl "${BASE_URL}editor/menu-items?root=Window&includeFolders=false"
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

### `executed` はディスパッチの報告であり、効果の報告ではありません

`executed: true` は `EditorApplication.ExecuteMenuItem()` が返した値そのものです。メニュー項目が見つかり、有効であり、呼び出されたことを意味します。その項目が何かを行ったという主張ではありません。

フォーカスされている `EditorWindow` を参照するコマンドのメニュー項目は、REST 経由では参照すべきウィンドウが存在しないため、ディスパッチされても何も起こりません。6000.0.80f1 で計測したところ、以下はいずれも `200 {"executed": true}` を返し、何も変更しませんでした(GameObject を選択した場合、アセットを選択した場合の両方で、`POST /api/editor/selection` により選択状態を確認したうえで実施)。

| メニュー項目 | 結果 |
|---|---|
| `Edit/Duplicate` | シーンオブジェクト・アセットのいずれも複製されない |
| `Edit/Delete` | 選択した GameObject が残ったまま |
| `Edit/Select All` | 選択状態が変化しない |
| `Assets/Create/Folder` | フォルダーが作成されない |

対照的に `Edit/Undo` は動作します。`POST /api/gameobjects/primitive` で GameObject を作成してから実行すると、そのオブジェクトが削除されます。境界は「`Edit/` 配下かどうか」ではなく、「コマンドがフォーカスされたウィンドウを必要とするかどうか」です。

確認用の第2のフィールドはありません。任意のメニュー項目が何をするはずだったかを UnionAir は知り得ないためです。`200` を根拠とせず、読み取り(`GET /api/assets`、`GET /api/scene/hierarchy`、`GET /api/editor/selection`)で効果を検証してください。その操作に対応する専用エンドポイントがある場合は、そちらを優先してください。専用エンドポイントは変更した内容を報告します。

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `path` の欠落または空 |
| 403 | Editor Actions カテゴリが無効 |
| 404 | メニュー項目が見つからない、無効化されている、または実行できなかった |
| 409 | Unity Editor が Play モード中 |

### 例

```bash
curl -X POST "${BASE_URL}editor/menu-item" \
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
curl "${BASE_URL}editor/capture"

# 指定解像度の PNG に出力画像をリサイズしてキャプチャ
curl "${BASE_URL}editor/capture?width=1280&height=720&format=png"
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
open "${BASE_URL}editor/capture/image"

# ファイルに保存
curl -o screenshot.jpg "${BASE_URL}editor/capture/image"

# 指定解像度に出力画像をリサイズして PNG で保存
curl -o view.png "${BASE_URL}editor/capture/image?format=png&width=1280&height=720"
```
