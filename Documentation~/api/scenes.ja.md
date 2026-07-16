# API リファレンス — Scenes

[English](scenes.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](scenes.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`(デフォルトポート: **8765**)。レスポンスの規約とカテゴリ/セキュリティの注意事項は [API リファレンス索引](../api-reference.ja.md) を参照してください。

---

## GET /api/scene

ロード済みシーンのメタデータを返します。`scenePath` を省略した場合はアクティブシーンが使用されます。

### クエリパラメータ

| パラメータ | 既定値 | 説明 |
|-------------|-----------|------|
| `scenePath` | アクティブシーン | `Assets/Scenes/Level_A.unity` のようなロード済みシーンのアセットパス。シーン名は一意に定まる場合のみ受け付けます。 |

### レスポンス

```json
{
  "name": "SampleScene",
  "path": "Assets/Scenes/SampleScene.unity",
  "guid": "a1b2c3...",
  "isDirty": false,
  "isLoaded": true,
  "rootCount": 4
}
```

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `name` | string | シーン名 |
| `path` | string | Assets/ 配下のパス |
| `guid` | string | シーンアセットの GUID |
| `isDirty` | bool | 未保存の変更があるかどうか |
| `isLoaded` | bool | シーンがロードされているかどうか |
| `rootCount` | int | ルート GameObject の数 |

---

## GET /api/scene/hierarchy

シーン全体の GameObject ツリーを返します。`scenePath` を省略した場合はアクティブシーンが使用されます。

### クエリパラメータ

| パラメータ | 既定値 | 説明 |
|-------------|-----------|------|
| `scenePath` | アクティブシーン | `Assets/Scenes/Level_A.unity` のようなロード済みシーンのアセットパス。シーン名は一意に定まる場合のみ受け付けます。 |
| `depth` | 無制限 | 最大再帰深度 |
| `compact` | `false` | `true` の場合、transform/tag/layer の詳細を省略し、子の数を含めます |
| `limit` | `500` | 返される GameObject の最大数 |
| `path` | シーンルート | サブツリーのルートパス(任意) |

### レスポンス

```json
{
  "scene": "SampleScene",
  "objects": [ <GameObjectNode>, ... ],
  "totalReturned": 42,
  "truncated": false
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

#### トップレベルのレスポンスフィールド

| フィールド | 型 | 説明 |
|-------------|------|------|
| `scene` | string | 解決されたシーンの名前 |
| `totalReturned` | int | `objects` に実際に含まれた GameObject の数 |
| `truncated` | bool | `limit` パラメータにより結果が打ち切られた場合に `true` |

#### GameObjectNode のフィールド

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `name` | string | GameObject 名 |
| `path` | string | ルートからの `/` 区切りパス |
| `globalObjectId` | string | GameObject の安定的な Unity GlobalObjectId |
| `isActive` | bool | `activeInHierarchy`(親を含む) |
| `tag` | string | タグ |
| `layer` | int | レイヤー番号 |
| `transform` | object | ローカル座標系での position / rotation(EulerAngles)/ scale |
| `children` | array | 子 GameObjectNode の配列(再帰) |

---

## GET /api/scenes

ロード済みのすべてのシーンを一覧し、アクティブシーンを示します。

### レスポンス

```json
{
  "activeScene": "Assets/Scenes/Main.unity",
  "scenes": [
    {
      "name": "Main",
      "path": "Assets/Scenes/Main.unity",
      "guid": "a1b2c3...",
      "buildIndex": 0,
      "isDirty": false,
      "isLoaded": true,
      "isActive": true,
      "rootCount": 4
    }
  ],
  "count": 1
}
```

---

## POST /api/scenes/new

新しいシーンを作成します。

> Scene Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{
  "mode": "single",
  "setup": "default",
  "discardUnsaved": false
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `mode` | ❌ | `single` または `additive`。既定は `single` |
| `setup` | ❌ | `default` または `empty`。既定は `default` |
| `discardUnsaved` | ❌ | `single` モードで、ロード済みシーンのいずれかが dirty の場合に `true` の指定が必要 |

### レスポンス

```json
{
  "created": {
    "name": "Untitled",
    "path": "",
    "buildIndex": -1,
    "isDirty": false,
    "isLoaded": true,
    "isActive": true,
    "rootCount": 2
  }
}
```

---

## POST /api/scenes/open

既存のシーンアセットを開きます。

> Scene Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{
  "path": "Assets/Scenes/Level_A.unity",
  "mode": "additive",
  "discardUnsaved": false
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `path` | ✅ | シーンアセットのパス |
| `mode` | ❌ | `single` または `additive`。既定は `single` |
| `discardUnsaved` | ❌ | `single` モードで、ロード済みシーンのいずれかが dirty の場合に `true` の指定が必要 |

---

## POST /api/scenes/unload

ロード済みシーンをアンロードします。

> Scene Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{
  "path": "Assets/Scenes/Level_A.unity",
  "discardUnsaved": false
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `path` | 条件付き | ロード済みシーンのアセットパス。`name` を省略した場合は必須 |
| `name` | 条件付き | ロード済みシーンの名前。一意に定まる場合のみ受け付けます |
| `discardUnsaved` | ❌ | 対象シーンが dirty の場合に `true` の指定が必要 |

---

## POST /api/scenes/active

アクティブシーンを設定します。

> Scene Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は、Editor 側の **Allow Play Mode Scene Changes** 設定と、ボディまたはクエリ文字列での `allowWhilePlaying=true` が必要です。

### リクエストボディ(JSON)

```json
{
  "path": "Assets/Scenes/Level_A.unity"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `path` | 条件付き | ロード済みシーンのアセットパス。`name` を省略した場合は必須 |
| `name` | 条件付き | ロード済みシーンの名前。一意に定まる場合のみ受け付けます |
| `allowWhilePlaying` | ❌ | Editor 側の設定を有効にしたうえで、Play モード中に呼び出す場合に `true` の指定が必要 |

---

## GET /api/scene/stats

ロード済みシーンの集計統計を返します。`scenePath` を省略した場合はアクティブシーンが使用されます。

### クエリパラメータ

| パラメータ | 既定値 | 説明 |
|-------------|-----------|------|
| `scenePath` | アクティブシーン | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

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

> `Transform` / `RectTransform` はノイズになるため `componentCounts` から除外されます。
> `layerCounts` のキーはレイヤー名です(名前のないレイヤーは数値 ID)。

---

## POST /api/scene/save

現在のシーンをディスクに保存します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### レスポンス

```json
{ "saved": true, "path": "Assets/Scenes/SampleScene.unity" }
```
