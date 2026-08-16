# API リファレンス — GameObjects & Components

[English](gameobjects.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](gameobjects.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`。実際の URL は接続時に `<project>/.unionair/endpoint.txt` から読み取ってください。エンドポイントの発見手順、レスポンスの規約、カテゴリ/セキュリティの注意事項は [API リファレンス索引](../api-reference.ja.md) を参照してください。

このページのシェル例は `BASE_URL="$(tr -d '\r\n' < .unionair/endpoint.txt)"` を前提としています。`${BASE_URL}` は末尾の `/api/` まで含みます。

---

## GET /api/gameobjects

指定した GameObject の詳細情報(コンポーネントを含む)を返します。
`scenePath` を省略した場合はアクティブシーンが使用されます。

### クエリパラメータ

| パラメータ | 必須 | 説明 |
|-------------|------|------|
| `target` | ✅ | オブジェクト参照。GameObject に解決される必要があります |
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

### レスポンス

```json
{
  "name": "Button",
  "path": "Canvas/Panel/Button",
  "globalObjectId": "GlobalObjectId_V1-...",
  "isActive": true,
  "tag": "Untagged",
  "layer": 5,
  "transform": { ... },
  "components": [
    {
      "type": "UnityEngine.RectTransform",
      "globalObjectId": "GlobalObjectId_V1-...",
      "properties": {
        "m_LocalPosition": { "x": 0, "y": 0, "z": 0 },
        "m_LocalScale":    { "x": 1, "y": 1, "z": 1 }
      }
    },
    {
      "type": "UnityEngine.UI.Button",
      "enabled": true,
      "properties": {
        "m_Interactable": true,
        "m_Transition": 1
      }
    }
  ]
}
```

`components[].enabled` は、コンポーネントの Inspector ヘッダにあるチェックボックスです。チェックボックスを持たないコンポーネント（`Transform` や `MeshFilter`）では**フィールドごと省略**されるため、「無効になっている」と「無効にできない」を読み手が区別できます。これは `properties` の一つではありません。Unity はこれをコンポーネント本体の外に描画し、`properties` は本体が描画するものだけを載せるためです。書き込みは `PATCH /api/gameobjects/components` の `enabled` フィールドを使います。

`components[].blendShapeNames` は、`SkinnedMeshRenderer` のメッシュが持つブレンドシェイプの名前を、メッシュ上の順序で並べたものです。それ以外のコンポーネント型では**フィールドごと省略**され、ブレンドシェイプを持たないメッシュのレンダラーでは空配列、メッシュが未設定のレンダラーでも空配列になります(この2つは `m_Mesh` で区別できます)。`enabled` と同様に `properties` の中ではなく隣に置かれます。シェイプ名は `Mesh` に属するものであり、レンダラーのシリアライズされたフィールドではないためです。

この配列は位置で対応します。インデックス *i* は `properties.m_BlendShapeWeights[i]` が動かすシェイプの名前であり、ウェイトの書き込みもこのインデックスで行います。名前は一意ではありません(Unity は同一メッシュ上の2つのシェイプに同じ名前を許します)。したがってインデックスが同一性であり、名前は説明です。また2つの配列は長さが異なることがあり、これは稀なケースではありません。名前はレンダラーが現在参照しているメッシュから読み取られ、ウェイトはコンポーネントにシリアライズされた内容から読み取られます。Unity はメッシュを割り当てた時点ではシリアライズされた配列をリサイズしません。6000.0.80f1 で計測したところ、シェイプを3つ持つメッシュを `SkinnedMeshRenderer` に割り当てた直後は、3つの名前と空の `m_BlendShapeWeights` が同時に報告されました。シェイプ数を減らしてメッシュを再インポートした場合も同じ理由で配列は長いままになります。どちらの配列も他方に合わせて補正されません。インデックスは防御的に扱ってください。

`components[].properties` は `SerializedObject` 経由で取得したプロパティです。
サポートされる `SerializedPropertyType`: `bool`、`int`、`float`、`string`、`Color`、`Vector2/3/4`、`Rect`、`ObjectReference`。配列は同じ型ルールに従う要素を持つ JSON 配列としてシリアライズされます。その他の型は `null` になります。

### オブジェクト参照は書き込みが読む綴りで返ります

参照の値は変換なしにそのまま `PATCH /api/gameobjects/components` へ送り返せます。コンポーネントに対する read-modify-write が成立するのはこのためです。

アセットの場合:

```json
"m_Mesh": {
  "assetGuid": "a1b2...",
  "assetPath": "Assets/Meshes/Rock.fbx",
  "assetType": "UnityEngine.Mesh"
}
```

ロード済みシーン内のオブジェクトの場合:

```json
"m_ProbeAnchor": {
  "type": "globalObjectId",
  "value": "GlobalObjectId_V1-2-..."
}
```

ここでの `type` はリクエストでの意味と同じ、すなわち**参照の種別**であって、オブジェクトのクラスではありません。クラスは `assetType` が担い、これを持つのはアセットだけです。`GET /api/assets/scriptableobjects/{guid}` がアセット参照に使う綴りと同一なので、2 つの読み取りは一致します。

説明ではなく Unity 自身の識別子を使うことから、2 点が導かれます。

- **表示名はありません。** 書き込みのどのフィールドも名前を運ばないため、これを載せると書き込みが再びその値を拒否するか、無視するキーを受け入れるかのどちらかになります。アセットは `assetPath` が名前を兼ね、シーンオブジェクトは解決して名前を得ます。
- **一度も保存されていないシーンのオブジェクトは指定できません。** Unity がそのオブジェクトの `GlobalObjectId` を持たず、何も解決しない null id `GlobalObjectId_V1-0-0000...-0-0` を返すためです。これは識別子側の性質であってこのエンドポイントの都合ではありません。シーンを保存すれば通常どおり指定できます。

**Unity 組み込みリソース**への参照(プリミティブのメッシュ、`Library/unity default resources`)は他のアセットと同様に GUID と path を返しますが、送り返すと `404` になります。これらは GUID **と** file ID の組で指定されるもので、書き込み語彙に file ID がないためです。読み取りは可能、書き込みは到達不能です。

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `target` の欠落または不正な形式 |
| 404 | `target` に対応する GameObject が存在しない |
| 422 | `target` が GameObject に解決されない |

---

## GET /api/search/gameobjects

複数の AND 条件でシーン内の GameObject を検索します。すべてのパラメータは任意です。`scenePath` を省略した場合はアクティブシーンが使用されます。

### クエリパラメータ

| パラメータ | 型 | 説明 |
|-------------|-----|------|
| `scenePath` | string | ロード済みシーンのアセットパス、または一意に定まるシーン名 |
| `name` | string | 名前に対する大文字小文字を区別しない部分一致 |
| `component` | string | コンポーネント型名の部分一致(例: `Camera`、`MeshRenderer`) |
| `tag` | string | タグの完全一致 |
| `layer` | int | レイヤー番号 |
| `active` | bool | `true`/`false`(省略 = 両方) |
| `assetGuid` | string | 指定 GUID のアセットをいずれかのコンポーネントから参照している |
| `includeComponents` | bool | `true` の場合、各 GameObject のコンポーネント型名一覧を含めます(既定: `false`) |

### レスポンス

```json
{
  "count": 2,
  "gameObjects": [
    {
      "name": "Main Camera",
      "path": "Main Camera",
      "globalObjectId": "GlobalObjectId_V1-...",
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

### 例

```bash
# 名前に "Enemy" を含む GameObject
curl "${BASE_URL}search/gameobjects?name=Enemy"

# Camera コンポーネントを持つ GameObject(コンポーネント一覧付き)
curl "${BASE_URL}search/gameobjects?component=Camera&includeComponents=true"

# 特定アセットを参照 + 非アクティブのみ
curl "${BASE_URL}search/gameobjects?assetGuid=abc123&active=false"
```

---

## Scene Write カテゴリ — 共通の注意事項

> **セキュリティ:** 書き込みエンドポイントは既定で**無効**です。
> **Window > UnionAir > REST Bridge** のトグルで有効化してください。
> Edit モードでの書き込み操作は Unity Editor の Undo(Ctrl+Z)で取り消せます。
> GameObject / Component の書き込みエンドポイントは、EditorWindow で **Allow Play Mode Scene Changes** が有効かつリクエストの JSON ボディまたはクエリ文字列に `allowWhilePlaying=true` が含まれない限り、Play モード中は `409 Conflict` を返します。`POST` と `PATCH` ではボディの値がクエリ文字列より優先されます。
> Play モードでのシーンオブジェクト書き込みは一時的なランタイム変更であり、シーンを dirty にせず、Undo 操作も登録しません。

---

## POST /api/gameobjects

シーンに新しい空の GameObject を作成します。
`scenePath` を省略した場合はアクティブシーンが使用されます。

### リクエストボディ(JSON)

```json
{
  "name": "MyObject",
  "parent": { "type": "hierarchyPath", "value": "Canvas" },
  "scenePath": "Assets/Scenes/Level_A.unity"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `name` | ✅ | 作成する GameObject の名前 |
| `parent` | ❌ | 親 GameObject に解決されるオブジェクト参照。省略時はシーンルートに配置 |
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

### レスポンス

```json
{
  "path": "Canvas/MyObject",
  "name": "MyObject",
  "globalObjectId": "GlobalObjectId_V1-..."
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `name` の欠落 |
| 404 | `parent` が存在しない |
| 422 | `parent` が GameObject に解決されない |
| 403 | Scene Write カテゴリが無効 |

---

## POST /api/gameobjects/primitive

プリミティブ型の GameObject を作成します。
`scenePath` を省略した場合はアクティブシーンが使用されます。

### リクエストボディ(JSON)

```json
{
  "type": "Cube",
  "name": "MyCube",
  "parent": { "type": "hierarchyPath", "value": "Stage" },
  "scenePath": "Assets/Scenes/Level_A.unity"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `type` | ✅ | `Cube` \| `Sphere` \| `Capsule` \| `Cylinder` \| `Plane` \| `Quad` |
| `name` | ❌ | 省略時は型名がそのまま使用されます |
| `parent` | ❌ | 親 GameObject に解決されるオブジェクト参照。省略時はシーンルートに配置 |
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

### レスポンス

```json
{
  "path": "Stage/MyCube",
  "name": "MyCube",
  "globalObjectId": "GlobalObjectId_V1-..."
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `type` の欠落または不正 |
| 404 | `parent` が存在しない |
| 422 | `parent` が GameObject に解決されない |
| 403 | Scene Write カテゴリが無効 |

---

## POST /api/gameobjects/instantiate

プレハブアセットを、プレハブとの接続を保ったままアクティブシーンにインスタンス化します。
`scenePath` を省略した場合はアクティブシーンが使用されます。

### リクエストボディ(JSON)

```json
{
  "guid": "a1b2c3...",
  "assetPath": "Assets/Prefabs/Player.prefab",
  "name": "PlayerInstance",
  "parent": { "type": "hierarchyPath", "value": "Stage" },
  "scenePath": "Assets/Scenes/Level_A.unity"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `guid` | 条件付き | プレハブアセットの GUID。両方指定した場合は `assetPath` より優先 |
| `assetPath` | 条件付き | プレハブアセットのパス。`guid` を省略した場合は必須 |
| `name` | ❌ | 作成されるインスタンスの名前(任意) |
| `parent` | ❌ | 親 GameObject に解決されるオブジェクト参照。省略時はシーンルート |
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

### レスポンス

```json
{
  "name": "PlayerInstance",
  "path": "Stage/PlayerInstance",
  "globalObjectId": "GlobalObjectId_V1-...",
  "prefabAssetPath": "Assets/Prefabs/Player.prefab",
  "components": ["Transform", "MeshRenderer"]
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `guid` と `assetPath` の両方が欠落、またはアセットがプレハブ/GameObject でない |
| 404 | `guid` または `parent` が存在しない |
| 422 | `parent` が GameObject に解決されない |
| 403 | Scene Write カテゴリが無効 |

---

## DELETE /api/gameobjects

指定した GameObject をシーンから削除します。
`scenePath` を省略した場合はアクティブシーンが使用されます。

### クエリパラメータ

| パラメータ | 必須 | 説明 |
|-------------|------|------|
| `target` | ✅ | GameObject に解決されるオブジェクト参照 |
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

### レスポンス

```json
{ "deleted": "Canvas/MyObject", "globalObjectId": "GlobalObjectId_V1-..." }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `target` の欠落または不正な形式 |
| 404 | `target` が存在しない |
| 422 | `target` が GameObject に解決されない |
| 403 | Scene Write カテゴリが無効 |

---

## PATCH /api/gameobjects

指定した GameObject のプロパティを更新します。
`scenePath` を省略した場合はアクティブシーンが使用されます。

### クエリパラメータ

| パラメータ | 必須 | 説明 |
|-------------|------|------|
| `target` | ✅ | GameObject に解決されるオブジェクト参照 |
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

### リクエストボディ(JSON)

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

すべてのフィールドは任意です。省略したフィールドは変更されません。`transform` の各サブフィールドも同様に任意です。

### レスポンス

```json
{ "path": "Canvas/RenamedObject", "name": "RenamedObject", "globalObjectId": "GlobalObjectId_V1-..." }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `target` の欠落または不正な形式 |
| 404 | `target` が存在しない |
| 422 | `target` が GameObject に解決されない |
| 403 | Scene Write カテゴリが無効 |

---

## POST /api/gameobjects/duplicate

指定した GameObject を複製します。
`scenePath` を省略した場合はアクティブシーンが使用されます。

### クエリパラメータ

| パラメータ | 必須 | 説明 |
|-------------|------|------|
| `source` | ✅ | 複製元 GameObject に解決されるオブジェクト参照 |
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

### レスポンス

```json
{ "path": "Canvas/MyObject (1)", "name": "MyObject (1)", "globalObjectId": "GlobalObjectId_V1-..." }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `source` の欠落または不正な形式 |
| 404 | `source` が存在しない |
| 422 | `source` が GameObject に解決されない |
| 403 | Scene Write カテゴリが無効 |

---

## POST /api/gameobjects/reparent

GameObject を別の親に移動します。
`scenePath` を省略した場合はアクティブシーンが使用されます。

### リクエストボディ(JSON)

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/Panel/Button" },
  "parent": { "type": "hierarchyPath", "value": "Canvas/NewPanel" },
  "scenePath": "Assets/Scenes/Level_A.unity"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `target` | ✅ | 移動する GameObject に解決されるオブジェクト参照 |
| `parent` | ❌ | 新しい親に解決されるオブジェクト参照。省略時はシーンルートに移動 |
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

### レスポンス

```json
{ "path": "Canvas/NewPanel/Button", "globalObjectId": "GlobalObjectId_V1-..." }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `target` または `parent` が不正な形式 |
| 404 | `target` または `parent` が存在しない |
| 422 | `target` または `parent` が GameObject に解決されない |
| 403 | Scene Write カテゴリが無効 |

---

## POST /api/gameobjects/batch

複数の作成 / 更新 / 削除操作を、まとめて**単一の Undo グループ**として実行します。
`scenePath` を省略した場合はアクティブシーンが使用されます。トップレベルの `scenePath` はすべての操作に適用され、各操作は自身の `scenePath` で上書きできます。

### リクエストボディ(JSON)

```json
{
  "operations": [
    { "op": "create", "name": "EmptyGO", "parent": { "value": "Stage" } },
    { "op": "create_primitive", "type": "Cube", "name": "Cube_0", "transform": { "position": { "x": 0, "y": 0, "z": 0 } } },
    { "op": "update", "target": { "value": "Stage/OldObject" }, "isActive": false },
    { "op": "delete", "target": { "value": "Stage/Trash" } }
  ]
}
```

#### `op` の種類

| `op` | 必須フィールド | 任意フィールド |
|------|--------------|------------------|
| `create` | `name` | `parent`、`transform` |
| `create_primitive` | `type`(`Cube`\|`Sphere`\|`Capsule`\|`Cylinder`\|`Plane`\|`Quad`) | `name`、`parent`、`transform` |
| `update` | `target` | `name`、`isActive`、`tag`、`layer`、`transform` |
| `delete` | `target` | — |

すべての操作タイプは任意の `scenePath` も受け付けます。

`transform` の形式: `{"position":{"x":0,"y":0,"z":0},"rotation":{...},"scale":{...}}`

### レスポンス(HTTP 207)

```json
{
  "processed": 4,
  "failed": 1,
  "results": [
    { "index": 0, "success": true,  "path": "Stage/EmptyGO", "globalObjectId": "GlobalObjectId_V1-..." },
    { "index": 1, "success": true,  "path": "Cube_0", "globalObjectId": "GlobalObjectId_V1-..." },
    { "index": 2, "success": true,  "path": "Stage/OldObject", "globalObjectId": "GlobalObjectId_V1-..." },
    { "index": 3, "success": false, "error": "GameObject not found: Stage/Trash" }
  ]
}
```

1つの操作が失敗しても、残りの操作は続行されます。成功したすべての操作は単一の Undo グループにまとめられます。

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `operations` の欠落または空 |
| 403 | Scene Write カテゴリが無効 |

---

## POST /api/gameobjects/components

指定した GameObject にコンポーネントを追加します。
`scenePath` を省略した場合はアクティブシーンが使用されます。

### リクエストボディ(JSON)

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/MyObject" },
  "type": "UnityEngine.BoxCollider",
  "scenePath": "Assets/Scenes/Level_A.unity"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `target` | ✅ | GameObject に解決されるオブジェクト参照 |
| `type` | ✅ | 追加するコンポーネントの完全修飾型名 |
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

### レスポンス

```json
{
  "path": "Canvas/MyObject",
  "globalObjectId": "GlobalObjectId_V1-...",
  "type": "UnityEngine.BoxCollider",
  "componentGlobalObjectId": "GlobalObjectId_V1-..."
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `target` の欠落・不正な形式、または `type` の欠落 |
| 404 | `target` が存在しない |
| 422 | `target` が GameObject に解決されない、コンポーネント型を解決できない、またはコンポーネントの追加に失敗 |
| 403 | Scene Write カテゴリが無効 |

---

## DELETE /api/gameobjects/components

指定したコンポーネントを削除します。
`scenePath` を省略した場合はアクティブシーンが使用されます。

### クエリパラメータ

| パラメータ | 必須 | 説明 |
|-------------|------|------|
| `target` | ✅ | Component に解決されるオブジェクト参照。`componentPath`(例: `{"type":"componentPath","value":"Canvas/Button:Rigidbody"}`)または Component の `globalObjectId` を使用します。あるいは `hierarchyPath` で GameObject を指定し、`?type=ComponentName` を追加します |
| `type` | ❌ | 削除するコンポーネントの C# 型名。`target` が GameObject 参照(例: `hierarchyPath`)の場合は必須 |
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

### レスポンス

```json
{
  "deleted": "UnityEngine.BoxCollider",
  "from": "Canvas/MyObject",
  "globalObjectId": "GlobalObjectId_V1-...",
  "componentGlobalObjectId": "GlobalObjectId_V1-..."
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `target` の欠落・不正な形式、または `type` が未知のコンポーネント名 |
| 404 | `target` が存在しない、または指定 `type` のコンポーネントがターゲット上にない |
| 422 | `target` が GameObject に解決されたが `type` が指定されていない |
| 403 | Scene Write カテゴリが無効 |

---

## PATCH /api/gameobjects/components

指定したコンポーネントのシリアライズ済みプロパティ(オブジェクト参照フィールドを含む)を更新します。
`scenePath` を省略した場合はアクティブシーンが使用されます。

### クエリパラメータ

| パラメータ | 必須 | 説明 |
|-------------|------|------|
| `target` | ✅ | Component に解決されるオブジェクト参照。`componentPath`(例: `{"type":"componentPath","value":"Obj:MeshRenderer"}`)または Component の `globalObjectId` を使用します。あるいは `hierarchyPath` で GameObject を指定し、`?type=ComponentName` を追加します |
| `type` | ❌ | 更新するコンポーネントの C# 型名。`target` が GameObject 参照(例: `hierarchyPath`)の場合は必須 |
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

### リクエストボディ(JSON)

```json
{
  "properties": {
    "m_Intensity": 2.0,
    "m_Color": { "r": 1, "g": 0.9, "b": 0.8, "a": 1 },
    "target": { "type": "componentPath", "value": "Canvas/Button:UnityEngine.UI.Text", "scenePath": "Assets/Scenes/Level_A.unity" },
    "textAsset": { "assetPath": "Assets/Data/config.txt", "assetType": "UnityEngine.TextAsset" },
    "optionalTarget": null
  },
  "enabled": false
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `properties` | 条件付き | 書き込むシリアライズプロパティ。`enabled` を省略する場合は必須 |
| `enabled` | 条件付き | Inspector ヘッダのチェックボックス。`properties` を省略する場合は必須 |

どちらか一方、または両方を送ります。どちらも無いボディは `400` です。

`enabled` が `properties` のキーではなく独立したフィールドなのは、チェックボックスがこのエンドポイントの扱えるプロパティではないためです。Unity はこれをコンポーネント本体ではなくヘッダに描画し、`properties` は本体が描画するものにしか届きません。`m_Enabled` をプロパティキーとして送った場合は `400` になり、その旨を返します。チェックボックスを持たないコンポーネント（`Transform`、`MeshFilter`）には有効状態が無いため、`enabled` は型名を挙げて `400` になります。

`properties` の各キーは `SerializedProperty.propertyPath` です。互換性のため、トップレベルのフィールド名も引き続き受け付けます。

各キーは一意で、このエンドポイントが書き込めるプロパティを指している必要があります。読み取られるのは `properties` のトップレベルのキーだけで、他のプロパティの値の内部に現れる名前はその値の一部であり、書き込み要求ではありません。重複キー、何も指していないキー、書き込めないものを指すキー、値の形が合っていないキーは、読み飛ばされるのではなく `400` となり、どのキーがなぜ拒否されたかを返します。したがって `updated` はリクエストが送ったすべてのキーを必ず列挙し、`200` はリクエスト全体が適用されたことを意味します。空の `properties` オブジェクトは受理され、何も更新しません。

ネストされたジェネリック型と `m_Script` は、このエンドポイントが書き込めないプロパティに含まれます。これらを送ることは無視される操作ではなくエラーです。

配列は次の3つのいずれかの方法で書き込みます。いずれも Unity 自身が生成する `SerializedProperty.propertyPath` の表記です。

| キー | 効果 |
|------|------|
| `m_Materials` | 配列を置き換え、JSON 配列の長さにリサイズします |
| `m_Materials.Array.data[0]` | 要素を1つ書き込み、長さは変更しません |
| `m_Materials.Array.size` | リサイズのみ |

```json
{
  "properties": {
    "m_Materials": [
      { "assetPath": "Assets/Materials/Brick.mat", "assetType": "UnityEngine.Material" },
      null
    ]
  }
}
```

要素の値は、そのシリアライズ型のトップレベルプロパティと同じ形を取ります。したがって `null`、スカラー、`{r,g,b,a}`、`{x,y,…}`、および後述のすべてのオブジェクト参照形式が配列の中でも同様に使えます。適用できない要素は配列のキーではなく要素自身のアドレスで報告されるため、`{"m_Materials": [null, 5]}` は `m_Materials.Array.data[1]` を挙げます。

配列の置き換えはマージではなく置換です。Unity は要素ごとの同一性を保持しないため、JSON 配列の長さがそのまま配列の長さになります。要素アドレスはリサイズを行わず、範囲外のインデックスは現在の長さを挙げて `400` になります。

配列を拡張したときの新しい要素は、Unity と同じく最後の要素のコピーで埋められます（クリアされません）。1,000,000 を超える長さは `400` になります。これは Unity がサポートする長さについての主張ではありません。`Array.size` はリクエスト本文でコストを支払わない唯一の書き込みであり、この上限が無いと打ち間違えた長さがそのまま Editor に確保を要求します。

1つのリクエストで配列の長さと要素の両方を書き込むことはできません。2つの要素アドレスは独立した2つの書き込みとして受理されますが、それらと並んで長さを指定した場合は — `m_Materials` と `m_Materials.Array.size` のどちらの綴りであっても — `400` になります。どちらが先に適用されるかは、このエンドポイントが呼び出し側に代わって決める問題ではないためです。

要素がこのエンドポイントの書き込めないシリアライズ型である配列（シリアライズ可能な構造体の `List<T>` など）は、3つのアドレスすべてで拒否されます。読み取りはそうした要素を `null` としてシリアライズするため、置き換えや削除は呼び出し側が見たことのない内容を破壊することになります。拡張だけは安全ですが、片方向のリサイズだけが通る契約を残すより、まとめて拒否します。

これ以外の形で配列の内部に届くキーは、名前を挙げて拒否されます。`m_Materials.Array.data[0].name` は要素のサブパスであり、要素の中のフィールドへの書き込みはサポートしていません。

Color および Vector オブジェクトは部分更新です。省略したメンバーは現在値を保持します。サポートされるメンバーを少なくとも1つ指定し、指定した各メンバーを JSON 数値にする必要があります。不明または重複したメンバーは拒否されます。

サポートされるオブジェクト参照値:

| 形式 | 説明 |
|------|-------------|
| `null` | 参照をクリア |
| `{ "type": "globalObjectId", "value": "GlobalObjectId_V1-..." }` | シーンの GameObject または Component を GlobalObjectId で割り当て |
| `{ "type": "hierarchyPath", "value": "Canvas/Button" }` | シーンの GameObject を割り当て |
| `{ "type": "componentPath", "value": "Canvas/Button:UnityEngine.UI.Text" }` | シーンの GameObject 上のコンポーネントを割り当て |
| `{ "type": "hierarchyPath", "value": "Canvas/Button", "scenePath": "Assets/Scenes/Level_A.unity" }` | ロード済みシーンの GameObject を割り当て |
| `{ "assetGuid": "...", "assetType": "UnityEngine.TextAsset" }` | GUID でアセットを割り当て |
| `{ "assetPath": "Assets/Data/config.txt", "assetType": "UnityEngine.TextAsset" }` | パスでアセットを割り当て |

`assetType` はアセット参照では任意です。指定した場合、`UnityEngine.Object` 型に解決される必要があり、解決されたオブジェクトはその型とシリアライズ済みフィールドの型の両方に代入可能である必要があります。
オブジェクト参照オブジェクトが受け付けるのは、上記の対応形式に示したメンバーだけです。不明または重複したメンバーは拒否されます。

### レスポンス

```json
{
  "path": "Directional Light",
  "globalObjectId": "GlobalObjectId_V1-...",
  "component": "UnityEngine.Light",
  "componentGlobalObjectId": "GlobalObjectId_V1-...",
  "enabled": false,
  "updated": ["m_Intensity", "m_Color"]
}
```

`enabled` は、リクエストが設定したかどうかに関わらず書き込み後の状態を返し、有効状態を持たないコンポーネントでは省略されます。`updated` が列挙するのは `properties` のキーだけです。

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `target` の欠落・不正な形式、`type` が未知のコンポーネント名、またはボディに `properties` と `enabled` のどちらも無い |
| 400 | `enabled` が JSON の真偽値でない、または対象のコンポーネント型が有効状態を持たない |
| 400 | オブジェクト参照ペイロードが不正、不明または重複したメンバーを含む、または要求された型を解決できない |
| 400 | `properties` のキーが、コンポーネント上のどのシリアライズプロパティも指していない |
| 400 | `properties` のキー、または Color / Vector / オブジェクト参照値のメンバーが重複している |
| 400 | キーが、このエンドポイントでは書き込めないプロパティを指している（ネストされたジェネリック型、`m_Script`、書き込めない要素を持つ配列、書き込み未対応のシリアライズ型） |
| 400 | キーが `name.Array.data[i]` および `name.Array.size` 以外の形で配列の内部に届いている |
| 400 | 要素のインデックスが配列の範囲外、または `Array.size` が負 |
| 400 | 1つのリクエストが配列の長さと要素の両方を書き込もうとしている |
| 400 | 値がプロパティの受け取る形と一致しない（数値を文字列で送る、ベクトルをスカラーで送るなど） |
| 400 | キーの値が正しい JSON になっていない |
| 404 | GameObject、コンポーネント、またはアセットが存在しない |
| 422 | `target` が GameObject に解決されたが `type` が指定されていない。または解決されたオブジェクトが要求された型やフィールド型に代入できない |
| 403 | Scene Write カテゴリが無効 |
