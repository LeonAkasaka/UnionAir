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
| `subAssets` | object[] | メインアセット以外にそのファイルが含むオブジェクト。メインアセットのみのパスではフィールドごと省略されます |

```json
"subAssets": [
  { "localIdentifier": "4300014", "name": "BLW_DEF", "type": "UnityEngine.Mesh" },
  { "localIdentifier": "4300038", "name": "button",  "type": "UnityEngine.Mesh" }
]
```

[オブジェクト参照](general.ja.md#ファイル内の1オブジェクトを指定する)が1つを指定する際に送るのが `localIdentifier` であり、クライアントはここから読み取ります。`name` は説明であって解決には使えません(2つのサブアセットが同じ名前を持つことがあります)。

このフィールドが存在すること自体が、そのパスがパスと型だけでは指定できないという合図です。

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

## GET /api/assets/materials/{guid}

マテリアルのシェーダー、レンダーキュー、有効なキーワード、およびシェーダーが宣言する全プロパティの現在値を返します。

> Read カテゴリが必要です(既定で有効)。

### パスパラメータ

| パラメータ | 説明 |
|-----------|------|
| `guid` | 対象マテリアルアセットの GUID |

### レスポンス

```json
{
  "guid": "5ebb6c...",
  "assetPath": "Assets/Materials/Hair.mat",
  "shader": "Toon/Toon",
  "renderQueue": 2000,
  "keywords": ["_EMISSIVE_SIMPLE", "_IS_CLIPPING_OFF"],
  "properties": [
    { "name": "_BaseColor", "type": "Color", "value": { "r": 1, "g": 1, "b": 1, "a": 1 }, "flags": [] },
    { "name": "_BumpScale", "type": "Range", "value": 1.0, "range": { "min": 0, "max": 1 }, "flags": [] },
    {
      "name": "_MainTex",
      "type": "Texture",
      "value": { "assetGuid": "cbb65e...", "assetPath": "Assets/Textures/hair.tga", "assetType": "UnityEngine.Texture2D" },
      "flags": []
    },
    { "name": "_ToonMaterialVersion", "type": "Int", "value": 0, "flags": ["HideInInspector"] }
  ]
}
```

| フィールド | 説明 |
|-----------|------|
| `shader` | シェーダー名。`POST /api/assets/materials` が受け取るのと同じ文字列なので、マテリアルを作り直せます |
| `renderQueue` | `Material.renderQueue`。読み取り専用です |
| `keywords` | 有効なシェーダーキーワード。読み取り専用です |
| `properties[].name` | シェーダープロパティ名。`PATCH /api/assets/materials` が受け付けるキーです |
| `properties[].type` | `Color`、`Float`、`Range`、`Int`、`Vector`、`Texture` のいずれか |
| `properties[].value` | 現在値。書き込みが読む綴りで返ります |
| `properties[].range` | `{min, max}`。`Range` プロパティにのみ存在します |
| `properties[].flags` | Unity のシェーダープロパティフラグ名(`HideInInspector`、`Normal`、`Gamma`、`PerRendererData` など) |

プロパティはシェーダーが宣言した順に並び、非表示のものも含めてすべて報告されます。シェーダーによっては200個以上を宣言します。マテリアルの実質的な表面と内部的な設定を見分けるための手がかりが `flags` です。

### 値は書き込みが読む綴りで返ります

`value` はいずれも変換なしにそのまま [`PATCH /api/assets/materials`](#patch-apiassetsmaterials) へ送り返せます。読み取り → 1つの値を変更 → 書き戻し、というラウンドトリップが成立します。

| シェーダープロパティ型 | 報告形式 |
|---|---|
| `Color` | `{"r":float,"g":float,"b":float,"a":float}` |
| `Float`、`Range` | `float` |
| `Int` | `int` |
| `Vector` | `{"x":float,"y":float,"z":float,"w":float}` |
| `Texture` | [オブジェクト参照](general.ja.md#オブジェクト参照)、未割り当ての場合は `null`(書き込み側でテクスチャを解除する際に使う値と同じ) |

ここで `properties` が配列なのは、`type`・`range`・`flags` を名前→値のマップに収める場所がないためです。そしてこれらは Unity にしか答えられない部分です(`.mat` ファイルが持つのは Unity が記録したオーバーライドであり、プロパティの集合や型ではありません)。書き込み側はマップを受け取り、この配列の `name` はいずれもそのキーになります。

ラウンドトリップしないフィールドは `renderQueue` と `keywords` の2つです。他のマテリアルのプロパティ値から組み立てたマテリアルが同じ見た目にならない原因は、たいていこの2つであるため報告しています。これらの書き込みは、このエンドポイントの対となる書き込み API の対象外です。

テクスチャのスケールとオフセットは報告しません。これらは独立したシェーダープロパティではなく、書き込み側にも対応する語彙がないためです。

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `guid` が空、またはアセットがマテリアルでない |
| 404 | GUID に対応するアセットが存在しない |

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
    "_BumpMap": { "assetGuid": "b1c2d3..." }
  }
}
```

`properties` の各キーはシェーダーのプロパティ名です。名前はシェーダーが宣言したものであり、大文字小文字を区別します。

`properties` の値の型:

| シェーダープロパティ型 | 形式 |
|----|------|
| Color | `{"r":float,"g":float,"b":float,"a":float}` |
| Float, Range | `float` |
| Int | `int` |
| Vector | `{"x":float,"y":float,"z":float,"w":float}` |
| Texture | アセットを指す[オブジェクト参照](general.ja.md#オブジェクト参照)(`assetGuid`、`assetPath`、任意の `assetType`)、または解除する場合は `null` |

`Color` と `Vector` は成分を省略できます。省略した成分はマテリアルの現在値を保持します。未知の成分、重複した成分、数値でない成分を含むオブジェクト、および成分を1つも持たないオブジェクトは `400` を返します。

### すべてのキーが書き込まれるか、1つも書き込まれないか

マテリアルのシェーダーに存在しないプロパティ名、そのプロパティ型に対して形式が誤っている値、および重複したキーは、いずれもキー名と理由を示す `400` を返します。マテリアルは変更されません。最初の値を適用する前にリクエスト全体を解決するため、拒否された場合はリクエスト前の状態がそのまま残ります。

したがって `updated` は常にリクエストが送ったすべてのキーを列挙し、`200` はリクエスト全体が適用されたことを意味します。リクエストと突き合わせるためのフィルターではありません。

テクスチャの指定方法は `GET /api/gameobjects` が報告する形式と同一であり、レンダラーのマテリアルから読み取ったテクスチャをそのまま送り返せます。GUID 文字列単体はオブジェクト参照ではないため `400` を返します。

### レスポンス

```json
{ "updated": ["_BaseColor", "_Metallic"] }
```

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `guid` の欠落、`properties` の欠落または JSON オブジェクトでない、あるいはキーがシェーダープロパティを指していない・他のキーと重複している・値の形式が誤っている |
| 404 | 一致するマテリアルが存在しない、またはテクスチャ値が存在しないアセットを指している |
| 403 | Asset Write カテゴリが無効 |

---

## GET /api/assets/shaders/{guid}

シェーダーのインポート状態、キャッシュされたコンパイラメッセージ、実効ローカルキーワード空間、既定値付きの宣言済みプロパティ、および Unity がコンパイルしたサブシェーダーを返します。

> Read カテゴリが必要です(既定で有効)。

### パスパラメータ

| パラメータ | 説明 |
|-----------|------|
| `guid` | 対象シェーダーアセットの GUID |

### レスポンス

```json
{
  "guid": "5ebb6c...",
  "assetPath": "Assets/Shaders/Toon.shader",
  "name": "Toon/Toon",
  "isSupported": true,
  "hasError": false,
  "hasWarnings": false,
  "messages": [],
  "renderQueue": 2000,
  "maximumLOD": -1,
  "subshaderCount": 1,
  "passCount": 2,
  "keywords": [
    { "name": "_ALPHATEST_ON", "isOverridable": false, "isDynamic": false }
  ],
  "properties": [
    { "name": "_BaseColor", "type": "Color", "description": "Base Color", "defaultValue": { "r": 1, "g": 1, "b": 1, "a": 1 }, "flags": ["MainColor"], "attributes": [] },
    { "name": "_Cutoff", "type": "Range", "description": "Alpha Cutoff", "defaultValue": 0.5, "range": { "min": 0, "max": 1 }, "flags": [], "attributes": [] },
    { "name": "_AlphaClip", "type": "Float", "description": "Alpha Clipping", "defaultValue": 0, "flags": [], "attributes": ["Toggle(_ALPHATEST_ON)"] },
    { "name": "_MainTex", "type": "Texture", "description": "Base Map", "defaultValue": "white", "textureDimension": "Tex2D", "flags": ["MainTexture"], "attributes": [] }
  ],
  "activeSubshaderIndex": 0,
  "subshaders": [
    {
      "levelOfDetail": 300,
      "renderPipeline": "UniversalPipeline",
      "passes": [
        { "name": "ForwardLit", "lightMode": "UniversalForward", "isGrabPass": false },
        { "name": "ShadowCaster", "lightMode": "ShadowCaster", "isGrabPass": false }
      ]
    }
  ]
}
```

| フィールド | 説明 |
|-----------|------|
| `guid`, `assetPath` | シェーダーのインポート元アセット。Unity 内蔵シェーダーの場合は共有の内蔵リソースコンテナが返ります(`Standard` なら `Resources/unity_builtin_extra` と、全内蔵アセットが共有する GUID)。したがってその `guid` は識別子として使えず、送り返しても `400` になります。そうしたシェーダーには名前による検索で到達してください |
| `name` | シェーダー名。`POST /api/assets/materials` が受け取り、`GET /api/assets/materials/{guid}` が報告するのと同じ文字列です。ShaderLab のパースが名前を読む前に失敗した場合にのみ `null` になります |
| `isSupported` | Unity の能力シグナル。現在の GPU でこのシェーダーが動作するか(フォールバックを含めて判定)を表します。インポートの成否を表すものでは**なく**、以下のフィールドが誰の宣言を記述しているかを表すものでも**ありません** — [`isSupported` が言えること・言えないこと](#issupported-が言えること言えないこと) を参照 |
| `renderQueue` | シェーダーが宣言するキュー。マテリアル側で上書きできます |
| `maximumLOD` | `Shader.maximumLOD`。キャップを設定していない場合は `-1` で、こちらが通常のケースです |
| `hasError`, `hasWarnings` | 直近のインポートで Unity がエラー/警告を記録したかどうか。`hasError` は「そのシェーダーが使用不能」を意味**しません**。`isSupported` を参照してください |
| `messages[]` | インポート時に Unity がキャッシュしたコンパイラメッセージ。[診断は直近のインポート由来](#診断は直近のインポート由来) を参照 |
| `keywords[]` | シェーダーの**実効ローカルキーワード空間** — そのシェーダーで有効なキーワード全件(有効・無効を問わない)。`isOverridable` と `isDynamic` を伴います。ソースより広く、`Fallback` や `UsePass` の依存先から来るキーワードや、Unity が自動的に追加するキーワードも含みます。6000.0.80f1 で実測したところ、`multi_compile` でちょうど1件だけ宣言したシェーダーが5件を報告し、残り4件は `STEREO_INSTANCING_ON`、`UNITY_SINGLE_PASS_STEREO`、`STEREO_MULTIVIEW_ON`、`STEREO_CUBEMAP_RENDER_ON` でした。ここに名前があることは、それがファイルに書かれている根拠には**なりません** |
| `properties[]` | 宣言された全プロパティ。宣言順で、非表示のものも含みます |
| `properties[].type` | `Color`、`Float`、`Range`、`Int`、`Vector`、`Texture` のいずれか |
| `properties[].description` | Inspector に表示されるラベル。宣言にない場合は `null` |
| `properties[].defaultValue` | 新規マテリアルの初期値。`PATCH /api/assets/materials` が読み取る表記に揃えてあります。`Texture` はオブジェクト参照ではなく組み込みテクスチャ名(`"white"`、`"bump"` など)を返します。宣言が保持しているのがその名前だからです |
| `properties[].range` | `{min, max}`。`Range` プロパティにのみ存在します |
| `properties[].textureDimension` | プロパティが要求するテクスチャの種類(`Tex2D`、`Cube`、`Tex3D` など)。`Texture` プロパティにのみ存在します |
| `properties[].flags` | Unity のシェーダープロパティフラグ名。`GET /api/assets/materials/{guid}` が返すものと同じ集合です。Unity は一部の宣言属性をフラグに変換します。`[HideInInspector]`、`[MainTexture]`、`[MainColor]`、`[HDR]`、`[NoScaleOffset]` は `attributes` ではなくこちらに現れます |
| `properties[].attributes` | Unity がフラグに変換しなかった宣言属性を、引数を含めてそのまま返します(`Toggle(_ALPHATEST_ON)`、`KeywordEnum(...)`、カスタムドローワー名など)。特に `Toggle` は、そのプロパティがどのキーワードを駆動するかを示す唯一の手がかりです(フラグには現れません) |
| `activeSubshaderIndex` | 現在のプラットフォームとパイプラインに対して Unity が選択したサブシェーダー |
| `subshaders[]` | Unity が**コンパイルした**サブシェーダー。ファイルの宣言と一致するとは限りません — シェーダー自身のサブシェーダーが使用不能で `Fallback` を指定している場合、ここに現れるのはフォールバック先のものです。パスの `name` は名前が付いていない場合 `null`、`lightMode` はパスの `LightMode` タグで、宣言がない場合は `null` |
| `subshaders[].renderPipeline` | サブシェーダーの `RenderPipeline` タグ(`UniversalPipeline`、`HDRenderPipeline`、あるいはファイルが指定した文字列)。つまり「このシェーダーはどのパイプライン向けか」への答えです。そのタグを宣言していない場合は `null` で、ビルトインパイプライン向けのサブシェーダーはこう読めます。シェーダー単位ではなくサブシェーダー単位である点に注意してください — 1 つのファイルが URP 用とビルトイン用のサブシェーダーを併せ持つことができ、両者を区別できるのはこのタグだけです |

### ファイルからは分からないこと

クライアントは `.shader` や `.hlsl` を自分で書けますし、このエンドポイントはその役割を奪いません。ファイルに書かれていないのは次の 2 点です。

- **Unity がそれを受け入れたかどうか。** シェーダーのコンパイルはインポート時に行われ、失敗したシェーダーも見た目はそのままディスク上に残ります。`hasError` と `messages` は、[`POST /api/compile`](compile.ja.md) が C# に対して閉じているのと同じ「編集 → インポート → 診断」のループをシェーダーに対して閉じます。
- **インポートが何を生成したか。** `activeSubshaderIndex` は現在のレンダーパイプラインとプラットフォームによって決まり、どのファイルにも書かれていません。Shader Graph アセットに至っては、プロパティ・キーワード・パスのいずれも読める形では持っておらず、すべてインポート時に生成されます。

タグは `tags` のようなマップではなく、名前付きフィールドとして報告します。フィールドを持つのは `lightMode` と `renderPipeline` の 2 つです。Unity はタグを名前で引くことしかできず、サブシェーダーやパスが保持しているタグを列挙する手段を公開していません。したがってマップにしたところで、このエンドポイントが問い合わせようと決めたキーしか入らないのに、全件であるかのように見えてしまいます。

### 診断は直近のインポート由来

`messages` は、そのアセットが最後にインポートされたときに Unity がキャッシュした内容であり、その場でのコンパイル結果ではありません。ファイルを編集したら [`POST /api/assets/reimport`](#post-apiassetsreimport) か [`POST /api/editor/refresh`](editor.ja.md#post-apieditorrefresh) で再インポートしてから読み直してください。

各メッセージは 1 本の文字列に潰さず、文脈を保ったまま返します。

| フィールド | 説明 |
|-----------|------|
| `severity` | `Error` または `Warning` |
| `message` | コンパイラメッセージ |
| `messageDetails` | Unity が持っている場合の詳細形式。ない場合は `null` |
| `file` | メッセージが指すファイル。シェーダー本体ではなくインクルード先のこともあります。メッセージがファイルを持たない場合は `null` |
| `line` | そのファイル内の行番号。メッセージが行を持たない場合は `0` |
| `platform` | メッセージの発生元グラフィックス API。同じ編集が API によってエラーになったりならなかったりする理由です。API が関与しないメッセージでは `null` — ShaderLab のパースエラーは API に到達する前に発生し、Unity は未定義のプラットフォーム値を報告します |

### Unity がファイルから何も読めなかった場合

構造的フィールドが `null` になるのは1つのケースだけです。ShaderLab のパースがシェーダー名を読む前に失敗し、レスポンスのどの値もファイル由来ではあり得ない場合です。6000.0.80f1 で、プロパティ1個・パス1個を宣言してその形で失敗したシェーダーを実測したところ、`name` は `""`、`properties` は空、`keywords` にはシェーダーが宣言していない stereo 系キーワードが4件、`passCount` はファイル上の1パスに対して `3` でした。いずれも正常な回答と区別がつかず、`properties` からマテリアルを組み立てるクライアントはプロパティのないマテリアルを作り、その理由を知ることもありません。

そのため、このときは `renderQueue`、`maximumLOD`、`subshaderCount`、`passCount`、`keywords`、`properties`、`activeSubshaderIndex`、`subshaders` をまとめて `null` にし、代わりに `messages` が答えになります。`guid`、`assetPath`、`isSupported`、`hasError`、`hasWarnings`、`messages` は常に報告されます。

この条件は意図的に狭くしてあります。それ以外のシェーダー — コンパイルに失敗したものも、この環境では動作しないものも — は Unity が公開している構造を報告します。`properties`、`name`、`renderQueue` についてはそれが宣言内容ですが、`subshaders` については Unity がコンパイルしたものであり、`Fallback` 先のこともあります。

### `isSupported` が言えること・言えないこと

`isSupported` は [Unity 自身の能力シグナル](https://docs.unity3d.com/ja/2022.3/ScriptReference/Shader-isSupported.html)で、フォールバックを含めて現在の GPU でそのシェーダーが動作するかを表します。それ以上の意味に読めない理由は、6000.0.80f1 での2つの実測が示しています。

| シェーダー | `hasError` | `isSupported` | フィールドが記述しているもの |
| --- | --- | --- | --- |
| 正常だが、唯一のパスが現在のレンダラーで除外されている | `false` | **`false`** | 自身の宣言。`properties` も `name` も正しい |
| 同じシェーダーに `Fallback "Diffuse"` を付けたもの | `false` | **`true`** | `properties` は自身のものだが、`subshaders` は `Legacy Shaders/Diffuse` のもの — サブシェーダー2件・パス4件で、そのシェーダーを直接読んだ結果と一致 |

したがって `false` はインポート失敗を意味せず、`true` は `subshaders` が自分の書いたファイルのものである保証にもなりません。`isSupported` は「この環境でこのシェーダーが使えるか」、`hasError` は「自分の編集がクリーンにコンパイルされたか」として読み、どちらも出所の主張としては読まないでください。

`hasError` も使用可否のシグナルではありません。1つ目のサブシェーダーがコンパイルに失敗し2つ目が成功するシェーダーを実測すると、`hasError` は `true` で `isSupported` も `true` になり、Unity は成功したサブシェーダーを選択します。

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `guid` が空、またはアセットがシェーダーではない |
| 404 | その GUID のアセットが存在しない |

---

## GET /api/assets/shaders

指定した名前のシェーダーについて、同じ内容を返します。

> Read カテゴリが必要です(既定で有効)。

### クエリパラメータ

| パラメータ | 説明 |
|-----------|------|
| `name` | シェーダー名。`GET /api/assets/materials/{guid}` が報告し、`POST /api/assets/materials` が受け取る文字列です |

この名前はマテリアルが保持している文字列であり、ファイル名ではありません。またこの方法は、プロジェクトのアセットとしてではなく Unity に同梱されているシェーダーに到達できる唯一の手段です。`Standard` がその例で、`Resources/unity_builtin_extra` と全内蔵アセットが共有する GUID を返します。その GUID を `GET /api/assets/shaders/{guid}` に送っても、コンテナのメインアセットがシェーダーではないため `400 Asset is not a Shader` になります。こうしたシェーダーにとって名前は唯一の手がかりです。

検索は `POST /api/assets/materials` が行うものと同一です。したがって、このエンドポイントが 404 を返す名前は、マテリアル作成でも失敗する名前です。先にここで確認しておけば、マテリアルを作る前にそれが分かります。

### レスポンス

[`GET /api/assets/shaders/{guid}`](#get-apiassetsshadersguid) と同じドキュメントです。

### エラー

| ステータス | 原因 |
|-----------|------|
| 400 | `name` が未指定または空 |
| 404 | その名前のシェーダーが存在しない |

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
    "bakeIk": true,
    "settings": {
      "model.useFileUnits": true,
      "tangents.import": true,
      "clips.definitions": true,
      "clips.avatarMask": true,
      "clips.events": true,
      "clips.curves": false,
      "rig.humanDescription": false
    },
    "unavailableSettings": ["rig.humanDescription", "clips.curves"]
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
  "clips": {
    "derivedFromDefaults": false,
    "definitions": [{
      "takeName": "Take 001",
      "name": "Idle",
      "firstFrame": 0.0,
      "lastFrame": 60.0,
      "wrapMode": "Default",
      "loop": false,
      "loopTime": true,
      "loopPose": true,
      "mirror": false,
      "lockRootRotation": true,
      "keepOriginalOrientation": true,
      "rotationOffset": 0.0,
      "lockRootHeightY": true,
      "keepOriginalPositionY": true,
      "heightFromFeet": false,
      "heightOffset": 0.0,
      "lockRootPositionXZ": true,
      "keepOriginalPositionXZ": true,
      "cycleOffset": 0.0,
      "hasAdditiveReferencePose": false,
      "additiveReferencePoseFrame": 0.0,
      "maskType": "CreateFromThisModel",
      "maskSource": null,
      "maskNeedsUpdating": false,
      "events": []
    }]
  },
  "unsupportedInitialSettings": ["rig.humanDescription", "clips.curves"]
}
```

`materialRemaps` は `GetExternalObjectMap()` の Material 項目です。参照先が欠落した target は
null として返すため、client は stale remap を検出して削除できます。`humanDescription` は
任意の serialized property として公開せず、未対応であることをすべての設定 snapshot に明示します。
`clipAnimations` が空の場合、`clips.definitions` は `defaultClipAnimations` から作られ、
`derivedFromDefaults` は true です。これにより「保存済み override 配列がない」と
「animation take がない」を区別できます。schema version 1 では clip curve は未対応です。

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
| `clips` | 順序付き配列の全置換。各 entry に `takeName`、一意な `name`、`firstFrame`、`lastFrame` が必須 |

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

### Imported clip definition

`clips` は `ModelImporter.clipAnimations` を順序付き配列として一括置換します。`[]` を
送ると保存済み配列が削除され、次の読み取りは default-derived になります。同じ
`(takeName, name)` の保存済み definition があれば各 entry の基準にし、なければ指定した
default take を基準にします。省略した任意フィールドはその基準値を維持します。

任意フィールドは `wrapMode`、`loop`、`loopTime`、`loopPose`、`mirror`、
`lockRootRotation`、`keepOriginalOrientation`、`rotationOffset`、`lockRootHeightY`、
`keepOriginalPositionY`、`heightFromFeet`、`heightOffset`、`lockRootPositionXZ`、
`keepOriginalPositionXZ`、`cycleOffset`、`hasAdditiveReferencePose`、
`additiveReferencePoseFrame`、`maskType`、`maskSource`、`events` です。

take は `defaultClipAnimations` に存在する必要があり、有限の frame range は順序が正しく、
その take 内でなければなりません。置換配列内の clip name は一意です。`loopPose` には
`loopTime` が必要です。additive reference frame は additive mode が必要で
clip range 内、mirror は Human 専用です。`maskType: CopyFromOther` には `AvatarMask` の
`maskSource` が必要で、それ以外の mask type では null が必要です。Mask と event object の
参照には Material/Avatar と同じ GUID/local identifier 規則を使います。

`events` は definition ごとの順序付き全置換です。各 event には有限で非負の `time` と空でない
`functionName` が必須です。任意フィールドは `stringParameter`、`floatParameter`、
`intParameter`、`objectReferenceParameter`、`messageOptions` (`DontRequireReceiver` または
`RequireReceiver`) です。未知の nested field は変更前に配列全体を拒否します。

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
