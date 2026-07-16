# API リファレンス — Play Mode

[English](playmode.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](playmode.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`(デフォルトポート: **8765**)。レスポンスの規約とカテゴリ/セキュリティの注意事項は [API リファレンス索引](../api-reference.ja.md) を参照してください。

---

## POST /api/editor/play

Play モードに入ります(`EditorApplication.isPlaying = true`)。

> Play Mode カテゴリが有効な場合のみ呼び出せます。
> Domain reload が発生した場合、HTTP サーバは一時的に再起動します。`GET /api/editor/status` をポーリングし、`isPlaying: true` になるまで待ってください。

### レスポンス

```json
{ "playing": true, "note": "Domain reload may occur. Poll GET /api/editor/status until isPlaying is true." }
```

---

## POST /api/editor/stop

Play モードを終了します(`EditorApplication.isPlaying = false`)。

> Play Mode カテゴリが有効な場合のみ呼び出せます。

### レスポンス

```json
{ "playing": false }
```

---

## POST /api/editor/pause

一時停止状態を設定します。ボディを省略した場合は現在の状態をトグルします。

> Play Mode カテゴリが有効な場合のみ呼び出せます。

### リクエストボディ(JSON、任意)

```json
{ "paused": true }
```

### レスポンス

```json
{ "paused": true }
```

---

## POST /api/editor/step

1フレーム進めます。`isPaused: true` の場合のみ有効です。

> Play Mode カテゴリが有効な場合のみ呼び出せます。

### レスポンス

```json
{ "stepped": true }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | Play モードでない、または一時停止していない |
| 403 | Play Mode カテゴリが無効 |

---

## GET /api/playmode/input/actions

実行中のゲームで有効な Unity Input System アクションを一覧します。

> オプションの `com.unity.inputsystem` パッケージが必要です。
> Play Mode カテゴリが有効な場合のみ呼び出せます。
> Play モード外では `409 Conflict` を返します。

### レスポンス

```json
{
  "actions": [
    {
      "name": "Jump",
      "map": "Player",
      "actionType": "Button",
      "expectedControlType": "Button",
      "bindings": ["<Keyboard>/space", "<Gamepad>/buttonSouth"]
    }
  ],
  "count": 1
}
```

| フィールド | 説明 |
|-------|-------------|
| `actions[].name` | InputAction 名。名前が一意であれば、`perform` または `set` で裸の名前を使用可能 |
| `actions[].map` | アクションマップ名、または空文字列。空でないマップ名とアクション名を `Map/Action` として組み合わせると、曖昧さのない識別子になる |
| `actions[].actionType` | `Button`、`Value`、`PassThrough` などの Unity InputAction タイプ |
| `actions[].expectedControlType` | アクションが宣言する期待コントロールタイプ |
| `actions[].bindings` | アクションが公開する空でない有効なバインディングパス |

### エラー

| ステータス | 原因 |
|--------|-------|
| 403 | Play Mode カテゴリが無効 |
| 409 | Unity Editor が Play モードでない |

---

## POST /api/playmode/input/perform

UnionAir の仮想デバイスを通じて Button InputAction を実行します。`action` には、大文字小文字を区別しない `Map/Action` 識別子、または名前が一意な場合は裸のアクション名を指定できます。

> オプションの `com.unity.inputsystem` パッケージが必要です。
> Play Mode カテゴリが有効な場合のみ呼び出せます。
> Play モード外では `409 Conflict` を返します。

### リクエストボディ(JSON)

Button アクションのタップ:

```json
{ "action": "Player/Jump" }
```

収集されたアクションのうち `Jump` という名前が1件だけの場合は、短い `{ "action": "Jump" }` 形式も使用できます。

`mode` は任意で、既定は `tap`(press → update → release → update を送信)です。

Button アクションの押しっぱなし:

```json
{ "action": "Player/Jump", "mode": "press" }
```

そのアクションで UnionAir が保持しているすべてのコントロールを解放:

```json
{ "action": "Player/Jump", "mode": "release" }
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `action` | ✅ | `Map/Action` 識別子、または一意な場合は裸の InputAction 名 |
| `mode` | ❌ | `tap`、`press`、または `release`。既定は `tap` |

このエンドポイントは `value` を受け付けません。Axis、Vector2、Stick アクションには `POST /api/playmode/input/set` を使用します。

`tap` と `press` では、UnionAir はアクションのバインディング順で最初にサポートされる非コンポジットの Button バインディングを使用します。サポートされる Button デバイスは Keyboard、Gamepad、Mouse、および `<Pointer>/press`(仮想 Mouse の左ボタンにマップ)です。Touch、Pen、XR、カスタムデバイス、コンポジットバインディングは `422` を返します。

`release` では、UnionAir はそのアクションで現在保持しているすべてのコントロールを解放します。呼び出し側は `press` 時に選択されたバインディングを指定する必要はありません。

### レスポンス

Tap:

```json
{
  "success": true,
  "action": "Jump",
  "controlType": "Button",
  "mode": "tap",
  "pressedBinding": "<Keyboard>/space",
  "pressedControl": "/UnionAirVirtualKeyboard/space",
  "releasedControl": "/UnionAirVirtualKeyboard/space"
}
```

Press:

```json
{
  "success": true,
  "action": "Jump",
  "controlType": "Button",
  "mode": "press",
  "pressedBinding": "<Keyboard>/space",
  "pressedControl": "/UnionAirVirtualKeyboard/space"
}
```

Release:

```json
{
  "success": true,
  "action": "Jump",
  "controlType": "Button",
  "mode": "release",
  "releasedControls": ["/UnionAirVirtualKeyboard/space"]
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `action` の欠落、`mode` が不正、`value` が指定された、またはアクションが Button アクションでない |
| 403 | Play Mode カテゴリが無効 |
| 404 | アクションが見つからない |
| 409 | Unity Editor が Play モードでない、ポインタ操作が進行中、または裸のアクション名が複数のマップに一致。曖昧な場合のレスポンスには `candidates` が含まれる |
| 422 | Button アクションは存在するが、シミュレート可能な Keyboard/Gamepad/Mouse/Pointer の Button バインディングがない |

---

## POST /api/playmode/input/set

UnionAir の仮想デバイスを通じて Axis、Vector2、または Stick InputAction の値を設定します。`action` には、大文字小文字を区別しない `Map/Action` 識別子、または名前が一意な場合は裸のアクション名を指定できます。値は別の `set` 呼び出しによる変更、Play モードの変化、または UnionAir による仮想デバイスのクリーンアップまで維持されます。

> オプションの `com.unity.inputsystem` パッケージが必要です。
> Play Mode カテゴリが有効な場合のみ呼び出せます。
> Play モード外では `409 Conflict` を返します。

### リクエストボディ(JSON)

Vector2 / Stick アクション:

```json
{ "action": "Player/Move", "value": [1.0, 0.0] }
```

ニュートラルに戻す:

```json
{ "action": "Move", "value": [0.0, 0.0] }
```

Axis アクション:

```json
{ "action": "Throttle", "value": 1.0 }
```

ニュートラルに戻す:

```json
{ "action": "Throttle", "value": 0.0 }
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `action` | ✅ | `Map/Action` 識別子、または一意な場合は裸の InputAction 名 |
| `value` | ✅ | Axis: 有限の数値、Vector2/Stick: `[x, y]` |

複数のバインディングを持つアクションでは、UnionAir はバインディング順で最初にサポートされる直接的な Gamepad 値バインディングを使用します。サポートされる set バインディングは `<Gamepad>/leftStick`、`<Gamepad>/rightStick`、`<Gamepad>/leftTrigger`、`<Gamepad>/rightTrigger`、および Gamepad スティックの x/y 軸です。WASD などのキーボードコンポジット、矢印キーコンポジット、Touch、Pen、XR、カスタムデバイス、その他のコントロールは `422` を返します。

### レスポンス

Vector2:

```json
{
  "success": true,
  "action": "Move",
  "controlType": "Vector2",
  "value": [1.0, 0.0],
  "setBinding": "<Gamepad>/leftStick",
  "setControl": "/UnionAirVirtualGamepad/leftStick"
}
```

Axis:

```json
{
  "success": true,
  "action": "Throttle",
  "controlType": "Axis",
  "value": 1.0,
  "setBinding": "<Gamepad>/rightTrigger",
  "setControl": "/UnionAirVirtualGamepad/rightTrigger"
}
```

### 注意

UnionAir は書き込んだバインディング/コントロールを報告しますが、仮想デバイスイベントのキュー投入後のアクション解決は Unity Input System の責務です。`PlayerInput` のデバイスペアリング、コントロールスキーム、バインディングマスク、インタラクション、プロセッサ、アクションの有効化状態により、アクションが仮想デバイスを観測できない場合があります。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `action` の欠落、`value` が不正・欠落、またはアクションが Button アクション |
| 403 | Play Mode カテゴリが無効 |
| 404 | アクションが見つからない |
| 409 | Unity Editor が Play モードでない、ポインタ操作が進行中、または裸のアクション名が複数のマップに一致。曖昧な場合のレスポンスには `candidates` が含まれる |
| 422 | アクションは存在するが、設定可能な直接的な Gamepad Axis/Vector2 バインディングがない |

---

## POST /api/playmode/input/pointer

UnionAir の仮想マウスを通じて、画面座標でのマウスクリック・押下・解放・移動をシミュレートします。move・press・release の各フェーズは別々のプレイヤーループフレームでキューに投入されるため、実行中のゲームは本物の入力とまったく同じように観測します: `InputSystemUIInputModule` のレイキャスト(3D オブジェクトへの `PhysicsRaycaster` ヒットを含む)、`<Pointer>`/`<Mouse>` アクションバインディング、`Mouse.current` をポーリングするコードのいずれも、本物のクリックと同様に反応します。

> オプションの `com.unity.inputsystem` パッケージが必要です。
> Play モード中かつ Play Mode カテゴリが有効な場合のみ呼び出せます。
> レスポンスは最後の入力フレームが消費された後に送信されます — `tap` はおよそ 3〜4 プレイヤーフレームかかります。プレイヤーフレームが進行している必要があります(Game ビューにフォーカスするか、Input System パッケージの Background Behavior を適切に設定してください)。そうでない場合、リクエストは5秒でタイムアウトします。
> 同時に実行できるポインタ操作は1つだけです。並行リクエストは `409` を返します。
> 制限事項: レガシー Input Manager の API(`Input.GetMouseButton`、`OnMouseDown` など)は Input System のイベントを観測しません。また仮想 `Touchscreen` デバイス(EnhancedTouch)はまだサポートされていません。クリック前に座標が何にヒットするかを確認するには `POST /api/playmode/screen/hittest` を使用してください。

### リクエストボディ(JSON)

```json
{
  "normalizedPosition": { "x": 0.5, "y": 0.5 },
  "origin": "topLeft",
  "mode": "tap"
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `position` | ✅* | Game ビュー(`Screen.width` × `Screen.height`)内のピクセル座標 `{ "x", "y" }`。範囲外の値は `422` を返します |
| `normalizedPosition` | ✅* | `0`〜`1` の正規化座標 `{ "x", "y" }`(範囲内にクランプ) |
| `origin` | ❌ | `bottomLeft`(既定、Unity スクリーン空間)または `topLeft`。`/api/editor/capture` のスクリーンショットから座標を拾う場合は `topLeft` を使用 |
| `mode` | ❌ | `tap`(既定)、`press`、`release`、または `move` |
| `button` | ❌ | `left`(既定)、`right`、または `middle` |
| `holdFrames` | ❌ | `tap` のみ: press と release の間にボタンを保持するプレイヤーフレーム数、`1`〜`300`(既定 `1`) |

*`position` と `normalizedPosition` はどちらか一方のみを指定してください。`mode: "release"` では座標は任意で、既定は現在の仮想マウス位置です。

`press` はボタンを押したまま保持します(Play モード終了時に自動解放)。ドラッグや長押しには `release` と組み合わせてください。`move` は仮想マウス位置の更新のみ行います。位置は呼び出し間で維持され、`POST /api/playmode/input/perform` の Mouse/Pointer バインディングでも使用されます。

### レスポンス

```json
{
  "success": true,
  "mode": "tap",
  "button": "left",
  "position": { "x": 640, "y": 360 },
  "screenSize": { "width": 1280, "height": 720 },
  "pressFrame": 1204,
  "releaseFrame": 1205
}
```

| フィールド | 説明 |
|-------|-------------|
| `position` | Unity スクリーン空間(左下原点)で解決されたピクセル座標 |
| `screenSize` | 座標の解決に使われた Game ビューの解像度 |
| `pressFrame` / `releaseFrame` | press / release イベントがキューに投入されたときの `Time.frameCount`(`press` モードは `releaseFrame` を省略、`move` は両方省略) |
| `released` | `release` モードのみ: 事前の `press` でボタンが保持されていなかった場合は `false` |

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `position`/`normalizedPosition` の両方指定または両方欠落、`origin`・`mode`・`button`・`holdFrames` が不正 |
| 403 | Play Mode カテゴリが無効 |
| 409 | Play モードでない、エディタが一時停止中、別のポインタ操作が進行中、またはシーケンス中に Play モードが終了 |
| 422 | ピクセル `position` が画面外 |
| 500 | プレイヤーフレームが5秒以内に進行しなかった |

---

## POST /api/playmode/screen/hittest

読み取り専用: 入力を一切送信せずに、画面座標をレイキャストし、そこへのポインタクリックが何にヒットするかを報告します。アクティブな `EventSystem` のレイキャスト(すべてのレイキャスター — uGUI の `GraphicRaycaster`、3D コライダーの `PhysicsRaycaster` — のイベントマスクを尊重)と、`Camera.main` からの素の `Physics.Raycast` を組み合わせます。`POST /api/playmode/input/pointer` の前に座標を確認する用途に使用してください。

> Play モード中かつ Play Mode カテゴリが有効な場合のみ呼び出せます。
> `com.unity.inputsystem` パッケージは不要です。

### リクエストボディ(JSON)

```json
{
  "normalizedPosition": { "x": 0.5, "y": 0.5 },
  "origin": "topLeft"
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `position` | ✅* | Game ビュー内のピクセル座標 `{ "x", "y" }`。範囲外の値は `422` を返します |
| `normalizedPosition` | ✅* | `0`〜`1` の正規化座標 `{ "x", "y" }`(範囲内にクランプ) |
| `origin` | ❌ | `bottomLeft`(既定)または `topLeft`。`/api/editor/capture` のスクリーンショットから座標を拾う場合は `topLeft` を使用 |

*`position` と `normalizedPosition` はどちらか一方のみを指定してください。

### レスポンス

```json
{
  "success": true,
  "position": { "x": 640, "y": 360 },
  "screenSize": { "width": 1280, "height": 720 },
  "eventSystemHits": [
    {
      "path": "Cube",
      "globalObjectId": "GlobalObjectId_V1-...",
      "module": "UnityEngine.EventSystems.PhysicsRaycaster",
      "distance": 9.4
    }
  ],
  "physicsCamera": "Main Camera",
  "physicsHit": {
    "path": "Cube",
    "globalObjectId": "GlobalObjectId_V1-...",
    "distance": 9.4,
    "point": [0.1, 0.5, -2.0]
  }
}
```

| フィールド | 説明 |
|-------|-------------|
| `eventSystemHits` | EventSystem 順(ポインタイベントが最初にヒットする順)のレイキャスト結果。アクティブな `EventSystem` がない場合は `null` |
| `eventSystemHits[].module` | ヒットを生成したレイキャスターの型 |
| `physicsCamera` | `Camera.main` の階層パス。存在しない場合は `null` |
| `physicsHit` | その点を通る `Camera.main` からの最初の `Physics.Raycast` ヒット。何にもヒットしない場合は `null` |

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `position`/`normalizedPosition` の両方指定または両方欠落、または `origin` が不正 |
| 403 | Play Mode カテゴリが無効 |
| 409 | Unity Editor が Play モードでない |
| 422 | ピクセル `position` が画面外、または `EventSystem` と `Camera.main` のどちらも存在しない |

---

## GET /api/playmode/ui/elements

Play Mode UI 操作 API の対象にできる、ロード済みシーン内のアクティブな Unity UI(uGUI)および TextMeshPro UI 要素を一覧します。

> Play モード中かつ Play Mode カテゴリが有効な場合のみ呼び出せます。
> v1 は Unity UI と TextMeshPro UI コンポーネントをサポートします。レスポンスの `backend` 値は将来の UI Toolkit サポートのために予約されています。

### クエリパラメータ

| パラメータ | 必須 | 説明 |
|-------------|------|------|
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名。省略時はアクティブシーン |

### レスポンス

```json
{
  "backend": "unityUi",
  "elements": [
    {
      "path": "Canvas/StartButton",
      "globalObjectId": "GlobalObjectId_V1-...",
      "componentGlobalObjectId": "GlobalObjectId_V1-...",
      "type": "UnityEngine.UI.Button",
      "interactable": true
    },
    {
      "path": "Canvas/NameInput",
      "globalObjectId": "GlobalObjectId_V1-...",
      "componentGlobalObjectId": "GlobalObjectId_V1-...",
      "type": "UnityEngine.UI.InputField",
      "interactable": true,
      "text": "Player"
    },
    {
      "path": "Canvas/TMPDropdown",
      "globalObjectId": "GlobalObjectId_V1-...",
      "componentGlobalObjectId": "GlobalObjectId_V1-...",
      "type": "TMPro.TMP_Dropdown",
      "interactable": true,
      "value": 0,
      "optionCount": 3
    }
  ],
  "count": 3
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 403 | Play Mode カテゴリが無効 |
| 404 | `scenePath` がロード済みシーンに一致しない |
| 409 | Play モードでない、または `scenePath` が曖昧 |

---

## POST /api/playmode/ui/click

Unity UI の `Button` または `IPointerClickHandler` を実装したコンポーネントをクリックします。

対象の要素自体がクリック可能でない場合(例: Button の子の `Text`)、
クリックは最も近い祖先のクリックハンドラーにフォールバックします。これは実際の
ポインタクリックがレイキャストをバブリングする挙動を再現したものです。レスポンスには
実際にクリックを受け取ったコンポーネントが報告されます。

> Play モード中かつ Play Mode カテゴリが有効な場合のみ呼び出せます。
> シーン内にアクティブな `EventSystem` が必要です。UnionAir が自動的に作成することはありません。

### リクエストボディ(JSON)

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/StartButton" },
  "backend": "unityUi",
  "normalizedPosition": { "x": 0.5, "y": 0.5 }
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `target` | ✅ | GameObject、`Button`、または `IPointerClickHandler` コンポーネントに解決されるオブジェクト参照 |
| `backend` | ❌ | `unityUi`(既定)。その他の値は将来の UI Toolkit サポートのために予約 |
| `scenePath` | ❌ | `hierarchyPath` / `componentPath` ターゲット用のロード済みシーンセレクタ |
| `normalizedPosition` | ❌ | ターゲット `RectTransform` 内のポインタ位置。`{ "x": 0.5, "y": 0.5 }` が中央。欠落・非数値の座標は `0.5` になり、`0`〜`1` 外の値はクランプされます |

### レスポンス

```json
{
  "success": true,
  "backend": "unityUi",
  "action": "click",
  "path": "Canvas/StartButton",
  "globalObjectId": "GlobalObjectId_V1-...",
  "component": "UnityEngine.UI.Button",
  "componentGlobalObjectId": "GlobalObjectId_V1-...",
  "clicked": true
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | サポート外の `backend`、`target` の欠落、または `target` が ObjectRef JSON オブジェクトでない |
| 403 | Play Mode カテゴリが無効 |
| 404 | ターゲットまたはシーンが見つからない |
| 409 | Play モードでない、アクティブな `EventSystem` がない、またはターゲットが操作不可(not interactable) |
| 422 | ターゲットがクリック可能な Unity UI 要素に解決されない |

---

## POST /api/playmode/ui/text

Unity UI の `InputField` または TextMeshPro の `TMP_InputField` にテキストを設定し、必要に応じて submit します。

> Play モード中かつ Play Mode カテゴリが有効な場合のみ呼び出せます。

### リクエストボディ(JSON)

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/NameInput" },
  "text": "Player",
  "submit": true
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `target` | ✅ | GameObject、`UnityEngine.UI.InputField`、または `TMPro.TMP_InputField` コンポーネントに解決されるオブジェクト参照 |
| `text` | ✅ | 設定するテキスト |
| `submit` | ❌ | `true` の場合、値の設定後に入力フィールドの end-edit コールバックを呼び出します |
| `backend` | ❌ | `unityUi`(既定) |
| `scenePath` | ❌ | `hierarchyPath` / `componentPath` ターゲット用のロード済みシーンセレクタ |

### レスポンス

```json
{
  "success": true,
  "backend": "unityUi",
  "action": "text",
  "path": "Canvas/NameInput",
  "globalObjectId": "GlobalObjectId_V1-...",
  "component": "UnityEngine.UI.InputField",
  "componentGlobalObjectId": "GlobalObjectId_V1-...",
  "text": "Player"
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `text` の欠落、サポート外の `backend`、または不正な `target` |
| 403 | Play Mode カテゴリが無効 |
| 404 | ターゲットまたはシーンが見つからない |
| 409 | Play モードでない、アクティブな `EventSystem` がない、またはターゲットが操作不可 |
| 422 | ターゲットが Unity UI の `InputField` または `TMP_InputField` に解決されない |

---

## POST /api/playmode/ui/scroll

Unity UI の `ScrollRect` を、スクロールデルタまたは正規化位置の直接指定でスクロールします。

> Play モード中かつ Play Mode カテゴリが有効な場合のみ呼び出せます。

### リクエストボディ(JSON)

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/List" },
  "delta": { "x": 0, "y": -1 }
}
```

または正規化位置を直接設定:

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/List" },
  "normalizedPosition": { "x": 0, "y": 1 }
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `target` | ✅ | GameObject または `UnityEngine.UI.ScrollRect` コンポーネントに解決されるオブジェクト参照 |
| `delta` | ❌ | `x` および/または `y` を持つスクロールホイールデルタオブジェクト |
| `normalizedPosition` | ❌ | `x` および/または `y` を持つ正規化スクロール位置の直接指定 |
| `backend` | ❌ | `unityUi`(既定) |
| `scenePath` | ❌ | `hierarchyPath` / `componentPath` ターゲット用のロード済みシーンセレクタ |

`delta` または `normalizedPosition` のいずれかを指定してください。

### レスポンス

```json
{
  "success": true,
  "backend": "unityUi",
  "action": "scroll",
  "path": "Canvas/List",
  "globalObjectId": "GlobalObjectId_V1-...",
  "component": "UnityEngine.UI.ScrollRect",
  "componentGlobalObjectId": "GlobalObjectId_V1-...",
  "normalizedPosition": { "x": 0, "y": 1 }
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `delta` と `normalizedPosition` の両方欠落、サポート外の `backend`、または値が不正 |
| 403 | Play Mode カテゴリが無効 |
| 404 | ターゲットまたはシーンが見つからない |
| 409 | Play モードでない、アクティブな `EventSystem` がない、またはターゲットが非アクティブ |
| 422 | ターゲットが Unity UI の `ScrollRect` に解決されない |

---

## POST /api/playmode/ui/value

Unity UI の `Toggle`、`Slider`、`Dropdown`、または TextMeshPro の `TMP_Dropdown` に意味的な値を設定します。

> Play モード中かつ Play Mode カテゴリが有効な場合のみ呼び出せます。

### リクエストボディ(JSON)

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/MusicToggle" },
  "value": true
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `target` | ✅ | GameObject、`Toggle`、`Slider`、`Dropdown`、または `TMP_Dropdown` コンポーネントに解決されるオブジェクト参照 |
| `value` | ✅ | `Toggle` は真偽値、`Slider` は数値、`Dropdown` / `TMP_Dropdown` は整数のオプションインデックス |
| `backend` | ❌ | `unityUi`(既定) |
| `scenePath` | ❌ | `hierarchyPath` / `componentPath` ターゲット用のロード済みシーンセレクタ |

`[minValue, maxValue]` の範囲外の Slider 値は範囲内にクランプされ、レスポンスに
`"clamped": true` が含まれます。範囲外の Dropdown オプションインデックスは 400 で拒否されます。

### レスポンス

```json
{
  "success": true,
  "backend": "unityUi",
  "action": "value",
  "path": "Canvas/MusicToggle",
  "globalObjectId": "GlobalObjectId_V1-...",
  "component": "UnityEngine.UI.Toggle",
  "componentGlobalObjectId": "GlobalObjectId_V1-...",
  "value": true
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `value` の欠落・不正、サポート外の `backend`、または不正な `target` |
| 403 | Play Mode カテゴリが無効 |
| 404 | ターゲットまたはシーンが見つからない |
| 409 | Play モードでない、アクティブな `EventSystem` がない、またはターゲットが操作不可 |
| 422 | ターゲットがサポート対象の Unity UI 値コンポーネントに解決されない |
