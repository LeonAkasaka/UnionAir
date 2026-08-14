# API リファレンス — Assets

[English](assets.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](assets.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`。実際の URL は接続時に `<project>/.unionair/endpoint.txt` から読み取ってください。エンドポイントの発見手順、レスポンスの規約、カテゴリ/セキュリティの注意事項は [API リファレンス索引](../api-reference.ja.md) を参照してください。

---

## GET /api/assets

プロジェクト内のアセット一覧を返します。

### クエリパラメータ

| パラメータ | 必須 | 説明 |
|-------------|------|------|
| `path` | ❌ | 検索対象フォルダ(例: `Assets/UI`)。省略時は `Assets/` ツリー全体 |
| `type` | ❌ | アセット型名(例: `Texture2D`、`Material`、`Scene`) |
| `search` | ❌ | `AssetDatabase.FindAssets` に渡す追加フィルタ文字列 |

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

> 最大 **500 件**を返します。`total` が 500 を超える場合はフィルタを絞ってください。

---

## GET /api/assets/{guid}

GUID で指定したアセットの詳細情報を返します。

### パスパラメータ

| パラメータ | 説明 |
|-------------|------|
| `guid` | `AssetDatabase` 用の GUID 文字列 |

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
| `path` | string | Assets/ 配下のパス |
| `type` | string | 完全修飾型名 |
| `dependencies` | string[] | 直接依存するアセットのパス(`GetDependencies(recursive: false)`) |
| `labels` | string[] | アセットラベル |

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | GUID が空 |
| 404 | 一致するアセットが存在しない |

---

## GET /api/search/asset-refs

シーン内のコンポーネントが特定のアセットを参照している箇所を一覧します。`scenePath` を省略した場合はアクティブシーンが使用されます。

### クエリパラメータ

| パラメータ | 必須 | 説明 |
|-------------|------|------|
| `guid` | ✅ | 検索対象アセットの GUID |
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

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
      "gameObjectGlobalObjectId": "GlobalObjectId_V1-...",
      "componentType": "UnityEngine.MeshRenderer",
      "componentGlobalObjectId": "GlobalObjectId_V1-...",
      "propertyName": "m_Materials"
    }
  ],
  "count": 1
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `guid` の欠落 |
| 404 | 一致するアセットが存在しない |

> **注**: シーン内のすべての GameObject の全コンポーネントを `SerializedObject` でスキャンします。大きなシーンでは処理に時間がかかることがあります。

---

## GET /api/assets/dependents

指定したアセットに依存しているアセット(逆依存)を返します。

### クエリパラメータ

| パラメータ | 必須 | 説明 |
|-------------|------|------|
| `guid` | ✅ | 依存されているアセットの GUID |

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
| 400 | `guid` の欠落 |
| 404 | 一致するアセットが存在しない |

> **注**: `Assets/` 配下のすべてのアセットに対して `GetDependencies()` を呼び出します。アセット数が多い場合は処理に時間がかかることがあります。

---

## POST /api/assets/prefabs

シーン内の GameObject からプレハブを作成します。
`scenePath` を省略した場合はアクティブシーンが使用されます。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{
  "source": { "type": "hierarchyPath", "value": "Stage/Player" },
  "assetPath": "Assets/Prefabs/Player.prefab",
  "mode": "new",
  "scenePath": "Assets/Scenes/Level_A.unity"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `source` | ✅ | 元になる GameObject に解決されるオブジェクト参照 |
| `assetPath` | ✅ | 出力先アセットパス(`Assets/` で始まる `.prefab` ファイル) |
| `mode` | ✅ | `new`(インスタンスを接続しつつ作成)または `replace`(既存プレハブを上書き) |
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

### レスポンス

```json
{
  "assetPath": "Assets/Prefabs/Player.prefab",
  "guid": "a1b2c3...",
  "sourceGlobalObjectId": "GlobalObjectId_V1-..."
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | 必須フィールドの欠落、または `mode` が不正 |
| 404 | `source` が存在しない |
| 422 | `source` が GameObject に解決されない |
| 403 | Asset Write カテゴリが無効 |

---

## POST /api/assets/prefabs/apply

プレハブインスタンスのオーバーライドをプレハブアセットに適用します。
`scenePath` を省略した場合はアクティブシーンが使用されます。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{ "source": { "type": "hierarchyPath", "value": "Stage/Player" }, "scenePath": "Assets/Scenes/Level_A.unity" }
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `source` | ✅ | プレハブインスタンスに解決されるオブジェクト参照 |
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

### レスポンス

```json
{
  "applied": "Stage/Player",
  "globalObjectId": "GlobalObjectId_V1-...",
  "prefabAssetPath": "Assets/Prefabs/Player.prefab"
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `source` の欠落・不正な形式、またはオブジェクトがプレハブインスタンスでない |
| 404 | `source` が存在しない |
| 422 | `source` が GameObject に解決されない |
| 403 | Asset Write カテゴリが無効 |

---

## POST /api/assets/prefabs/revert

プレハブインスタンスをプレハブアセットの状態に戻します。
`scenePath` を省略した場合はアクティブシーンが使用されます。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{ "source": { "type": "hierarchyPath", "value": "Stage/Player" }, "scenePath": "Assets/Scenes/Level_A.unity" }
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `source` | ✅ | プレハブインスタンスに解決されるオブジェクト参照 |
| `scenePath` | ❌ | ロード済みシーンのアセットパス、または一意に定まるシーン名 |

### レスポンス

```json
{
  "reverted": "Stage/Player",
  "globalObjectId": "GlobalObjectId_V1-...",
  "prefabAssetPath": "Assets/Prefabs/Player.prefab"
}
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `source` の欠落・不正な形式、またはオブジェクトがプレハブインスタンスでない |
| 404 | `source` が存在しない |
| 422 | `source` が GameObject に解決されない |
| 403 | Asset Write カテゴリが無効 |

---

## POST /api/assets/materials

新しいマテリアルアセットを作成します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{
  "assetPath": "Assets/Materials/MyMat.mat",
  "shader": "Universal Render Pipeline/Lit"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `assetPath` | ✅ | 出力先(`Assets/` で始まる `.mat` ファイル) |
| `shader` | ✅ | シェーダー名 |

### レスポンス

```json
{ "guid": "d4e5f6...", "assetPath": "Assets/Materials/MyMat.mat" }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | 必須フィールドの欠落 |
| 422 | シェーダーが見つからない |
| 403 | Asset Write カテゴリが無効 |

---

## PATCH /api/assets/materials

マテリアルのプロパティを更新します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### クエリパラメータ

| パラメータ | 必須 | 説明 |
|-------------|------|------|
| `guid` | ✅ | 対象マテリアルの GUID |

### リクエストボディ(JSON)

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
| 400 | `guid` の欠落 |
| 404 | 一致するマテリアルが存在しない |
| 403 | Asset Write カテゴリが無効 |

---

## DELETE /api/assets/{guid}

アセットとその `.meta` ファイルを削除します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。
> 対象が読み込み中のシーン、または読み込み中のシーンを含むフォルダーの場合は、何も削除せずに `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
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
| 404 | 一致するアセットが存在しない |
| 403 | Asset Write カテゴリが無効 |
| 409 | 対象が読み込み中のシーン、そのシーンを含むフォルダー、または Editor が Play モード中 |

dirty状態にかかわらず、読み込み中のシーンは拒否されます。UnionAir はシーンを自動的に保存、破棄、unloadしません。報告されたすべてのシーンを明示的にunloadしてから、削除を再試行してください。

```json
{
  "error": "Cannot delete loaded scenes. Unload them before retrying to avoid deleting the backing asset of an open scene.",
  "code": "loaded_scene_delete_blocked",
  "assetPath": "Assets/Scenes",
  "loadedScenes": [
    {
      "path": "Assets/Scenes/Level.unity",
      "name": "Level",
      "isDirty": true,
      "isActive": true
    }
  ]
}
```

---

## POST /api/assets/move

アセットを移動/リネームします。GUID とプロジェクト内の参照は保持されます。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{
  "guid": "a1b2c3...",
  "newPath": "Assets/Textures/Renamed/icon.png"
}
```

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `guid` | ✅ | 移動するアセットの GUID |
| `newPath` | ✅ | 移動先パス(`Assets/` で始まる) |

### レスポンス

```json
{ "guid": "a1b2c3...", "newPath": "Assets/Textures/Renamed/icon.png" }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `guid` または `newPath` の欠落 |
| 404 | 一致するアセットが存在しない |
| 422 | 移動操作に失敗(パスの重複など) |
| 403 | Asset Write カテゴリが無効 |

---

## POST /api/assets/open

`AssetDatabase.OpenAsset()` を使ってアセットを Unity Editor で開きます。

> Editor Actions カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。
> エンドポイントのリスクは `editorState` です。

### リクエストボディ(JSON)

```json
{
  "guid": "a1b2c3...",
  "assetPath": "Assets/Scripts/Foo.cs"
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `guid` | 条件付き | 開くアセットの GUID。両方指定した場合はこちらが優先 |
| `assetPath` | 条件付き | `Assets/` または `Packages/` 配下のプロジェクト相対パス。`guid` を省略した場合は必須。Unity がまだ GUID を割り当てていない既存ファイルもインポート可能 |

### レスポンス

```json
{
  "opened": true,
  "guid": "a1b2c3...",
  "assetPath": "Assets/Scripts/Foo.cs"
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `guid` と `assetPath` の両方が欠落 |
| 403 | Editor Actions カテゴリが無効 |
| 404 | 一致するアセットが存在しない |
| 409 | Unity Editor が Play モード中 |
| 422 | Unity はパスをインポートしたが、アセット GUID が登録されなかった |
| 422 | アセットを開けなかった |

---

## POST /api/assets/reimport

`AssetDatabase.ImportAsset()` を使ってプロジェクトアセットを1件再インポートします。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。
> 読み込み中の `.unity` シーンは再インポートできません。再インポートすると、
> 全 API 処理を停止させる対話的な Reload ダイアログを Unity が表示する可能性があります。
> 再試行する前にシーンをアンロードしてください。

### リクエストボディ(JSON)

```json
{
  "guid": "a1b2c3...",
  "recursive": false,
  "forceUpdate": false
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `guid` | 条件付き | 再インポートするアセットの GUID。両方指定した場合はこちらが優先 |
| `assetPath` | 条件付き | プロジェクトアセットのパス。`guid` を省略した場合は必須 |
| `recursive` | ❌ | `ImportAssetOptions.ImportRecursive` を追加 |
| `forceUpdate` | ❌ | `ImportAssetOptions.ForceUpdate` を追加 |

### レスポンス

```json
{
  "reimported": true,
  "guid": "a1b2c3...",
  "assetPath": "Assets/Textures/Icon.png",
  "isCompiling": false,
  "isUpdating": true
}
```

対象が読み込み中のシーンである場合、または `recursive: true` で読み込み中の
シーンを含むフォルダーを対象にした場合、`AssetDatabase.ImportAsset()` を
呼び出す前に `409 Conflict` を返します。

```json
{
  "error": "Cannot reimport loaded scenes. Unload them before retrying to avoid Unity's interactive Reload dialog.",
  "code": "loaded_scene_reimport_blocked",
  "assetPath": "Assets/Scenes",
  "loadedScenes": [
    {
      "path": "Assets/Scenes/Level.unity",
      "name": "Level",
      "isDirty": true,
      "isActive": true
    }
  ]
}
```

| 競合フィールド | 型 | 説明 |
|----------------|------|-------------|
| `code` | string | 固定値 `loaded_scene_reimport_blocked` |
| `assetPath` | string | リクエストから解決されたアセットまたはフォルダーのパス |
| `loadedScenes` | array | 要求されたインポートと競合する読み込み中シーン。Scene Manager の順序で返す |
| `loadedScenes[].path` | string | シーンアセットのパス |
| `loadedScenes[].name` | string | シーン名 |
| `loadedScenes[].isDirty` | bool | シーンに未保存の Editor 変更があるか |
| `loadedScenes[].isActive` | bool | アクティブシーンか |

clean なシーンでは、`POST /api/scenes/unload`、再インポート、
`POST /api/scenes/open` の順に呼び出します。dirty なシーンでは、
Editor 上の変更を保存するか、`discardUnsaved: true` でアンロードするかを
先に明示的に選択してください。reimport エンドポイントがシーンを自動的に
保存、アンロード、破棄することはありません。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `guid` と `assetPath` の両方が欠落 |
| 403 | Asset Write カテゴリが無効 |
| 404 | 一致するアセットが存在しない |
| 409 | Unity Editor が Play モード中、または読み込み中のシーンが1つ以上対象に含まれる |

---

## GET /api/assets/scriptableobjects

プロジェクト内の ScriptableObject アセットを一覧します。

> Read カテゴリが必要です(既定で有効)。

### クエリパラメータ

| パラメータ | 必須 | 説明 |
|-----------|----------|-------------|
| `type` | ❌ | 型名でフィルタ(例: `EnemyConfig`)。既定は `ScriptableObject`(すべての SO アセット) |
| `path` | ❌ | 検索対象をこのフォルダに限定(例: `Assets/Data`) |
| `search` | ❌ | `AssetDatabase.FindAssets` に渡す追加キーワード |

### レスポンス

```json
{
  "assets": [
    { "guid": "a1b2c3...", "path": "Assets/Data/EnemyConfig.asset", "type": "MyGame.EnemyConfig" }
  ],
  "total": 1,
  "returned": 1
}
```

1リクエストあたり最大 500 件を返します。

---

## GET /api/assets/scriptableobjects/{guid}

ScriptableObject アセットを、読み取り可能なすべてのシリアライズ済みプロパティとともに返します。

> Read カテゴリが必要です(既定で有効)。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | ScriptableObject アセットの GUID |

### レスポンス

```json
{
  "guid": "a1b2c3...",
  "path": "Assets/Data/EnemyConfig.asset",
  "type": "MyGame.EnemyConfig",
  "properties": {
    "health": 100,
    "speed": 3.5,
    "displayName": "Goblin",
    "primaryWeapon": { "assetGuid": "def456...", "assetPath": "Assets/Weapons/Sword.asset", "assetType": "MyGame.WeaponData" },
    "tags": ["fire", "aoe"]
  }
}
```

**プロパティのシリアライズ規則:**

| SerializedPropertyType | JSON 表現 |
|---|---|
| Boolean | `true` / `false` |
| Integer、Enum、LayerMask | 整数リテラル |
| Float | 浮動小数点リテラル(ラウンドトリップ形式) |
| String | JSON 文字列 |
| Color | `{"r":…,"g":…,"b":…,"a":…}` |
| Vector2 | `{"x":…,"y":…}` |
| Vector3 | `{"x":…,"y":…,"z":…}` |
| Vector4、Quaternion | `{"x":…,"y":…,"z":…,"w":…}` |
| Rect | `{"x":…,"y":…,"width":…,"height":…}` |
| Bounds | `{"center":{"x":…,"y":…,"z":…},"extents":{"x":…,"y":…,"z":…}}` |
| ObjectReference(アセット) | `{"assetGuid":…,"assetPath":…,"assetType":…}` |
| ObjectReference(null) | `null` |
| 配列 | 同じルールに従う要素を持つ JSON 配列 |
| ネストしたジェネリック型 | `null` |

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | GUID が空、またはアセットが ScriptableObject でない |
| 404 | 指定 GUID のアセットが見つからない |

---

## POST /api/assets/scriptableobjects

新しい ScriptableObject アセットを作成します。型は実行時にリフレクションで解決されるため、プロジェクト定義の任意の ScriptableObject サブクラスをサポートします — パッケージの変更は不要です。

`properties` を指定した場合、PATCH と同じオール・オア・ナッシングの検証に従います。各キーは一意で、書き込み可能なシリアライズ済みプロパティを指し、互換性のある JSON 値を持つ必要があります。拒否されたリクエストはアセットを作成しません。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{
  "typeName": "MyGame.EnemyConfig",
  "assetPath": "Assets/Data/Enemies/Goblin.asset",
  "properties": {
    "health": 100,
    "speed": 3.5
  }
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `typeName` | ✅ | ScriptableObject サブクラスの完全修飾または単純型名 |
| `assetPath` | ✅ | 出力先パス(`Assets/` で始まり `.asset` で終わる必要があります) |
| `properties` | ❌ | 初期プロパティ値(PATCH と同じ形式) |

### レスポンス(HTTP 201)

```json
{
  "guid": "a1b2c3...",
  "assetPath": "Assets/Data/Enemies/Goblin.asset",
  "type": "MyGame.EnemyConfig",
  "updated": ["health", "speed"]
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | 必須フィールドの欠落、`assetPath` が `.asset` で終わらない・`Assets/` で始まらない、型が見つからない、型が ScriptableObject でない、または型が抽象クラス |
| 400 | 初期 `properties` のキーが重複している、書き込み可能なシリアライズ済みプロパティを指していない、または値の形が一致しない |
| 403 | Asset Write カテゴリが無効 |
| 409 | 指定パスにアセットが既に存在する、または Unity Editor が Play モード中 |

---

## PATCH /api/assets/scriptableobjects

既存の ScriptableObject アセットのシリアライズ済みプロパティを更新します。

`properties` の各キーは一意で、このエンドポイントが書き込めるプロパティを指している必要があります。読み取られるのはトップレベルのキーだけで、他のプロパティの値の内部に現れる名前はその値の一部であり、書き込み要求ではありません。重複キー、何も指していないキー、書き込めないものを指すキー、値の形が合っていないキーは `400` となり、どのキーがなぜ拒否されたかを返します。したがって `updated` はリクエストが送ったすべてのキーを必ず列挙します。ネストしたジェネリック型と `m_Script` は書き込めず、これらを送ることは無視される操作ではなくエラーです。空の `properties` オブジェクトは受理され、何も更新しません。

配列は、JSON 配列として全体を書き込む(`"tags": ["fire", "aoe"]`)か、`tags.Array.data[0]` として要素を1つずつ書き込むか、`tags.Array.size` としてリサイズします。ルールは [PATCH /api/gameobjects/components](gameobjects.ja.md#patch-apigameobjectscomponents) が詳述しているものと同じです。配列全体の書き込みはマージではなく置換であること、要素アドレスはリサイズを行わないこと、1つのリクエストで長さと要素の両方を書き込めないこと、このエンドポイントが書き込めないシリアライズ型を要素に持つ配列は3つのアドレスすべてで拒否されること、これら以外の形で配列の内部に届くキーは名前を挙げて拒否されること、1,000,000 を超える長さは拒否されること。要素のオブジェクト参照は、このエンドポイントのすべてのオブジェクト参照と同様にアセットのみを解決します。

Color および Vector オブジェクトは部分更新です。省略したメンバーは現在値を保持します。サポートされるメンバーを少なくとも1つ指定し、指定した各メンバーを JSON 数値にする必要があります。不明または重複したメンバーは拒否されます。

ObjectReference 値が受け付けるのは `assetGuid`、`assetPath`、および任意の `assetType` だけです。不明または重複したメンバーは拒否されます。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### クエリパラメータ

| パラメータ | 必須 | 説明 |
|-----------|----------|-------------|
| `guid` | ✅ | 対象 ScriptableObject の GUID |

### リクエストボディ(JSON)

```json
{
  "properties": {
    "health": 150,
    "primaryWeapon": { "assetGuid": "def456..." }
  }
}
```

ObjectReference フィールドには `assetGuid` または `assetPath` と任意の `assetType` を持つオブジェクトを指定します。不明または重複したメンバーは拒否されます。参照をクリアするには `null` を使用します。

```json
{ "properties": { "primaryWeapon": null } }
```

### レスポンス

```json
{
  "guid": "a1b2c3...",
  "assetPath": "Assets/Data/Enemies/Goblin.asset",
  "type": "MyGame.EnemyConfig",
  "updated": ["health", "primaryWeapon"]
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `guid` の欠落、アセットが ScriptableObject でない、`properties` の欠落、プロパティ値が不正、または複合値に不明なメンバーが含まれている |
| 400 | `properties` のキーが、アセット上のどのシリアライズプロパティも指していない |
| 400 | `properties` のキー、または Color / Vector / ObjectReference 値のメンバーが重複している |
| 400 | キーが、このエンドポイントでは書き込めないプロパティを指している（ネストしたジェネリック型、`m_Script`、書き込めない要素を持つ配列、書き込み未対応のシリアライズ型） |
| 400 | キーが `name.Array.data[i]` および `name.Array.size` 以外の形で配列の内部に届いている |
| 400 | 要素のインデックスが配列の範囲外、または `Array.size` が負 |
| 400 | 1つのリクエストが配列の長さと要素の両方を書き込もうとしている |
| 400 | 値がプロパティの受け取る形と一致しない |
| 404 | 指定 GUID のアセットが見つからない |
| 403 | Asset Write カテゴリが無効 |
| 409 | Unity Editor が Play モード中 |

---

## DELETE /api/assets/scriptableobjects/{guid}

ScriptableObject アセットとその `.meta` ファイルを削除します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | 削除する ScriptableObject アセットの GUID |

### レスポンス

```json
{ "deleted": "Assets/Data/Enemies/Goblin.asset", "guid": "a1b2c3..." }
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | GUID が空、またはアセットが ScriptableObject でない |
| 404 | 指定 GUID のアセットが見つからない |
| 403 | Asset Write カテゴリが無効 |
| 409 | Unity Editor が Play モード中 |

---

## PATCH /api/assets/texture-importer/{guid}

テクスチャのインポート設定を更新し、アセットを再インポートします。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | テクスチャアセットの GUID |

### リクエストボディ(JSON)

```json
{
  "textureType": "Sprite",
  "spriteMode": "Single",
  "pixelsPerUnit": 100
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `textureType` | ❌ | `Sprite`、`Default`、`NormalMap`、`GUI`、`Cursor`、`Cookie`、`Lightmap`、または `SingleChannel` |
| `spriteMode` | ❌ | `Single`、`Multiple`、または `Polygon`(`textureType` が `Sprite` の場合のみ) |
| `pixelsPerUnit` | ❌ | Sprite タイプの Pixels Per Unit |

少なくとも1つのフィールドの指定が必要です。

### レスポンス

```json
{
  "guid": "a1b2c3...",
  "assetPath": "Assets/Actors/portrait.png",
  "textureType": "Sprite",
  "spriteMode": "Single",
  "pixelsPerUnit": 100.0
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | 認識可能なフィールドがない、`textureType` の値が未知、またはアセットがテクスチャでない |
| 404 | 指定 GUID のアセットが見つからない |
| 403 | Asset Write カテゴリが無効 |

---

## GET /api/assets/audio-importer/{guid}

オーディオアセットの `AudioImporter` 設定、この Editor 向けプラットフォーム
override カタログ、およびインポート後の `AudioClip` メタデータを型付きで返します。

> このエンドポイントは Read カテゴリに属します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | importer が `AudioImporter` であるアセットの GUID |

### レスポンス

```json
{
  "guid": "a1b2c3...",
  "assetPath": "Assets/Audio/theme.ogg",
  "forceToMono": false,
  "normalize": true,
  "ambisonic": false,
  "loadInBackground": false,
  "defaultSampleSettings": {
    "loadType": "CompressedInMemory",
    "compressionFormat": "Vorbis",
    "quality": 0.7,
    "preloadAudioData": true,
    "sampleRateSetting": "PreserveSampleRate",
    "sampleRateOverride": 0,
    "conversionMode": 0
  },
  "defaultCompressionFormats": ["PCM", "Vorbis", "ADPCM"],
  "supportedConversionModes": [0],
  "platforms": [{
    "platform": "WebGL",
    "installed": false,
    "compressionFormats": ["AAC"],
    "override": false,
    "inherited": {
      "loadType": "CompressedInMemory",
      "compressionFormat": "Vorbis",
      "quality": 0.7,
      "preloadAudioData": true,
      "sampleRateSetting": "PreserveSampleRate",
      "sampleRateOverride": 0,
      "conversionMode": 0
    },
    "effective": {
      "loadType": "CompressedInMemory",
      "compressionFormat": "AAC",
      "quality": 0.7,
      "preloadAudioData": true,
      "sampleRateSetting": "OverrideSampleRate",
      "sampleRateOverride": 44100,
      "conversionMode": 0
    }
  }],
  "audioClip": {
    "name": "theme",
    "length": 12.5,
    "channels": 2,
    "frequency": 44100,
    "samples": 551250,
    "loadType": "CompressedInMemory",
    "preloadAudioData": true,
    "ambisonic": false,
    "loadInBackground": false,
    "loadState": "Loaded"
  }
}
```

現在の Editor が serialized normalization setting を公開している場合、`normalize` は
bool です。公開していない場合、GET は `null` を返します。PATCH では引き続き bool が
必要で、その Editor が設定を更新できない場合は `400` を返します。

`defaultSampleSettings` と各プラットフォームの `inherited` は、保存されている
default の基準値です。`effective` はそのプラットフォームに対して
`AudioImporter.GetOverrideSampleSettings()` が返す値です。`override` が `false`
でも Unity が継承値を変換することがあり、WebGL で default codec が `AAC` に
変換されるケースがその例です。`override` が `true` の場合、`effective` は
明示的な override です。

`platforms` は、この Editor が認識する obsolete でない build target から生成されます。
`installed` は、その group の platform module が1つ以上インストールされているかを
示します。未インストールの platform も読み取り可能で、serialized override を持つ場合があります。

### Compression Format の互換性

現在のリクエストでは、レスポンスの `compressionFormats` 配列が正となります。
互換性モデルは次のとおりです。

| 設定 | 使用可能な format |
|----------|------------------|
| Default、`Standalone`、`WSA` | `PCM`、`Vorbis`、`ADPCM` |
| `WebGL` | `AAC` |
| `PS4`、`PS5` | `PCM`、`Vorbis`、`ADPCM`、`MP3`、`ATRAC9` |
| `GameCoreScarlett`、`GameCoreXboxSeries`、`GameCoreXboxOne` | `PCM`、`Vorbis`、`ADPCM`、`MP3`、`XMA` |
| この Editor が返すその他の platform | `PCM`、`Vorbis`、`ADPCM`、`MP3` |

platform 名は従来の enum alias (`iPhone`、`Metro`) ではなく、現在の名称
(`iOS`、`WSA`) を使います。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | アセットが `AudioImporter` を使っていない |
| 404 | 指定 GUID のアセットが見つからない |

---

## PATCH /api/assets/audio-importer/{guid}

AudioImporter 設定を検証して更新し、変更がある場合だけ `SaveAndReimport()` を
1回呼び出して、上記 GET と同じ最終状態を返します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中または競合する Editor activity の実行中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{
  "forceToMono": true,
  "normalize": true,
  "defaultSampleSettings": {
    "loadType": "CompressedInMemory",
    "compressionFormat": "Vorbis",
    "quality": 0.7,
    "preloadAudioData": true,
    "sampleRateSetting": "OptimizeSampleRate"
  },
  "platformOverrides": [{
    "platform": "Android",
    "override": true,
    "sampleSettings": {
      "compressionFormat": "Vorbis",
      "quality": 0.5,
      "preloadAudioData": false
    }
  }, {
    "platform": "WebGL",
    "override": false
  }]
}
```

トップレベルフィールド:

| フィールド | 型 | 説明 |
|-------|------|-------------|
| `forceToMono` | bool | インポートする音源を mono に変換 |
| `normalize` | bool | force-to-mono 後の音源を normalize |
| `ambisonic` | bool | clip を ambisonic audio として扱う |
| `loadInBackground` | bool | main thread を block せず clip data を load |
| `defaultSampleSettings` | object | 保存されている default sample settings への部分 patch |
| `platformOverrides` | array | platform override の作成、更新、または削除 |

sample settings は部分 patch です。

| フィールド | 型 | 使用可能な値 |
|-------|------|-----------------|
| `loadType` | string | `DecompressOnLoad`、`CompressedInMemory`、`Streaming` |
| `compressionFormat` | string | 対応する `compressionFormats` 配列の値 |
| `quality` | number | `0` から `1` の有限値 |
| `preloadAudioData` | bool | default/platform sample settings ごとに保存する preload policy |
| `sampleRateSetting` | string | `PreserveSampleRate`、`OptimizeSampleRate`、`OverrideSampleRate` |
| `sampleRateOverride` | integer | `OverrideSampleRate` では `1..192000`、それ以外では `0` |
| `conversionMode` | integer | `0` のみ。Unity はフィールドを公開していますが、0 以外の public flag は定義していません |

`sampleRateSetting` を `OverrideSampleRate` 以外へ変更したとき、
`sampleRateOverride` を省略すると `0` にクリアされます。他の mode とともに0以外の
override を指定した場合は拒否されます。

Unity 6 では preload policy は global な `AudioImporter` property ではなく sample
settings の一部です。nested object 内に置くことで、Unity 2022.3 と Unity 6 に共通の
contract となり、platform ごとの preload override にも対応します。

各 platform entry には `platform` と bool の `override` が必要です。
`override: true` では、空でない `sampleSettings` も必要です。現在の effective
settings に patch を適用し、その結果を明示的な override として登録します。
`override: false` では `sampleSettings` を指定できず、override を clear します。
すでに継承状態の platform を clear した場合は unchanged request になります。

reimport 前にリクエスト全体を検証します。未知または重複した field、JSON type の
不一致、未知の enum/platform、重複した platform entry、互換性のない codec、
不正な range/combination は、reimport せずに `400` を返します。Unity が staged
platform override の1つを拒否した場合は、staged override をすべて復元して失敗します。

### レスポンス

GET と同じ importer、platform、`audioClip` field に、次の field が加わります。

```json
{
  "...": "...",
  "reimported": true,
  "diagnostics": [{
    "severity": "warning",
    "message": "Import message",
    "file": "Assets/Audio/theme.ogg",
    "line": 0
  }]
}
```

`diagnostics` は最終 import に対する Unity import log の warning/error entry です。
unchanged request は `reimported: false` と空の diagnostics 配列を返し、
`SaveAndReimport()` を呼び出しません。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | リクエスト、設定の組み合わせ、platform が不正、Unity が override を拒否、またはアセットが audio でない |
| 403 | Asset Write カテゴリが無効 |
| 404 | 指定 GUID のアセットが見つからない |
| 409 | Unity Editor が Play モード中、または競合 activity の実行中 |
| 500 | normalization の書き込み、reimport が失敗、または reimport 後に importer が消失 |

---

## GET /api/assets/model-importer/{guid}

アセットの `ModelImporter` 基本設定と永続的なインポート済みサブアセットを、
バージョン付きの正規化形式で返します。この Read エンドポイントはアセット更新中に
利用できません。

```json
{
  "schemaVersion": 1,
  "guid": "a1b2c3...",
  "assetPath": "Assets/Models/robot.fbx",
  "capabilities": {
    "unityVersion": "6000.0.80f1",
    "useFileUnits": true,
    "tangentImport": true,
    "bakeIk": true
  },
  "settings": {
    "model": { "globalScale": 1.0, "fileScale": 1.0, "useFileScale": true, "useFileUnits": true, "bakeAxisConversion": false, "preserveHierarchy": false, "isReadable": false },
    "mesh": { "compression": "Off", "indexFormat": "Auto", "keepQuads": false, "weldVertices": true, "skinWeights": "Standard", "maxBonesPerVertex": 4, "minBoneWeight": 0.001, "optimizePolygons": true, "optimizeVertices": true },
    "geometry": { "addCollider": false, "importBlendShapes": true, "importCameras": true, "importLights": true, "importVisibility": true, "importConstraints": true, "swapUvChannels": false, "generateSecondaryUv": false, "secondaryUvMarginMethod": "Manual", "secondaryUvAngleDistortion": 8.0, "secondaryUvAreaDistortion": 15.0, "secondaryUvHardAngle": 88.0, "secondaryUvPackMargin": 4.0 },
    "normals": { "import": "Import", "blendShapeImport": "Calculate", "calculationMode": "AreaAndAngleWeighted", "smoothingSource": "PreferSmoothingGroups", "smoothingAngle": 60.0 },
    "tangents": { "import": "CalculateMikk" }
  },
  "subAssets": [{ "guid": "a1b2c3...", "localIdentifier": "4300000", "name": "Body", "type": "UnityEngine.Mesh" }]
}
```

`fileScale` は読み取り専用です。`subAssets` には `AssetDatabase.IsSubAsset` が
true のインポート済み `Mesh`、`Material`、`Avatar`、`AnimationClip` が入り、
プレビューオブジェクトは除外されます。64 bit 精度を失わないよう
`localIdentifier` は10進文字列です。永続的な識別には `(guid, localIdentifier)`
を使用します。

同じ `settings` オブジェクトには次の設定も含まれます。

```json
{
  "materials": {
    "importMode": "ImportViaMaterialDescription",
    "location": "InPrefab",
    "naming": "BasedOnMaterialName",
    "search": "RecursiveUp"
  },
  "materialRemaps": [{
    "source": { "type": "UnityEngine.Material", "name": "Body" },
    "target": { "guid": "def456...", "localIdentifier": "2100000", "name": "RobotBody", "type": "UnityEngine.Material" }
  }],
  "rig": {
    "animationType": "Human",
    "avatarSetup": "CopyFromOther",
    "sourceAvatar": { "guid": "987abc...", "localIdentifier": "9000000", "name": "RobotAvatar", "type": "UnityEngine.Avatar" },
    "autoGenerateAvatarMappingIfUnspecified": false,
    "humanoidOversampling": "X2",
    "optimizeGameObjects": true,
    "extraExposedTransformPaths": ["Root/WeaponSocket"]
  },
  "unsupportedInitialSettings": ["rig.humanDescription"]
}
```

`materialRemaps` は `GetExternalObjectMap()` の Material 項目です。null target は
GET では返りません。`humanDescription` は任意の serialized property として公開せず、
未対応であることをすべての設定 snapshot に明示します。

---

## POST /api/assets/model-importer/{guid}/preflight

PATCH の契約を検証し、変更やインポートをせずに `valid`、`reimportRequired`、
`changedFields`、正規化された `before` と `after` の設定を返します。

```json
{
  "schemaVersion": 1,
  "model": { "globalScale": 1.0, "isReadable": true },
  "normals": { "import": "Calculate" },
  "tangents": { "import": "CalculateMikk" }
}
```

`schemaVersion` は必須で整数 `1` です。空でない設定グループが1つ以上必要です。
各グループは部分更新で、省略フィールドは現在値を維持します。未知または重複した
フィールドと不正な JSON 型は拒否されます。enum は GET が返す名前を大文字小文字を
区別せず受け付けます。

| Group | 書き込み可能なフィールド |
|-------|---------------------------|
| `model` | `globalScale` (`> 0`、`<= 100000`)、`useFileScale`、`useFileUnits`、`bakeAxisConversion`、`preserveHierarchy`、`isReadable` |
| `mesh` | `compression`、`indexFormat`、`keepQuads`、`weldVertices`、`skinWeights`、`maxBonesPerVertex` (`1..255`)、`minBoneWeight` (`0..1`)、`optimizePolygons`、`optimizeVertices` |
| `geometry` | `addCollider`、`importBlendShapes`、`importCameras`、`importLights`、`importVisibility`、`importConstraints`、`swapUvChannels`、`generateSecondaryUv`、`secondaryUvMarginMethod`、`secondaryUvAngleDistortion` (`1..75`)、`secondaryUvAreaDistortion` (`1..75`)、`secondaryUvHardAngle` (`0..180`)、`secondaryUvPackMargin` (`1..64`) |
| `normals` | `import`、`blendShapeImport`、`calculationMode`、`smoothingSource`、`smoothingAngle` (`0..180`) |
| `tangents` | `import` |
| `materials` | `importMode`、`location`、`naming`、`search` |
| `materialRemaps` | `{source: {type: "UnityEngine.Material", name}, target}` の配列。`target: null` はその source remap を削除 |
| `rig` | `animationType`、`avatarSetup`、`sourceAvatar`、`autoGenerateAvatarMappingIfUnspecified`、`humanoidOversampling`、`optimizeGameObjects`、`extraExposedTransformPaths` |

`useFileUnits` と tangent import はソースの capability に対しても検証されます。
normal が `None` の場合、tangent も `None` でなければなりません。

Material と Avatar の target は `{guid, localIdentifier}` で指定します。参照先に必要な型の
オブジェクトが1つだけの場合に限り `localIdentifier` を省略できます。欠落、型違い、曖昧な
target は、設定や remap を変更する前にリクエスト全体を拒否します。同じ remap source を
1リクエスト内で繰り返すこともできません。

Material の naming/search は material import が必要で、`location: InPrefab` とは互換性が
ありません。remap の追加・置換には `None` 以外の import mode が必要ですが、古い remap の
削除は可能です。`CopyFromOther` には有効で
互換性のある source Avatar が必要です。`None` と `Legacy` rig には `NoAvatar` が必要です。
humanoid oversampling は Human 専用で、自動 mapping にはさらに `CreateFromThisModel` が必要、
exposed transform path には optimization が必要です。互換性のないフィールドは無視せず拒否します。

---

## PATCH /api/assets/model-importer/{guid}

preflight の契約を適用し、`SaveAndReimport()` を最大1回呼びます。変更がなければ
再インポートせず `reimported: false` を返します。変更時のレスポンスには完全な
設定とサブアセットを持つ `before` と `after`、`subAssetDelta`、`diagnostics`、
`rollback` が含まれます。再インポートが例外になった場合、元の設定の復元を試み、
構造化された rollback 結果とともに `500` を返します。

| ステータス | 原因 |
|-------------|------|
| 400 | 不正な schema、フィールド、型、範囲、enum、組み合わせ、capability、または Model 以外のアセット |
| 403 | Asset Write カテゴリが無効 |
| 404 | 指定 GUID のアセットがない |
| 409 | Play mode、競合 activity、ロード済み Scene の競合、または編集不能な Importer |
| 500 | 再インポート失敗、または再インポート後に Importer が見つからない |

---
