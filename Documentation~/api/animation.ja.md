# API リファレンス — Animation

[English](animation.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](animation.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`。実際の URL は接続時に `<project>/.unionair/endpoint.txt` から読み取ってください。エンドポイントの発見手順、レスポンスの規約、カテゴリ/セキュリティの注意事項は [API リファレンス索引](../api-reference.ja.md) を参照してください。

---

## Undo

アニメーション系の書き込みには、Editor で Ctrl+Z を押して戻せるものと戻せないものがあります。これは偶然ではなく意図した境界なので、エンドポイントごとではなくここに一度だけ記載します。Unity 6000.0.80f1 で計測。

| 書き込み | Undo 可否 |
|---|---|
| AnimatorController の構造(パラメータ、レイヤー、ステート、トランジション) | ✅ |
| AnimationClip のカーブ(`POST` / `DELETE .../curves`) | ❌ |
| アセットの作成(`POST /api/assets/animation-clips`、`POST /api/assets/animator-controllers`) | ❌ |

**コントローラーへの書き込みは Undo できますが、UnionAir は何もしていません。** `UnityEditor.Animations` の編集 API が自前で Undo を登録するため、ステートを追加するリクエストは Ctrl+Z 1 回で戻ります。UnionAir はこれらの経路に独自の登録を追加しません。二重に登録しても冗長なだけだからです。

**クリップのカーブへの書き込みは、意図的に Undo 対象外です。** これらの API は Undo を登録せず、UnionAir も代わりに登録しません。ここでのアセット書き込みはレスポンスを返す前にディスクへ保存されるため、`200` はファイルが既に変更済みであることを意味します。復元は Undo スタックではなくバージョン管理の担当です。Undo を登録すると、Ctrl+Z がメモリ上のアセットだけを戻し、ファイルは次の無関係な保存まで書き込んだ内容を保持するため、「前でも後でもない」状態が生じます。

**アセットの作成は Unity 自体が Undo に対応していません。** UnionAir もそれを変えません。作成を取り消すにはアセットを削除してください。

シーンへの書き込みは別で、Undo できます。[`api/gameobjects.md`](gameobjects.ja.md) を参照してください。

---

## POST /api/assets/animation-clips

AnimationClip アセットを作成します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{
  "assetPath": "Assets/Animations/Walk.anim",
  "frameRate": 60,
  "wrapMode": "Loop"
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `assetPath` | ✅ | 出力先パス(`.anim` で終わる必要があります) |
| `frameRate` | ❌ | 1秒あたりのサンプル数(既定: Unity のデフォルト、通常 60) |
| `wrapMode` | ❌ | `Once`、`Loop`、`PingPong`、`ClampForever`、または `Default` |

### レスポンス(HTTP 201)

```json
{
  "assetPath": "Assets/Animations/Walk.anim",
  "guid": "a1b2c3...",
  "frameRate": 60.0,
  "length": 0.0
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `assetPath` の欠落、または `.anim` で終わらない |
| 403 | Asset Write カテゴリが無効 |

---

## GET /api/assets/animation-clips/{guid}

AnimationClip のメタデータを、すべての float カーブおよびオブジェクト参照カーブとともに返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimationClip アセットの GUID |

### レスポンス

```json
{
  "assetPath": "Assets/Animations/Walk.anim",
  "guid": "a1b2c3...",
  "name": "Walk",
  "clipsAtPath": 1,
  "clipNames": ["Walk"],
  "imported": false,
  "importer": null,
  "writable": true,
  "frameRate": 60.0,
  "length": 1.0,
  "wrapMode": "Loop",
  "settings": {
    "loopTime": true,
    "loopBlend": false,
    "cycleOffset": 0.0,
    "loopBlendOrientation": false,
    "loopBlendPositionY": false,
    "loopBlendPositionXZ": false,
    "keepOriginalOrientation": false,
    "keepOriginalPositionY": true,
    "keepOriginalPositionXZ": false,
    "heightFromFeet": false,
    "mirror": false,
    "level": 0.0,
    "orientationOffsetY": 0.0,
    "startTime": 0.0,
    "stopTime": 1.0,
    "additiveReferencePoseTime": 0.0,
    "hasAdditiveReferencePose": false
  },
  "events": [
    {
      "time": 0.5,
      "functionName": "Footstep",
      "stringParameter": "left",
      "floatParameter": 0.0,
      "intParameter": 0,
      "objectReferenceParameter": null,
      "messageOptions": "RequireReceiver"
    }
  ],
  "curveCount": 1,
  "curves": [
    {
      "relativePath": "Hips",
      "type": "Transform",
      "property": "localPosition.y",
      "keyCount": 3,
      "keys": [
        { "time": 0.0, "value": 0.0, "inTangent": 0.0, "outTangent": 1.0 },
        { "time": 0.5, "value": 1.0, "inTangent": 0.0, "outTangent": 0.0 },
        { "time": 1.0, "value": 0.0, "inTangent": -1.0, "outTangent": 0.0 }
      ]
    }
  ],
  "objectReferenceCurveCount": 1,
  "objectReferenceCurves": [
    {
      "relativePath": "",
      "type": "Image",
      "property": "m_Sprite",
      "keys": [
        { "time": 0.0, "guid": "a1b2c3...", "name": "sprite_01" },
        { "time": 0.1667, "guid": "d4e5f6...", "name": "sprite_02" }
      ]
    }
  ]
}
```

### `wrapMode` は Loop Time ではありません

`wrapMode` はクリップオブジェクト上の `WrapMode` です。**そのクリップがループするかどうかは `settings.loopTime`** で、Animation Inspector が Loop Time と表示している項目です。6000.0.80f1 で計測: インポートされた待機クリップは `"wrapMode": "Default"` かつ `"settings": { "loopTime": true }` を返します。ループするかを知るために `wrapMode` を読むクライアントは、別のことについての答えを受け取ります。

両方を隣り合わせで返すのは、この 2 つを取り違えられないようにするためです。

### `settings`

Animation Inspector がカーブ一覧の上に表示するものすべてです: `loopTime`, `loopBlend`, `cycleOffset`, `loopBlendOrientation`, `loopBlendPositionY`, `loopBlendPositionXZ`, `keepOriginalOrientation`, `keepOriginalPositionY`, `keepOriginalPositionXZ`, `heightFromFeet`, `mirror`, `level`, `orientationOffsetY`, `startTime`, `stopTime`, `additiveReferencePoseTime`, `hasAdditiveReferencePose`。任意の部分集合を [`PATCH`](#patch-apiassetsanimation-clipsguid) で設定できます。

### `imported` / `writable` とクリップの所有者

| フィールド | 説明 |
|---|---|
| `name` | クリップ自身の名前。`assetPath` と `guid` は**ファイル**を指し、`.fbx` の中ではそれはクリップではありません |
| `clipsAtPath`, `clipNames` | 同じパスを共有する AnimationClip の数と名前 |
| `imported` | インポータが生成したクリップかどうか |
| `importer` | インポータの型名、または `null` |
| `writable` | この API が書き込むかどうか |

`.fbx` の中のクリップは `ModelImporter` が生成し、その設定はインポータが所有します。そのクリップに `AnimationUtility.SetAnimationClipSettings` を呼んでも、次の再インポートで破棄されるメモリ上のオブジェクトを変更するだけです。したがって**すべての書き込みエンドポイントはインポート済みクリップを `409` で拒否します** — 従来は書き込みを受け付けて黙って失っていた `POST` / `DELETE .../curves` も含みます。インポート済みクリップを変更するにはインポータを変更する必要があり、UnionAir はまだそれを公開していません。

所有者の判定は拡張子ではなく `AssetImporter.GetAtPath` が何をそのパスのインポータと答えるかで行います。`.anim` もインポートはされますが、そのインポータ(`NativeFormatImporter`)はクリップの設定を所有していません。

`LoadAssetAtPath` はインポータが最初に列挙したクリップを返すため、複数のテイクを持つパスは 1 つだけを GUID で公開し残りを隠します。`clipsAtPath` が `1` より大きければそれを示しています。インポート済みファイル内の個々のクリップをアドレス指定することは、このエンドポイントが解決しないサブアセットの問題です。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | アセットが AnimationClip でない |
| 404 | 指定 GUID のアセットが見つからない |

---

## PATCH /api/assets/animation-clips/{guid}

クリップの `frameRate`、`wrapMode`、および `settings` の任意の部分集合を設定します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{
  "frameRate": 30.0,
  "wrapMode": "Loop",
  "settings": { "loopTime": true, "cycleOffset": 0.0 }
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `frameRate` | ❌ | 1 秒あたりのサンプル数。0 より大きい必要があります |
| `wrapMode` | ❌ | `Once`、`Loop`、`PingPong`、`ClampForever`、`Default`。**Loop Time ではありません** |
| `settings` | ❌ | [`settings`](#settings) に挙げたフィールドの任意の部分集合 |

省略したフィールドは変更されません。未知のフィールド(settings のフィールドをトップレベルに送った場合を含む)は、その名前を挙げて `400` になります。すべての値は最初の書き込みの前に検証されるため、拒否されたリクエストはクリップを元のまま残します。

### レスポンス

```json
{
  "assetPath": "Assets/Animations/Walk.anim",
  "name": "Walk",
  "applied": ["frameRate", "settings.loopTime"],
  "settings": {
    "loopTime": true,
    "loopBlend": false,
    "cycleOffset": 0.0,
    "loopBlendOrientation": false,
    "loopBlendPositionY": false,
    "loopBlendPositionXZ": false,
    "keepOriginalOrientation": false,
    "keepOriginalPositionY": true,
    "keepOriginalPositionXZ": false,
    "heightFromFeet": false,
    "mirror": false,
    "level": 0.0,
    "orientationOffsetY": 0.0,
    "startTime": 0.0,
    "stopTime": 1.0,
    "additiveReferencePoseTime": 0.0,
    "hasAdditiveReferencePose": false
  }
}
```

`applied` はリクエストが設定した項目を列挙します。`settings` は適用後のクリップの設定オブジェクト全体であり、パッチした部分集合ではありません。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | 値が不正、`wrapMode` が未知、またはボディに未知のフィールドがある |
| 404 | 指定 GUID のアセットが見つからない |
| 409 | クリップがインポータによって生成されている |

---

## POST /api/assets/animation-clips/{guid}/events

クリップのアニメーションイベントをすべて置き換えます。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{
  "events": [
    { "time": 0.25, "functionName": "Footstep", "stringParameter": "left" },
    { "time": 0.75, "functionName": "Hit", "objectReferenceParameter": { "guid": "a1b2c3..." },
      "messageOptions": "DontRequireReceiver" }
  ]
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `time` | ✅ | 秒単位の時間 |
| `functionName` | ✅ | アニメーション対象 GameObject のコンポーネントで呼び出すメソッド |
| `stringParameter`, `floatParameter`, `intParameter` | ❌ | パラメータ |
| `objectReferenceParameter` | ❌ | アセットの `{guid}`、または `null` |
| `messageOptions` | ❌ | `RequireReceiver` または `DontRequireReceiver`。既定は `RequireReceiver` で、これはこのエンドポイントの選択ではなく新規イベントに対する Unity の既定値です |

**配列はリスト全体を置き換えます。** Unity はイベントを「要素ごとの識別子を持たない順序付き配列」として保存し、まるごと書き換えます。個々の要素をアドレス指定することは、フォーマットに存在しない識別子を発明することになります。クリアするには `[]` を送るか `DELETE` を使ってください。`events` の省略はクリアではなく `400` です。配列が無いことを「全部消せ」と読むべきではないからです。

すべての要素は 1 つも書き込む前に解析・解決されるため、4 番目の要素が存在しないアセットを指しているリストは何も置き換えません。

### レスポンス

```json
{
  "assetPath": "Assets/Animations/Walk.anim",
  "eventCount": 2,
  "events": [
    {
      "time": 0.25,
      "functionName": "Footstep",
      "stringParameter": "left",
      "floatParameter": 0.0,
      "intParameter": 0,
      "objectReferenceParameter": null,
      "messageOptions": "RequireReceiver"
    },
    {
      "time": 0.75,
      "functionName": "Hit",
      "stringParameter": "",
      "floatParameter": 0.0,
      "intParameter": 0,
      "objectReferenceParameter": { "guid": "a1b2c3...", "name": "HitVfx" },
      "messageOptions": "DontRequireReceiver"
    }
  ]
}
```

イベントは保存された状態のまま、`GET` が返すのと同じ形でエコーされます。したがって `stringParameter` を省略すれば `""`、`messageOptions` を省略すれば `RequireReceiver` が返ります(上の 1 件目がそれです)。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `events` の欠落や不正、要素に `time` / `functionName` が無い、または `messageOptions` が未知 |
| 404 | `objectReferenceParameter` の GUID が解決しない |
| 409 | クリップがインポータによって生成されている |

---

## DELETE /api/assets/animation-clips/{guid}/events

クリップからアニメーションイベントをすべて削除します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### レスポンス

```json
{ "assetPath": "Assets/Animations/Walk.anim", "removed": 2 }
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 404 | 指定 GUID のアセットが見つからない |
| 409 | クリップがインポータによって生成されている |

---

## POST /api/assets/animation-clips/{guid}/curves

AnimationClip に float カーブおよび/またはオブジェクト参照カーブを追加または置換します。`curves` と `objectReferenceCurves` の少なくとも一方の指定が必要です。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimationClip アセットの GUID |

### リクエストボディ — float カーブ

```json
{
  "curves": [
    {
      "relativePath": "Hips",
      "type": "Transform",
      "property": "localPosition.y",
      "keys": [
        { "time": 0.0, "value": 0.0, "inTangent": 0.0, "outTangent": 1.0 },
        { "time": 0.5, "value": 1.0, "inTangent": 0.0, "outTangent": 0.0 },
        { "time": 1.0, "value": 0.0, "inTangent": -1.0, "outTangent": 0.0 }
      ]
    }
  ]
}
```

### リクエストボディ — オブジェクト参照カーブ(例: Sprite の切り替え)

```json
{
  "objectReferenceCurves": [
    {
      "relativePath": "",
      "type": "UnityEngine.UI.Image",
      "property": "m_Sprite",
      "keys": [
        { "time": 0.0,    "guid": "a1b2c3..." },
        { "time": 0.1667, "guid": "d4e5f6..." }
      ]
    }
  ]
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `relativePath` | ✅ | Animator の GameObject からの相対子パス。Animator 自身の GameObject には `""` を使用 |
| `type` | ✅ | C# 型名。短縮名と完全修飾名のどちらでも解決します(`Transform` と `UnityEngine.Transform`、`Image` と `UnityEngine.UI.Image`)。型は `UnityEngine.Object` から派生している必要があり、そうでなければ `Unknown type` を返します |
| `property` | ✅ | シリアライズ済みプロパティパス(例: `localPosition.y`、`m_Sprite`) |
| `keys[].time` | ✅ | 秒単位の時間 |
| `keys[].value` | ✅(float カーブ) | float 値 |
| `keys[].inTangent` / `outTangent` | ❌ | タンジェント(既定: 0) |
| `keys[].guid` | ✅(オブジェクト参照) | 参照先アセットの GUID。Sprite モードのテクスチャの場合、Sprite サブアセットが自動的にロードされます |

> `curves` と `objectReferenceCurves` は同一リクエストで同時に指定できます。

### レスポンス

```json
{
  "added": ["localPosition.y", "m_Sprite"],
  "addedFloat": ["localPosition.y"],
  "addedObjectReference": ["m_Sprite"],
  "errors": []
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | カーブの必須フィールドの欠落、未知の型、または有効なカーブが1つもない |
| 404 | 指定 GUID のアセットが見つからない |
| 403 | Asset Write カテゴリが無効 |

---

## DELETE /api/assets/animation-clips/{guid}/curves

バインディングを指定して AnimationClip からカーブを削除します。float カーブとオブジェクト参照カーブの両方に対応します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimationClip アセットの GUID |

### `property` には `GET` が返す名前を指定します

**書き込み時に指定した名前とは限りません。** 追加と削除で経由する Unity API が異なるためです。

`POST .../curves` は `AnimationClip.SetCurve` 経由で書き込みます。これは Transform のベクタープロパティを全成分に展開します。`localPosition.y` に書いたカーブは `m_LocalPosition.x`、`.y`、`.z` の 3 バインディングとして保存され、指定しなかった成分にはそのプロパティの既定値が、カーブ全長にわたる定数として入ります(position なら `0`、scale なら `1`)。1 軸だけアニメーションさせたつもりでも、残り 2 軸が固定されます。

展開するのは `SetCurve` であって略記ではありません。シリアライズ済みの名前 `m_LocalPosition.y` を渡しても同じように展開されます。対象は Transform の position、scale、euler angles で、`Light.m_Intensity` のようなスカラーや `Light.m_Color.r` のような色の 1 チャンネルはそれぞれ 1 バインディングとして保存されます。

`DELETE .../curves` は `AnimationUtility.SetEditorCurve` 経由で削除します。こちらは厳密で、1 エントリが 1 バインディングを指します。したがって `m_LocalPosition.y` を削除しても `.x` と `.z` は残り、展開されたプロパティをまとめて消すには各成分を列挙する必要があります。

クリップ上のどのバインディングにも一致しない `property` は `errors` に報告され、メッセージにはその相対パスと型にバインドされているプロパティ名が列挙されます。失敗した応答から正しい名前を読み取れます。

### リクエストボディ(JSON)

```json
{
  "bindings": [
    { "relativePath": "Hips", "type": "Transform", "property": "m_LocalPosition.y" },
    { "relativePath": "", "type": "UnityEngine.UI.Image", "property": "m_Sprite" }
  ]
}
```

### レスポンス

```json
{
  "removed": ["m_LocalPosition.y", "m_Sprite"],
  "errors": []
}
```

`removed` に載るのは、呼び出し前に存在し呼び出し後に存在しなくなったバインディングだけです。削除できなかったものは `errors` に報告されます。同一リクエスト内で同じバインディングを複数回指定しても、削除も報告も 1 回だけです。エントリはカーブを 1 本指すものであり、繰り返しても 2 本目が消えるわけではありません。

```json
{
  "removed": [],
  "errors": [
    "No curve bound to 'localPosition.y' on 'Hips' (Transform). Bindings there: m_LocalPosition.x, m_LocalPosition.y, m_LocalPosition.z"
  ]
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `bindings` の欠落・空、または 1 件も削除できず失敗が 1 件以上ある。1 件でも削除できていれば、他のエントリが失敗していても `200` を返し、失敗は `errors` に入ります |
| 404 | 指定 GUID のアセットが見つからない |
| 403 | Asset Write カテゴリが無効 |

---

## POST /api/assets/animator-controllers

デフォルトの Base Layer を含む AnimatorController アセットを作成します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{ "assetPath": "Assets/Animations/Character.controller" }
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `assetPath` | ✅ | 出力先パス(`.controller` で終わる必要があります) |

### レスポンス(HTTP 201)

```json
{
  "assetPath": "Assets/Animations/Character.controller",
  "guid": "a1b2c3...",
  "layerCount": 1
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `assetPath` の欠落、または `.controller` で終わらない |
| 403 | Asset Write カテゴリが無効 |

---

## GET /api/assets/animator-controllers/{guid}

AnimatorController の完全な構造(パラメータ、レイヤー、ステート、トランジション、Any State トランジション)を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimatorController アセットの GUID |

### レスポンス

```json
{
  "assetPath": "Assets/Animations/Character.controller",
  "guid": "a1b2c3...",
  "parameters": [
    { "name": "Speed", "type": "Float", "defaultFloat": 0.0 },
    { "name": "IsGrounded", "type": "Bool", "defaultBool": false }
  ],
  "layers": [
    {
      "name": "Base Layer",
      "index": 0,
      "defaultWeight": 0.0,
      "isBaseLayer": true,
      "blendingMode": "Override",
      "avatarMask": null,
      "iKPass": false,
      "syncedLayerIndex": -1,
      "syncedLayerAffectsTiming": false,
      "defaultState": "Idle",
      "states": [
        {
          "name": "Idle",
          "isDefault": true,
          "tag": "",
          "writeDefaultValues": true,
          "iKOnFeet": false,
          "mirror": false,
          "cycleOffset": 0.0,
          "speed": 1.0,
          "speedParameter": "",
          "speedParameterActive": false,
          "cycleOffsetParameter": "",
          "cycleOffsetParameterActive": false,
          "mirrorParameter": "",
          "mirrorParameterActive": false,
          "timeParameter": "",
          "timeParameterActive": false,
          "position": { "x": 156.0, "y": -48.0 },
          "behaviours": [],
          "motion": {
            "type": "AnimationClip",
            "guid": "d4e5f6...",
            "name": "IdleClip",
            "assetPath": "Assets/Animations/Idle.anim",
            "clipsAtPath": 1
          },
          "transitions": [
            {
              "transitionId": "GlobalObjectId_V1-3-a1b2c3...-14749150960317597279-0",
              "destination": { "type": "State", "name": "Walk" },
              "hasExitTime": false,
              "exitTime": 0.0,
              "duration": 0.25,
              "fixedDuration": true,
              "offset": 0.0,
              "interruptionSource": "None",
              "orderedInterruption": true,
              "canTransitionToSelf": true,
              "mute": false,
              "solo": false,
              "conditions": [
                { "parameter": "Speed", "mode": "Greater", "threshold": 0.1 }
              ]
            }
          ]
        }
      ],
      "anyStateTransitions": [],
      "entryTransitions": [],
      "stateMachineTransitions": [],
      "behaviours": [],
      "stateMachines": [
        {
          "name": "Combat",
          "path": ["Combat"],
          "position": { "x": 300.0, "y": 60.0 },
          "defaultState": null,
          "states": [],
          "anyStateTransitions": [],
          "entryTransitions": [
            {
              "transitionId": "GlobalObjectId_V1-3-a1b2c3...-1355314737468677203-0",
              "from": { "type": "Entry" },
              "destination": { "type": "StateMachine", "name": "Melee" },
              "solo": false,
              "mute": false,
              "conditions": []
            }
          ],
          "stateMachineTransitions": [],
          "behaviours": [],
          "stateMachines": []
        }
      ]
    }
  ]
}
```

### モーション

すべての `motion` は `type` を持ちます。モーションが設定されていないステートは `"motion": null` です。

| `type` | 意味 |
|--------|---------|
| `AnimationClip` | モーションはクリップ。`guid` はクリップを含むアセットを指し、クリップ自体を一意に指すのは `clipsAtPath` が `1` のときだけ |
| `BlendTree` | モーションはこのコントローラーが所有するブレンドツリー。`guid` は常に `null` |
| `Unknown` | このバージョンが記述できない `Motion` 派生型。上の 2 つのどちらかであるかのように見せず、そのまま報告する。`guid` はそのモーションがパス上のメインアセットである場合にのみ非 null |

削除されたモーションアセットは `Unknown` ではなく `"motion": null` になります。型を調べる前に、Unity が失われた参照を null として解決するためです。

#### AnimationClip

| フィールド | 説明 |
|-------|-------------|
| `guid` | クリップを含むアセットの GUID。未保存のクリップでは `null` |
| `name` | クリップ名 |
| `assetPath` | クリップを含むアセットのパス。未保存のクリップでは `null` |
| `clipsAtPath` | `assetPath` から到達できる AnimationClip の数。`assetPath` が `null` のときは数える対象が無いため**キーごと出力されません** |

`clipsAtPath` は `guid` の精度を示します。モデルファイルからインポートされたクリップはそのファイルの中にあるため、GUID が指すのはクリップではなく**ファイル**です。`clipsAtPath` が `1` なら GUID は一意に定まります。`1` より大きい場合、`GET /api/assets/animation-clips/{guid}` はインポーターが最初に列挙したクリップを返し、他のテイクは GUID では参照できません。

#### BlendTree

ブレンドツリーはコントローラーのサブアセットであり GUID を持たないため、別途取得させるのではなくインラインで構造を返します。

| フィールド | 説明 |
|-------|-------------|
| `blendType` | `Simple1D`、`SimpleDirectional2D`、`FreeformDirectional2D`、`FreeformCartesian2D`、`Direct` のいずれか |
| `blendParameter` | ブレンドを駆動するパラメータ。2D タイプでは X 軸 |
| `blendParameterY` | Y 軸を駆動するパラメータ。2D タイプでのみ参照される |
| `useAutomaticThresholds` | Unity が子のしきい値を自動計算するか。`Simple1D` でのみ参照される |
| `minThreshold`, `maxThreshold` | しきい値の範囲。`Simple1D` でのみ参照される |
| `children` | 子モーション(順序どおり) |

上の表のフィールドは、`blendType` に関わらずすべてのブレンドツリーで返されます。ブレンドが実際に参照するかどうかに関わらず Unity が値を保持しているためで、`blendParameterY` としきい値系のフィールドも、`Direct` だけが使う子の `directBlendParameter` も同様です。「参照される」はどのブレンドタイプがその値を読むかを示すものであって、どのタイプで出力されるかではありません。フィールドが出力されない唯一のケースは深さ上限に達したツリーで、その場合は代わりに `truncated` が付きます。

各子は `threshold`、`position`(`{x, y}`、2D タイプで使用)、`timeScale`、`cycleOffset`、`mirror`、`directBlendParameter`、そして上記とまったく同じ形の `motion` を持ちます。入れ子のブレンドツリーも他と同じように記述されます。

```json
"motion": {
  "type": "BlendTree",
  "guid": null,
  "name": "Locomotion",
  "blendType": "Simple1D",
  "blendParameter": "Speed",
  "blendParameterY": "Blend",
  "useAutomaticThresholds": true,
  "minThreshold": 0.0,
  "maxThreshold": 0.8,
  "children": [
    {
      "threshold": 0.0,
      "position": { "x": 0.0, "y": 0.0 },
      "timeScale": 1.0,
      "cycleOffset": 0.0,
      "mirror": false,
      "directBlendParameter": "Blend",
      "motion": { "type": "AnimationClip", "guid": "...", "name": "Walk", "assetPath": "...", "clipsAtPath": 1 }
    }
  ]
}
```

入れ子は深さ 10 まで返します。その深さにあるブレンドツリーは `"truncated": true` を持ち、`children` を**持ちません**。境界と葉を区別できるようにするためで、空の `children` 配列は文字どおり「子が無い」という意味のまま残ります。

### サブステートマシン

レイヤーのルートステートマシンと、その中に入れ子になったすべてのマシンは同じフィールドを持ちます。クライアントが 2 種類ではなく 1 種類の構造をたどれるようにするためです。ルートの `name` と `position` はレイヤーのもの(ルートはレイヤーそのもの)で、入れ子のマシンは自分のものを返します。

| フィールド | 説明 |
|---|---|
| `name` | マシン名。`path` の 1 セグメントでもあります |
| `path` | レイヤールートからこのマシンまでの名前の配列。リクエストの `stateMachinePath` と同じもの |
| `position` | 親マシン内のグラフ位置 `{x, y}` |
| `defaultState` | 開始ステート名、または `null` |
| `states` | [ステートのフィールド](#ステートのフィールド)を参照 |
| `stateMachines` | 入れ子のマシン。同じ形 |
| `anyStateTransitions` | **このマシンの** AnyState トランジション。マシンごとに固有です |
| `entryTransitions` | このマシンの Entry ノードから出るトランジション。[ステートマシン間のトランジション](#ステートマシン間のトランジション)を参照 |
| `stateMachineTransitions` | このマシンに入れ子になったマシンから出るトランジション |
| `behaviours` | アタッチされた `StateMachineBehaviour` の型名。ステートと同じく**読み取り専用** |

### `stateMachinePath`

ステートを名前で指定するすべてのエンドポイントが `stateMachinePath` を受け付けます。レイヤールートからのステートマシン名の配列です:

```json
{ "layerIndex": 0, "stateMachinePath": ["Combat", "Melee"], "name": "Swing" }
```

省略または `[]` はレイヤーのルートステートマシンを意味します。これはこのフィールドが存在する前のすべてのリクエストが意味していたものなので、動いていたリクエストの意味は変わりません。

**`/` 区切りの文字列ではなく配列です。** Unity はステートマシン名に `/` を禁止していないため、連結したパスにはエスケープ規則が必要になり、エスケープ規則はクライアントが静かに間違えるものです。配列なら衝突する区切り文字がありません。

読み取りレスポンスは同じ配列を `path` として返すので、レスポンスから読んだパスをそのままリクエストに送れます。

Unity は兄弟のマシンが同じ名前を持つことを許します(この API 経由では作成を拒否しますが、Animator ウィンドウでのリネームでは起こり得ます)。その組に到達したパスは、どちらかを黙って選ばずに、曖昧さを示す `409` を返します。

**ステートマシンをリネームすると、クライアントが保持しているパスはすべて無効になります。** その中のステートに対して保持しているパスも同様です。トランジションと違いステートマシンには安定した ID がないため、リネーム後はコントローラーを読み直してください。

### ステートマシン間のトランジション

Unity の型が 2 つ関わっており、レスポンスは両者を区別します。

`AnimatorStateTransition` はステート同士をつなぎます。`states[].transitions` と `anyStateTransitions` が持つのがこれで、[トランジションのフィールド](#トランジションのフィールド)に記載したタイミングや割り込みのフィールドを持ちます。

`AnimatorTransition` はステートマシンをつなぎます。`entryTransitions` と `stateMachineTransitions` が持つのがこれで、**遷移元・遷移先・`solo`・`mute`・`conditions` だけ**を持ちます。`hasExitTime`、`duration`、`offset`、割り込み系は存在せず、0 としても出力しません。`"duration": 0` は「型に無いフィールド」ではなく「設定値」として読まれてしまうためです。同じ仕組みによる `transitionId` を持ちます。

| フィールド | 説明 |
|---|---|
| `transitionId` | `DELETE .../state-machine-transitions` のアドレス |
| `from` | エントリトランジションは `{"type": "Entry"}`、入れ子マシンから出るものは `{"type": "StateMachine", "name": "..."}` |
| `destination` | 下記参照 |
| `solo`, `mute` | シリアライズされた値 |
| `conditions` | `{parameter, mode, threshold}` の配列 |

### `destination`

どちらの型のトランジションも、遷移先を名前ではなく判別子つきのオブジェクトとして返します:

| `type` | 意味 |
|---|---|
| `State` | `name` は同じマシン内のステート |
| `StateMachine` | `name` はステートマシン。入るとそのマシンの Entry から開始します |
| `Exit` | そのマシンの Exit ノード。`name` はありません |
| `None` | 遷移先が削除済み。欠落フィールドに見える `null` ではなく、そう報告します |

名前だけでは何を指しているか分かりません。遷移先がステートにもステートマシンにもなり得る以上、`"Melee"` はあるコントローラーではステート、別のコントローラーではステートマシンであり、レスポンスをたどるクライアントには区別できません。これは `motion` フィールドが既に従っている方針です。

**`Entry` は値に含まれません。** Unity には Entry ノードを遷移先とする経路がなく、ステートマシンに入ることは `StateMachine` 型の遷移先として表現され、Entry ノードはエントリトランジションの*遷移元*としてのみ現れます。何も生成できない値は、無いことより悪くなります。

### ステートのフィールド

| フィールド | 説明 |
|---|---|
| `name` | ステート名。`PATCH` と `DELETE` のアドレスでもあります |
| `isDefault` | そのレイヤーのデフォルトステートかどうか |
| `tag` | ランタイムが `AnimatorStateInfo.IsTag` で照合する文字列 |
| `writeDefaultValues` | そのステートがアニメーションさせないプロパティを既定値に戻すかどうか |
| `iKOnFeet` | Foot IK |
| `mirror` | モーションを左右反転して再生するかどうか |
| `cycleOffset` | モーションのサイクルに対する正規化オフセット |
| `speed` | 再生速度。**`speedParameterActive` が `true` のときは実効値ではありません** |
| `speedParameter`, `cycleOffsetParameter`, `mirrorParameter`, `timeParameter` | 各値を駆動するパラメータ |
| `speedParameterActive`, `cycleOffsetParameterActive`, `mirrorParameterActive`, `timeParameterActive` | そのオーバーライドが有効かどうか |
| `position` | Animator ウィンドウのグラフ上の位置。下記参照 |
| `behaviours` | アタッチされている `StateMachineBehaviour` の型名。**読み取り専用。** スクリプトが失われているものは `null` |
| `motion` | [モーション](#モーション)を参照 |
| `transitions` | [トランジションのフィールド](#トランジションのフィールド)を参照 |

各 `*Parameter` は、無効なときに空文字へ畳まず、対応する `*Active` フラグと並べて返します。Unity は両方を保存しており、無効なパラメータ名もアセットが保持する内容です。ステートを再現するクライアントにはそれが必要です。

#### `position` はグラフ上のレイアウト

`position` はステートのプロパティではありません。ステートマシンの配列の要素である `ChildAnimatorState` が持っており、Animator ウィンドウはこれを読んでグラフを配置します。つまり書き込むとノードが移動します。API だけで組み立てたコントローラーは、これが無いと全ステートが原点に重なります。

Unity のフィールドは `Vector3` ですが、グラフは平面です。`z` はそこでは使われないため返しません。書き込みは `x` と `y` を設定し、`z` は保持します。

#### `behaviours` は読み取り専用

スクリプトを実行するステートとそうでないステートを区別できるように、アタッチされているものを返します。アタッチ操作は提供しません。スクリプト型を解決してコントローラーのサブアセットとして生成する必要があり、それ自体が別の所有権の問題だからです。リクエストボディの `behaviours` は無視せず `unsupported` に報告します。

### トランジションのフィールド

ステートのトランジションも AnyState のトランジションも、次のフィールドを持ちます。

| フィールド | 説明 |
|---|---|
| `transitionId` | このトランジションの安定したアドレス。[トランジションのアドレス指定](#トランジションのアドレス指定)を参照 |
| `destination` | 判別子つきの遷移先。[`destination`](#destination) を参照 |
| `hasExitTime` | Exit Time で遷移するかどうか |
| `exitTime` | Exit Time が発火する正規化時間 |
| `duration` | ブレンド時間。**`fixedDuration` が `true` なら秒、`false` なら遷移元ステートに対する割合** |
| `fixedDuration` | `AnimatorStateTransition.hasFixedDuration`。Unity が新規トランジションに与える既定値は `true` |
| `offset` | 遷移先ステートでの正規化された開始オフセット |
| `interruptionSource` | `None`, `Source`, `Destination`, `SourceThenDestination`, `DestinationThenSource` |
| `orderedInterruption` | 割り込みがトランジションの順序に従うかどうか |
| `canTransitionToSelf` | 参照されるのは AnyState トランジションのみ。値自体はすべてのトランジションで保存され、返されます |
| `mute`, `solo` | シリアライズされた値そのもの。Animator ウィンドウがレイヤー全体から算出する結果は返しません |
| `conditions` | `{parameter, mode, threshold}` の配列 |

`duration` と `fixedDuration` は必ず一緒に返されます。どちらも単独では意味が定まらず、同じ数値が一方では秒、他方では遷移元ステートに対する割合になるためです。

### トランジションのアドレス指定

1 つのステートの組は任意の個数のトランジションを持てます。条件セットごとに経路を分けるのが通常の作り方だからです。したがって `from` と `to` の組は、それが 1 本だけのあいだしかアドレスになりません。`transitionId` は常にちょうど 1 本を指します。

この ID は、コントローラーのサブアセットであるトランジションに対する Unity の `GlobalObjectId` です。6000.0.80f1 で確認した挙動:

- ドメインリロード後も同じトランジションに解決されます
- ステートの `transitions` 配列を並べ替えても、位置ではなくトランジション自体に追従します
- `POST` が作成した直後、`SaveAssets` の前でもすでに有効です
- トランジションを削除すると解決されなくなります。古い ID が誤って別のトランジションに当たるのではなく `404` になるのはこのためです

内容は不透明な文字列として扱い、トランジションを削除した後は読み直してください。

### このレスポンスが記述しないもの

このレスポンスが記述するのは**アセット**であり、動作中の Animator ではありません。ランタイムの面はありません。Play mode で Animator が実際にいるステート、その正規化時間、実効のレイヤーウェイトを読むエンドポイントはなく、パラメータを駆動したり `CrossFade` を呼ぶものもありません。`defaultWeight` は格納された値で、ベースレイヤーでは実効ウェイトと一致しません。[ベースレイヤーの `defaultWeight`](#ベースレイヤーの-defaultweight) を参照してください。

このほかに 2 つの制限がありますが、いずれも隠されずに報告され、該当箇所で説明しています。

- `behaviours` は型名だけを返します。ステートでもステートマシンでも同じです。[`behaviours` は読み取り専用](#behaviours-は読み取り専用)を参照してください。
- ブレンドツリーとステートマシンのネストは深さ 10 までシリアライズされます。その深さのノードは中身の代わりに `"truncated": true` を持つため、境界を「中身が空」と取り違えることはありません。

`mute` と `solo` はシリアライズされた値そのものです。Animator ウィンドウがレイヤー全体から計算する結果は報告しません。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | アセットが AnimatorController でない |
| 404 | 指定 GUID のアセットが見つからない |

---

## POST /api/assets/animator-controllers/{guid}/parameters

パラメータを追加します。既に存在する場合は更新します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimatorController アセットの GUID |

### リクエストボディ(JSON)

```json
{ "name": "Speed", "type": "Float", "defaultValue": 0.0 }
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `name` | ✅ | パラメータ名 |
| `type` | ✅ | `Float`、`Int`、`Bool`、または `Trigger` |
| `defaultValue` | ❌ | 既定値。`Trigger` では保存されません(下記参照) |

**同じ型で**既に存在するパラメータはその場で更新され、パラメータ配列内の位置も保たれます。破棄して作り直すのは `type` が変わる場合だけで、そのときはすべての参照が孤立します。[パラメータの参照](#パラメータの参照)を参照してください。

すべての値は作成や置換の前に検証されます。したがって `400` で拒否されたリクエストはパラメータを追加せず、型も変えません。

リネームには [`PATCH`](#patch-apiassetsanimator-controllersguidparameters) を使います。

### レスポンス(HTTP 201)

```json
{ "added": "Speed", "type": "Float", "unsupported": [] }
```

型が変わった場合は `"replacedType": true` と、[パラメータの参照](#パラメータの参照)と同じ形の `orphanedReferences` 配列も返します。

### Trigger の `defaultValue`

Trigger は 1 フレーム内でセットされ消費されるため、Unity は既定値を保持しません。リクエストは拒否せず、フィールドを `unsupported` に列挙します。以前は黙って捨てたうえで、適用したかのように `201` を返していました。

---

## PATCH /api/assets/animator-controllers/{guid}/parameters

パラメータをその場でリネーム、または既定値を設定します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{ "name": "Speed", "newName": "MoveSpeed", "defaultValue": 0.5 }
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `name` | ✅ | 変更対象のパラメータ |
| `newName` | ❌ | 新しい名前。すべての参照がこの名前に書き換わります |
| `defaultValue` | ❌ | 新しい既定値 |

`newName` と `defaultValue` はどちらか一方でも両方でも指定できます。いずれの場合もパラメータの配列内の位置は保たれます。

**リネームはアトミックであり、リネームと既定値の設定を同時に行うリクエストもアトミックです。** 最初の書き込みの前にすべての値を解析し、すべての検査を終えるため、名前衝突でも不正な `defaultValue` でも、拒否されたリクエストはパラメータも参照もそのまま残します。中途半端に適用されたリネームこそ、このエンドポイントが防ごうとしている破損そのものです。

### レスポンス

```json
{
  "name": "MoveSpeed",
  "type": "Float",
  "renamed": { "from": "Speed", "to": "MoveSpeed" },
  "referencesUpdated": 3,
  "references": [
    { "kind": "condition", "layerIndex": 0, "stateMachinePath": ["Combat"], "transitionId": "GlobalObjectId_V1-3-...", "conditionIndex": 0 },
    { "kind": "blendParameter", "layerIndex": 0, "stateMachinePath": [], "state": "Locomotion", "childPath": [0] },
    { "kind": "speedParameter", "layerIndex": 0, "stateMachinePath": [], "state": "Run" }
  ],
  "unsupported": []
}
```

件数だけでは足りません。リネームしたクライアントは各サイトを確認できる必要があり、参照があるはずなのに 0 が返ってきたなら、コントローラーかこの実装のどちらかにバグがあるということです。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `name` の欠落、`newName` が空、値が不正、`type` が送られた、またはボディに未知のフィールドがある |
| 404 | パラメータが見つからない |
| 409 | `newName` が既存のパラメータ名と衝突。何も変更されません |

### `type` はここでは変更できません

型を変えると、そのパラメータを名指しするすべての条件が無効になります。しかもクライアントの代わりに解決できる規則がありません。閾値 0.1 の `Greater` は Float についての文であり、パラメータが Trigger になった時点で読み方そのものが存在しなくなります。`type` フィールドは無視せず、その理由とともに拒否します。`DELETE` してから `POST` するのが正直な経路で、どちらも変更が孤立させる参照を報告します。

---

## パラメータの参照

パラメータは 4 種類のサイトから名前で参照されており、**そのどれも Unity が保守する参照ではありません**。すべてただの文字列です:

| `kind` | 場所 | 追加フィールド |
|---|---|---|
| `condition` | ステート / AnyState / Entry / ステートマシンの各トランジションの `AnimatorCondition.parameter` | `transitionId`, `conditionIndex` |
| `blendParameter`, `blendParameterY` | ブレンドツリー(入れ子を含む) | `state`, `childPath` |
| `speedParameter`, `cycleOffsetParameter`, `mirrorParameter`, `timeParameter` | ステートのオーバーライド | `state` |

すべての参照が `layerIndex` と `stateMachinePath` を持つので、サブステートマシン内のサイトも報告からアドレス指定できます。

Unity 6000.0.80f1 で計測: `parameters` 配列を書き換えて代入するリネームは、パラメータ名だけを変え、上記の文字列は**すべて**存在しない名前を指したまま残ります。コントローラーは読み込め、条件はシリアライズもされ、そして二度と評価されません。`PATCH` がそれらを書き換える理由であり、`DELETE` + `POST` によるリネームが等価でない理由でもあります。

`DELETE` と `POST` による型変更は、孤立させる参照を同じ形で報告します。処理自体は実行されます(パラメータを失った条件が何になるべきかは、この API が決められる事柄ではありません)が、もう黙って行われることはありません。

過去の削除で既に孤立している条件は修復しません。`GET /api/assets/animator-controllers/{guid}` の条件と `parameters` を突き合わせれば見つけられます。

---

## DELETE /api/assets/animator-controllers/{guid}/parameters

名前を指定してパラメータを削除し、孤立させる参照を報告します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimatorController アセットの GUID |

### リクエストボディ(JSON)

```json
{ "name": "Speed" }
```

### レスポンス

```json
{
  "removed": "Speed",
  "orphanedReferences": 2,
  "references": [
    { "kind": "blendParameter", "layerIndex": 0, "stateMachinePath": [], "state": "Locomotion", "childPath": [] },
    { "kind": "condition", "layerIndex": 0, "stateMachinePath": [], "transitionId": "GlobalObjectId_V1-3-...", "conditionIndex": 0 }
  ]
}
```

パラメータはいずれにせよ削除されます。条件を放置する理由は[パラメータの参照](#パラメータの参照)を参照してください。

### エラー

| ステータス | 原因 |
|--------|-------|
| 404 | パラメータが見つからない |

---

## レイヤーのフィールド

レイヤーは名前ではなく `layerIndex` で指定します。Unity はレイヤー名の一意性を保証せず、ステートとトランジションのエンドポイントも既にインデックスでレイヤーを指定しているためです。

| フィールド | 説明 |
|-------|-------------|
| `name` | レイヤー名。一意ではなく、アドレスにもなりません |
| `index` | コントローラー内の位置 |
| `defaultWeight` | `AnimatorControllerLayer.defaultWeight` そのまま。**ベースレイヤーでは実効ウェイトではなく**、**クランプもされません** — 下記参照 |
| `isBaseLayer` | レイヤー 0 のとき true |
| `blendingMode` | `Override` または `Additive` |
| `avatarMask` | `null`、または `AvatarMask` アセットの `{guid, name}`。ブレンドツリーと違いマスクは通常のアセットなので、GUID から取得できます |
| `iKPass` | このレイヤーが IK パスを実行するか |
| `syncedLayerIndex` | ステートマシンを借りる先のレイヤーのインデックス。同期していなければ `-1` |
| `syncedLayerAffectsTiming` | 同期しているレイヤーでのみ参照されます |

### ベースレイヤーの `defaultWeight`

レイヤー 0 では `defaultWeight` は実効ウェイトではありません。ベースレイヤーはこの値に関わらず実行時ウェイト 1 で動作し、Animator ウィンドウにもウェイトのスライダーは表示されません。作成直後のコントローラーは、完全に有効なレイヤーに対して `"defaultWeight": 0` を返します。このフィールドはシリアライズされた値の忠実な読み取りであり、その値が参照されないことをクライアントに伝えるのが `isBaseLayer` です。Unity の規則を知らなくても判断できます。

`effectiveWeight` は意図的に用意していません。実行時ウェイトはアセットではなく生きた `Animator` の性質であり、ここで計算すると推測を読み取り結果として提示することになります。

### `defaultWeight` はクランプされません

意味を持つ範囲は 0 〜 1 ですが、それを強制する仕組みはありません。6000.0.80f1 での実測では、Unity は `5` も `-2` もそのまま格納し、そのまま読み戻します。したがってこのエンドポイントも拒否しません。拒否すると API がアセットや Inspector のデータモデルより狭くなるためで、`effectiveWeight` を用意しない理由と同じです。0〜1 の外の値もそのまま往復します。実行時にどう扱われるかは Unity の領分です。

---

## POST /api/assets/animator-controllers/{guid}/layers

AnimatorController にレイヤーを追加します。`PATCH` が受け付ける設定はすべてここでも指定できるため、マスク付きレイヤーの作成が「作成してから更新」ではなく 1 リクエストで済みます。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimatorController アセットの GUID |

### リクエストボディ(JSON)

```json
{ "name": "Arms", "defaultWeight": 1.0, "avatarMask": { "guid": "a1b2c3..." } }
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `name` | ✅ | レイヤー名 |
| `defaultWeight` | ❌ | レイヤーの既定ウェイト。意味を持つのは 0〜1 ですが、**クランプされません**(下記参照)。追加レイヤーの既定値は 0 |
| `weight` | ❌ | `defaultWeight` の別名として受け付けます |
| `blendingMode` | ❌ | `Override` または `Additive` |
| `avatarMask` | ❌ | `AvatarMask` アセットの `{guid}` |
| `iKPass` | ❌ | IK パスを実行するか |
| `syncedLayerIndex` | ❌ | 受け付ける値は `PATCH` を参照 |
| `syncedLayerAffectsTiming` | ❌ | 同期レイヤーでのみ意味を持ちます |

### レスポンス(HTTP 201)

```json
{ "added": "Arms", "layerIndex": 1, "applied": ["defaultWeight", "avatarMask"] }
```

`applied` は実際に設定されたフィールドを列挙します。設定が 1 つでも拒否された場合は `400` を返し、**レイヤーは作成されません**。要求の一部だけが反映されたレイヤーを残すのではなく、作成ごと取り消します。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `name` の欠落、または設定値が不正 |
| 404 | 指定 GUID のアセットが見つからない、または `avatarMask.guid` が `AvatarMask` を指していない |
| 403 | Asset Write カテゴリが無効 |

---

## PATCH /api/assets/animator-controllers/{guid}/layers

レイヤーを 1 つ更新します。`layerIndex` 以外はすべて省略可能で、**省略したフィールドは変更されません**。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{ "layerIndex": 1, "defaultWeight": 0.5, "avatarMask": null }
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `layerIndex` | ✅ | 更新対象のレイヤー |
| `name`、`defaultWeight`、`weight`、`blendingMode`、`iKPass`、`syncedLayerAffectsTiming` | ❌ | 指定されたときのみ設定 |
| `avatarMask` | ❌ | `{guid}` で設定、明示的な `null` で解除。省略した場合はマスクをそのまま維持します。ここでは `null` と省略は別の意味です |
| `syncedLayerIndex` | ❌ | 同期しないなら `-1`、または他のレイヤーのインデックス |

### レスポンス

```json
{ "layerIndex": 1, "applied": ["defaultWeight", "avatarMask"] }
```

### `syncedLayerIndex` は Unity に渡す前に検証します

不正な値は素通しせず `400` で拒否します。Unity は不正な値を拒否するのではなく、コントローラーを壊すことで応答するためです。6000.0.80f1 での実測では、レイヤーを**自分自身**に向けるとエラーも例外もなくレイヤーが 1 つ消え(3 レイヤーが 2 レイヤーになる)、**最終インデックスの 1 つ先**を代入すると Editor がクラッシュしました。そのため範囲外の挙動はこれ以上特性化せず、正当な側から境界を定めています。再現には Editor のセッションを 1 つ失う必要があるためです。

受け付ける値: `-1`、または自分自身のインデックスを除く `0` 〜 `layerCount - 1`。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `layerIndex` の欠落・範囲外、不正な `syncedLayerIndex`、未知の `blendingMode`、オブジェクトでも `null` でもない `avatarMask` |
| 404 | 指定 GUID のアセットが見つからない、または `avatarMask.guid` が `AvatarMask` を指していない |
| 403 | Asset Write カテゴリが無効 |

---

## DELETE /api/assets/animator-controllers/{guid}/layers

レイヤーを 1 つ削除します。そのレイヤーの `AnimatorStateMachine` はコントローラーが所有するサブアセットで、一緒に破棄されます。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{ "layerIndex": 1 }
```

### レスポンス

```json
{ "removed": "Arms", "layerIndex": 1, "layerCount": 1 }
```

### レイヤー 0 は削除できません

`AnimatorController.RemoveLayer(0)` は拒否しません。6000.0.80f1 での実測では、ベースレイヤーを削除して次のレイヤーを繰り上げ、レイヤーが 1 つしかないコントローラーでは**レイヤー 0 個**の状態になります。これは他のどのエンドポイントでも修復できません。そのため `400` を返します。

### 他のレイヤーが同期しているレイヤーは削除できません

レイヤーを削除すると、それより大きいインデックスはすべて 1 つずつ繰り下がりますが、そのレイヤーを指していた `syncedLayerIndex` は補正されません。参照が別のレイヤーを指してしまうことも、自分自身を指してしまうこともあり、後者は前述のとおりレイヤーが黙って消えるケースです。このようなリクエストは、妨げになっているレイヤーを名指しして `400` を返します。先にそのレイヤーの `syncedLayerIndex` を解除してください。

削除対象のレイヤー**自身**が同期している場合は問題ありません。削除前に同期を解除します。`RemoveLayer` は同期レイヤーのステートマシンを破棄しないため、解除しないとどのレイヤーからも参照されないステートマシンがアセットに残ります。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `layerIndex` の欠落・範囲外・`0`、または他のレイヤーが同期しているレイヤー |
| 404 | 指定 GUID のアセットが見つからない |
| 403 | Asset Write カテゴリが無効 |

---

## ブレンドツリー

ブレンドツリーは GUID を持ちません。コントローラーが所有するサブアセットなので、位置で指定します。

```json
{ "layerIndex": 0, "state": "Locomotion", "childPath": [1] }
```

| フィールド | 説明 |
|-------|-------------|
| `layerIndex` | ステートを含むレイヤー。既定は `0` |
| `state` | ルートブレンドツリーをモーションに持つステート名 |
| `childPath` | ルートからの子インデックス。`[]` または省略でルート自身、`[1]` で 2 番目の子、`[1, 0]` でそのさらに最初の子 |

### `childPath` は位置指定です

子を削除・並べ替えすると、クライアントが保持しているパスは無効になります。これはここでの設計判断ではなくアセットの性質です。Unity は `ChildMotion` にインデックス以外の同一性を与えておらず、鍵にできる名前も保持すべき id もありません。独自に発明すると、`.controller` が保持していない対応表を維持することになります。

解決できない `childPath` は、失敗したインデックスと深さを名指しして `404` を返します。古くなったパスは、どこでずれたかが分かる形で失敗します。

---

## POST /api/assets/animator-controllers/{guid}/blend-trees

既存ステートのモーションとしてブレンドツリーを作成するか、既存のツリーに子を追加します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### ルートツリーの作成

`addChild` を指定しない場合、ステートのルートブレンドツリーを作成します。

```json
{ "layerIndex": 0, "state": "Locomotion", "name": "Locomotion",
  "blendType": "Simple1D", "blendParameter": "Speed",
  "useAutomaticThresholds": false, "minThreshold": 0, "maxThreshold": 0.8 }
```

既にブレンドツリーを持つステートは `409` を返します。先に削除するか、`addChild` を使ってください。

### 子の追加

`addChild` を指定すると、`childPath` が指すツリーに子を追加します。既定では入れ子のブレンドツリー、`motion` に GUID があればクリップです。

```json
{ "layerIndex": 0, "state": "Locomotion", "childPath": [], "addChild": true,
  "name": "Runs", "blendType": "Simple1D", "blendParameter": "Direction", "threshold": 0.8 }
```

```json
{ "layerIndex": 0, "state": "Locomotion", "childPath": [0], "addChild": true,
  "motion": { "guid": "a1b2c3..." }, "threshold": -1 }
```

入れ子ツリーの作成手段は `addChild` だけです。ツリーをリテラルとして渡す方法は用意していないため、サブアセットが生まれる経路はちょうど 1 つです。

### フィールド

| フィールド | 対象 | 説明 |
|-------|-----------|-------------|
| `name`、`blendType`、`blendParameter`、`blendParameterY`、`useAutomaticThresholds`、`minThreshold`、`maxThreshold` | ツリー | `blendType` は `Simple1D`、`SimpleDirectional2D`、`FreeformDirectional2D`、`FreeformCartesian2D`、`Direct` のいずれか |
| `threshold`、`position`、`timeScale`、`cycleOffset`、`mirror`、`directBlendParameter` | 子エントリ | `addChild` 指定時のみ |
| `motion` | 子エントリ | `AnimationClip` の `{guid}`。これが無いことが「子は入れ子ツリー」を意味します |

### レスポンス(HTTP 201)

```json
{ "created": "BlendTree", "layerIndex": 0, "state": "Locomotion",
  "childPath": [1], "name": "Runs", "ignored": [] }
```

`childPath` は作成されたツリーまたは子の位置で、そのまま次のリクエストの指定に使えます。

---

## PATCH /api/assets/animator-controllers/{guid}/blend-trees

指定したツリーを更新します。`childPath` が空でない場合は、その子エントリも更新します。

```json
{ "layerIndex": 0, "state": "Locomotion", "childPath": [1], "threshold": 0.8 }
```

フィールドは `POST` と同じで、加えて子が保持するモーションを差し替える `motion` があります。

### 子はブレンドツリーである必要はありません

`childPath` はクリップを保持する子も指定できます。子フィールド(`threshold`、`position`、`timeScale`、`cycleOffset`、`mirror`、`directBlendParameter`、`motion`)は親側のエントリに属し、エントリが何を保持しているかとは無関係なので、どちらでも適用されます。実際のツリーでは子の大半がクリップです。

ツリーのフィールドはそうではありません。クリップを保持する子に対して指定した場合、黙って捨てるのではなく `400` を返します。

| リクエスト | 結果 |
|---|---|
| `childPath` が空のまま `threshold` | `400` — ルートツリーは何かの子ではない |
| クリップを保持する子にツリーのフィールド | `400`(不一致を名指し) |
| `motion` とツリーのフィールドを同時指定 | `400` — 同じリクエストが破棄するツリーに書き込むことになるため |

### `motion` は置き換えた対象を破棄します

子のモーションを差し替えると、元の保持物は外れます。それがブレンドツリーだった場合、Unity は子を削除したときと同じようにアセット内に残すため、ここで部分木ごと破棄します。子の `DELETE` と同じ後始末です。

### 失敗したリクエストは何も適用しません

最初の書き込みの前に、ツリーのフィールドも子のフィールドも含めてすべての値を解決します。したがって複数フィールドを設定して 1 つが失敗したリクエストは、ツリーを元のまま残します。`name` と存在しない `blendParameter` を同時に指定した場合はどちらも変更されず、`addChild` の `POST` が子フィールドで失敗した場合は子もサブアセットも作られません。

---

## DELETE /api/assets/animator-controllers/{guid}/blend-trees

指定したツリーまたは子を削除します。

```json
{ "layerIndex": 0, "state": "Locomotion", "childPath": [1] }
```

`childPath` が空または省略ならステートのモーションを解除し、空でなければその子を削除します。

```json
{ "removed": "child", "layerIndex": 0, "state": "Locomotion",
  "childPath": [1], "destroyedSubTrees": 2 }
```

`destroyedSubTrees` は子とともに破棄したブレンドツリーの数です。このフィールドが必要な理由は次節のとおりです。

---

## サブアセットの後始末

ブレンドツリーは `.controller` ファイルの中に存在するため、グラフから外すこととアセットから消えることは別です。Unity 6000.0.80f1 で、API 自身の読み取りではなくファイルに対して計測した結果:

| 操作 | Unity の挙動 | UnionAir の対応 |
|---|---|---|
| ステートのモーション解除 | ツリーと**すべての子孫**を破棄する | 何もしません。ここに後始末を足すと何もしないコードになります |
| 子の `DELETE` | エントリを外し、**部分木をファイルに残す** | エントリを外す前に部分木を収集し、外した後に破棄します |
| ツリーを持つステートの `DELETE .../states` | ステート自身のツリーは破棄するが、**子孫は破棄しない** | 先に部分木を収集し、生き残ったものを破棄して `destroyedBlendTrees` で報告します |

3 行目が `DELETE .../states` に件数を返すようになった理由です。平坦なブレンドツリーは Unity が正しく片付けるため、この漏れは入れ子にして初めて現れます。1 段のツリーだけでテストしていれば成功と報告されていました。

作成するサブアセットには `HideFlags.HideInHierarchy` を設定します。Animator ウィンドウが生成するものと同じです。`BlendTree.CreateBlendTreeChild` は自動で設定しますが、既存ステートにルートツリーを作る唯一の経路である `AssetDatabase.AddObjectToAsset` は設定しないため、明示的に設定しています。

---

## 検証

- `blendParameter` と `blendParameterY` は、コントローラーに存在する `Float` パラメータを指す必要があります。存在しないパラメータを指すツリーは壊れたコントローラーであり、読み取りでは正常なものと区別できないため、格納せず `400` を返します。
- 未知の `blendType` は、受け付ける値を名指しして `400` を返します。
- 解決できない `childPath` は `400` ではなく `404` です。

### 格納されるが参照されないフィールド

一部のフィールドは特定のブレンドタイプでのみ意味を持ちます。それらは格納したうえで(Unity も格納し、読み取りも返すため、拒否すると API がアセットより狭くなります)、黙って無視されないよう `ignored` に列挙します。

```json
{ "created": "AnimationClip", "childPath": [1, 0], "ignored": [
  "position is stored but not consulted: the parent blendType is Simple1D, and position applies to the 2D types.",
  "threshold is not kept because the parent has useAutomaticThresholds true; Unity recomputes it. Set the parent's useAutomaticThresholds to false to keep a threshold."
] }
```

子の `position`、`directBlendParameter`、`threshold` は**親**を基準に判定します。それらが参照されるかどうかを決めるのは子ではなく、親が行うブレンドだからです。

---

## POST /api/assets/animator-controllers/{guid}/states

AnimatorController のレイヤーにステートを追加します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimatorController アセットの GUID |

### リクエストボディ(JSON)

```json
{
  "name": "Walk",
  "layerIndex": 0,
  "motion": { "guid": "d4e5f6..." },
  "speed": 1.0,
  "writeDefaultValues": false,
  "tag": "Locomotion",
  "position": { "x": 300, "y": 120 },
  "setAsDefault": false
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `name` | ✅ | ステート名 |
| `layerIndex` | ❌ | 対象レイヤーのインデックス(既定: 0) |
| `setAsDefault` | ❌ | `true` の場合、このステートをレイヤーの既定(エントリ)ステートに設定 |

[ステートのフィールド](#ステートのフィールド)の書き込み可能な設定はすべてここでも指定できます。作成してから PATCH で整える必要はありません: `motion`、`speed`、`tag`、`writeDefaultValues`、`iKOnFeet`、`mirror`、`cycleOffset`、4 つの `*Parameter` とその `*Active` フラグ、`position`。各項目の詳細は [PATCH](#patch-apiassetsanimator-controllersguidstates) を参照してください。

### レスポンス(HTTP 201)

```json
{ "added": "Walk", "layerIndex": 0, "isDefault": false, "unsupported": [] }
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `name` の欠落、`layerIndex` が範囲外、motion の GUID が見つからない、設定値が不正、`*Parameter` がコントローラーに存在しないパラメータを指している、またはボディに未知のフィールドがある |

すべての値はステートを作成する前に検証されます。したがって `400` で拒否されたリクエストは何も追加しません。

---

## PATCH /api/assets/animator-controllers/{guid}/states

AnimatorController の既存ステートを更新します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimatorController アセットの GUID |

### リクエストボディ(JSON)

```json
{
  "name": "Walk",
  "layerIndex": 0,
  "newName": "Run",
  "motion": { "guid": "e5f6a7..." },
  "speed": 1.5,
  "writeDefaultValues": false,
  "cycleOffset": 0.25,
  "speedParameter": "Speed",
  "speedParameterActive": true,
  "position": { "x": 300, "y": 120 },
  "setAsDefault": true
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `name` | ✅ | 現在のステート名(ステートの特定に使用) |
| `layerIndex` | ❌ | レイヤーインデックス(既定: 0) |
| `newName` | ❌ | ステートの新しい名前 |
| `setAsDefault` | ❌ | このステートをレイヤーの既定に設定 |
| `motion` | ❌ | Motion アセットを参照する `guid` を持つオブジェクト。割り当てを差し替えます |
| `speed` | ❌ | 再生速度 |
| `tag` | ❌ | ランタイムが `AnimatorStateInfo.IsTag` で照合する文字列。`""` でクリア |
| `writeDefaultValues` | ❌ | そのステートがアニメーションさせないプロパティを既定値に戻すかどうか |
| `iKOnFeet` | ❌ | Foot IK |
| `mirror` | ❌ | モーションを左右反転して再生するかどうか |
| `cycleOffset` | ❌ | モーションのサイクルに対する正規化オフセット |
| `speedParameter`, `cycleOffsetParameter`, `mirrorParameter`, `timeParameter` | ❌ | 各値を駆動するパラメータ。`""` でオーバーライドを解除 |
| `speedParameterActive`, `cycleOffsetParameterActive`, `mirrorParameterActive`, `timeParameterActive` | ❌ | そのオーバーライドを有効にするか |
| `position` | ❌ | `{x, y}` のグラフ位置。[`position` はグラフ上のレイアウト](#position-はグラフ上のレイアウト)を参照 |
| `behaviours` | ❌ | 受け付けますが**適用しません**。読み取り専用で、`unsupported` に報告されます |

省略したフィールドは変更されません。すべての値は最初の 1 つが書き込まれる前に検証されるため、`400` で拒否されたリクエストはステートを中途半端に更新した状態にはせず、元のまま残します。

`*Parameter` とその `*Active` フラグは 1 つの決定として扱い、判定はリクエストが持つ半分ずつではなく**リクエスト適用後に残る組**に対して行います(送られたフィールドはその値、送られなかったフィールドはステートの現在値)。有効なのに名前が空のオーバーライドは再生できないステートであり、どちらの半分も単独では不正に見えないまま作れてしまうためです。

| リクエスト | 結果 |
|---|---|
| コントローラーに無い名前 | `400`。名前もフラグも書き込みません |
| リクエストにもステートにも名前が無い状態で `*Active: true` | `400`。何も駆動しないオーバーライドになります |
| フラグが `true` のまま `*Parameter: ""` | 同じ理由で `400`。まとめて解除するには同じリクエストで `*Active: false` も送ってください |
| ステートが既に実在するパラメータ名を持つ状態で `*Active: true` だけ | 受理。名前を送り直す必要はありません |
| `*Parameter: ""` と `*Active: false` | 受理。オーバーライドを解除します |

オーバーライドが無効のままなら、ステートが既に持っている名前は再検証しません。休眠中のオーバーライドが指すパラメータを誰かが削除していても、無関係なフィールドの更新が拒否されないようにするためです。

**未知のフィールドは拒否します。** `400` で受け付けるフィールド一覧を返すため、`writeDefaults` のような綴り違いが「効かない設定」として通ってしまうことはありません。

### レスポンス

```json
{ "updated": "Run", "layerIndex": 0, "unsupported": [] }
```

`unsupported` は、受け付けたが適用されなかったフィールドを列挙します。現在該当するのは `behaviours` だけです。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | 設定値が不正、`*Parameter` がコントローラーに存在しないパラメータを指している、またはボディに未知のフィールドがある |
| 404 | ステートが見つからない |

---

## DELETE /api/assets/animator-controllers/{guid}/states

AnimatorController のレイヤーからステートを削除します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimatorController アセットの GUID |

### リクエストボディ(JSON)

```json
{ "name": "Walk", "layerIndex": 0 }
```

### レスポンス

```json
{ "removed": "Walk", "layerIndex": 0 }
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 404 | ステートが見つからない |

---

## POST /api/assets/animator-controllers/{guid}/transitions

AnimatorController のステート間にトランジションを追加します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

Any State トランジションには `from` に `"AnyState"` を使用します。Exit トランジションには `to` に `"Exit"` を使用します。`"AnyState"` → `"Exit"` は無効な組み合わせです。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimatorController アセットの GUID |

### リクエストボディ(JSON)

```json
{
  "from": "Idle",
  "to": "Walk",
  "layerIndex": 0,
  "hasExitTime": false,
  "duration": 0.25,
  "fixedDuration": true,
  "offset": 0.0,
  "conditions": [
    { "parameter": "Speed", "mode": "Greater", "threshold": 0.1 }
  ]
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `from` | ✅ | 遷移元ステート名、または `"AnyState"` |
| `to` | ❌ | 遷移先ステート名、または `"Exit"` |
| `toStateMachine` | ❌ | 遷移先をステートマシンにする。このトランジションが属するマシンからのパス。ステートがサブステートマシンに入る方法です |
| `layerIndex` | ❌ | レイヤーインデックス(既定: 0) |
| `stateMachinePath` | ❌ | どのステートマシンがこのトランジションを持つか。[`stateMachinePath`](#statemachinepath) を参照 |
| `hasExitTime` | ❌ | トランジションが exit time トリガーを持つかどうか |
| `exitTime` | ❌ | exit time が発火する正規化時間(`hasExitTime: true` の場合) |
| `duration` | ❌ | ブレンド時間。`fixedDuration` が `true` なら秒、`false` なら遷移元ステートに対する割合 |
| `fixedDuration` | ❌ | `duration` を秒として扱うかどうか。Unity が新規トランジションに与える既定値は `true` |
| `offset` | ❌ | 遷移先ステートでの正規化時間オフセット |
| `interruptionSource` | ❌ | `None`, `Source`, `Destination`, `SourceThenDestination`, `DestinationThenSource` |
| `orderedInterruption` | ❌ | 割り込みがトランジションの順序に従うかどうか |
| `canTransitionToSelf` | ❌ | AnyState トランジション専用。それ以外に送った場合は保存はされますが `unsupported` に列挙されます |
| `mute` | ❌ | トランジションをミュートする |
| `solo` | ❌ | トランジションをソロにする |
| `conditions` | ❌ | 条件オブジェクトの配列。配列全体を置き換えます |

**`to` と `toStateMachine` はそれぞれ任意で、どちらか一方が必須です。** 単体で必須のものはありません(ステートへの遷移は `to`、サブステートマシンへの遷移は `toStateMachine`)。両方送った場合も、どちらも送らなかった場合も `400` です。

**条件の mode:** `If`、`IfNot`(Bool/Trigger)、`Greater`、`Less`、`Equals`、`NotEqual`(Float/Int)

すべてのフィールドはトランジションを作成する前に解析・検証されます。したがって `400` で拒否されたリクエストはコントローラーに何も追加しません。`mode` が上記 6 種のいずれでもない条件は、読み飛ばさずに拒否します。`threshold` が存在するのに数値でない場合(クォート付きの `"0.5"`、`null`、`NaN`)も同様に拒否します。`threshold` を**省略**した場合は `0` で、これは `If` と `IfNot` が使う値です。

すでにトランジションがあるステートの組に 2 本目を追加することは正当な操作であり、これまでどおり可能です。レスポンスは新しいトランジションの `transitionId` を返します。以後はこれでアドレス指定します。

### レスポンス(HTTP 201)

```json
{
  "added": true,
  "transitionId": "GlobalObjectId_V1-3-a1b2c3...-14749150960317597279-0",
  "from": "Idle",
  "to": "Walk",
  "layerIndex": 0,
  "unsupported": []
}
```

`unsupported` は、保存はされたが参照されないフィールドを列挙します。現在該当するのは、AnyState 以外のトランジションに送られた `canTransitionToSelf` だけです。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `from` または `to` の欠落、設定値が不正、`interruptionSource` または条件の `mode` が未知、あるいは `"AnyState"` → `"Exit"` が要求された |
| 404 | 遷移元または遷移先のステートが見つからない |

---

## PATCH /api/assets/animator-controllers/{guid}/transitions

トランジションを 1 本更新します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimatorController アセットの GUID |

### リクエストボディ(JSON)

```json
{
  "transitionId": "GlobalObjectId_V1-3-a1b2c3...-14749150960317597279-0",
  "duration": 0.1,
  "fixedDuration": false,
  "interruptionSource": "Destination",
  "conditions": [
    { "parameter": "Speed", "mode": "Greater", "threshold": 0.5 }
  ]
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `transitionId` | ❌ | 読み取りレスポンスが返すアドレス。ちょうど 1 本を指します |
| `from`, `to` | ❌ | ステート名によるアドレス指定。その組がちょうど 1 本のときだけ受け付けます |
| `layerIndex` | ❌ | 探索するレイヤー(既定: 0) |

`transitionId` か、`from` と `to` の両方か、いずれかが必要です。両方送られた場合は `transitionId` が優先されます。`POST` に挙げた設定はすべて受け付け、省略したフィールドは変更されません。

`conditions` は配列全体を置き換えます。**空配列は条件をクリアします** — 「変更しない」を意味するのはフィールドの省略のほうです。

すべての値は最初の 1 つが書き込まれる前に解析・検証されます。したがって `400` で拒否されたリクエストは、トランジションを中途半端に更新した状態にはせず、元のまま残します。

### レスポンス

```json
{
  "updated": true,
  "transitionId": "GlobalObjectId_V1-3-a1b2c3...-14749150960317597279-0",
  "from": "Idle",
  "to": "Walk",
  "layerIndex": 0,
  "unsupported": []
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | アドレスが送られていない、`transitionId` の形式が不正、または設定値が不正か未知 |
| 404 | 一致するトランジションが無い、または `transitionId` がもう解決できない。別レイヤーの ID の場合はどのレイヤーにあるかを返します |
| 409 | `from` と `to` が 2 本以上に一致した — 下記参照 |
| 422 | `transitionId` が `AnimatorStateTransition` 以外に解決された |

### ステート名の組が曖昧な場合の 409

```json
{
  "error": "2 transitions match Idle -> Walk. Address one by transitionId; 'matches' lists every candidate with its conditions.",
  "from": "Idle",
  "to": "Walk",
  "layerIndex": 0,
  "matches": [
    {
      "transitionId": "GlobalObjectId_V1-3-a1b2c3...-14749150960317597279-0",
      "conditions": [ { "parameter": "Speed", "mode": "Greater", "threshold": 0.1 } ]
    },
    {
      "transitionId": "GlobalObjectId_V1-3-a1b2c3...-10875748444440948623-0",
      "conditions": [ { "parameter": "Jump", "mode": "If", "threshold": 0.0 } ]
    }
  ]
}
```

`400` ではなく `409` である理由は、リクエスト自体は正しく、アドレスとして使えなくしているのがコントローラー側の形だからです。候補ごとに条件が付くのは、それが経路を見分ける手がかりであり、追加のリクエストなしに選べるようにするためです。何も書き込まれません。

---

## DELETE /api/assets/animator-controllers/{guid}/transitions

トランジションを 1 本削除します。トランジションはコントローラーのサブアセットであり、削除と同時に破棄されます。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimatorController アセットの GUID |

### リクエストボディ(JSON)

```json
{ "transitionId": "GlobalObjectId_V1-3-a1b2c3...-14749150960317597279-0" }
```

アドレス指定は `PATCH` とまったく同じです。`transitionId`、またはその組がちょうど 1 本のときの `from` と `to` を使います。`transitionId`、`from`、`to` はクエリパラメータでも送れます。

### レスポンス

```json
{
  "removed": true,
  "transitionId": "GlobalObjectId_V1-3-a1b2c3...-14749150960317597279-0",
  "from": "Idle",
  "to": "Walk",
  "layerIndex": 0
}
```

`transitionId` は削除されたトランジションのものです。この ID はもう解決されません。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | アドレスが送られていない、または `transitionId` の形式が不正 |
| 404 | 一致するトランジションが無い、または `transitionId` がもう解決できない |
| 409 | `from` と `to` が 2 本以上に一致した。ボディは `PATCH` と同じ形で、何も削除されません |
| 422 | `transitionId` が `AnimatorStateTransition` 以外に解決された |

## POST /api/assets/animator-controllers/{guid}/state-machines

サブステートマシンを作成します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimatorController アセットの GUID |

### リクエストボディ(JSON)

```json
{
  "layerIndex": 0,
  "stateMachinePath": ["Combat"],
  "name": "Melee",
  "position": { "x": 300, "y": 120 }
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `name` | ✅ | 新しいマシンの名前 |
| `layerIndex` | ❌ | レイヤーインデックス(既定: 0) |
| `stateMachinePath` | ❌ | どのマシンの中に作るか。省略または `[]` はレイヤーのルート |
| `position` | ❌ | 親マシン内のグラフ位置 `{x, y}` |

兄弟が既に持っている `name` は `409` を返します。Unity の `AddStateMachine` は名前を重複させず、6000.0.80f1 で計測したところ黙って別の名前を返します。したがって選択肢は「呼び出し側が要求していない名前を報告する」か「拒否する」かであり、パスは名前でアドレス指定するため、要求した名前で組み立てたアドレスは機能しません。

### レスポンス(HTTP 201)

```json
{ "added": "Melee", "layerIndex": 0, "stateMachinePath": ["Combat", "Melee"] }
```

返された `stateMachinePath` が新しいマシンのアドレスです。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `name` の欠落、`position` が不正、またはボディに未知のフィールドがある |
| 404 | `stateMachinePath` が解決しない |
| 409 | 兄弟が既に `name` を持っている、またはパスが曖昧 |

---

## DELETE /api/assets/animator-controllers/{guid}/state-machines

サブステートマシンと、それが保持するすべてを削除します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{ "layerIndex": 0, "stateMachinePath": ["Combat", "Melee"], "recursive": true }
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `stateMachinePath` | ✅ | 削除するマシン。`[]` はレイヤーのルートを指し、拒否されます |
| `layerIndex` | ❌ | レイヤーインデックス(既定: 0) |
| `recursive` | ❌ | 中身のあるマシンの削除を承認する |

ステートマシンは自身のステート、トランジション、入れ子のマシン、そしてそれらのステートが持つブレンドツリーを所有しており、いずれもコントローラーのサブアセットです。`DELETE .../states` より大きな操作なので、中身のあるマシンは `recursive` が `true` でない限り `409` を返します:

```json
{
  "error": "State machine 'Combat' holds 2 state(s) and 1 nested state machine(s) in total, which removing it would take with it. Send recursive true to confirm.",
  "layerIndex": 0,
  "stateMachinePath": ["Combat"],
  "totalStates": 2,
  "totalStateMachines": 1,
  "states": [],
  "stateMachines": ["Melee"]
}
```

`totalStates` と `totalStateMachines` はサブツリー全体を数えます。それが削除の代償だからです。`states` と `stateMachines` は直下の子を名前で並べます。こちらは呼び出し側が見分けられるものです。直下にステートを持たないが 5 つ持つマシンを 1 つ抱えているマシンは、5 と報告されます。

### レスポンス

```json
{
  "removed": "Melee",
  "layerIndex": 0,
  "stateMachinePath": ["Combat", "Melee"],
  "removedStates": 3,
  "removedStateMachines": 0,
  "destroyedBlendTrees": 1
}
```

`destroyedBlendTrees` は、Unity の削除がアセットに残したブレンドツリーをこのエンドポイントが手で破棄した数です。`DELETE .../states` と同じ意味です。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `stateMachinePath` が空または不正、またはボディに未知のフィールドがある |
| 404 | `stateMachinePath` が解決しない |
| 409 | マシンが中身を持っていて `recursive` が `true` でない、またはパスが曖昧 |

---

## POST /api/assets/animator-controllers/{guid}/state-machine-transitions

ステートマシン同士をつなぐ型である `AnimatorTransition` を追加します。これが無いと、作成したサブステートマシンには決して入れません。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{
  "layerIndex": 0,
  "stateMachinePath": ["Combat"],
  "from": "Entry",
  "toStateMachine": ["Melee"],
  "conditions": []
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `from` | ✅ | エントリトランジションは `"Entry"`、または対象マシンに入れ子になったステートマシンの名前 |
| `layerIndex` | ❌ | レイヤーインデックス(既定: 0) |
| `stateMachinePath` | ❌ | このトランジションを持つマシン |
| `to` | ❌ | 対象マシン内の遷移先ステート名 |
| `toStateMachine` | ❌ | 遷移先ステートマシン。対象マシンからのパス |
| `toExit` | ❌ | 遷移先をそのマシンの Exit ノードにする |
| `solo`, `mute` | ❌ | |
| `conditions` | ❌ | 条件オブジェクトの配列。配列全体を置き換えます |

`to`、`toStateMachine`、`toExit` のうち**ちょうど 1 つ**を指定します。読み取りが遷移先に判別子を付けるのと同じ理由です。名前だけではステートかステートマシンかを言えず、コントローラーは両方に同じ名前を使えます。

エントリトランジションは Exit を指せません。Entry はマシンがどこから始まるかを決めるものだからです。

### レスポンス(HTTP 201)

```json
{ "added": true, "transitionId": "GlobalObjectId_V1-3-...", "layerIndex": 0, "from": "Entry" }
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `from` の欠落、遷移先が無いか複数、`"Entry"` が Exit を指した、またはボディに未知のフィールドがある |
| 404 | 遷移元マシン、遷移先ステート、またはパスが解決しない |
| 409 | パスまたは遷移元名が曖昧 |

---

## DELETE /api/assets/animator-controllers/{guid}/state-machine-transitions

`AnimatorTransition` を削除します。トランジションはコントローラーのサブアセットであり、削除と同時に破棄されます。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### リクエストボディ(JSON)

```json
{ "transitionId": "GlobalObjectId_V1-3-..." }
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `transitionId` | ✅ | 読み取りの `entryTransitions` または `stateMachineTransitions` から |
| `layerIndex` | ❌ | 探索するレイヤー(既定: 0) |

名前の組による指定はありません。これらのトランジションには名前を挙げられる遷移元ステートが無く、エントリトランジションに至っては Entry ノード以外に遷移元がありません。

### レスポンス

```json
{ "removed": true, "transitionId": "GlobalObjectId_V1-3-...", "kind": "entry", "layerIndex": 0 }
```

`kind` は `entry` または `stateMachine` です。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `transitionId` の欠落または形式不正、またはボディに未知のフィールドがある |
| 404 | そのレイヤーにトランジションが無い、または ID がもう解決できない |
| 422 | ID が `AnimatorStateTransition` に解決された。それらは `DELETE .../transitions` を使ってください |

---
