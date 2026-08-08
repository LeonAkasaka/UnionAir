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
  "frameRate": 60.0,
  "length": 1.0,
  "wrapMode": "Loop",
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

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | アセットが AnimationClip でない |
| 404 | 指定 GUID のアセットが見つからない |

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
      "weight": 1.0,
      "blendingMode": "Override",
      "states": [
        {
          "name": "Idle",
          "speed": 1.0,
          "isDefault": true,
          "motion": {
            "type": "AnimationClip",
            "guid": "d4e5f6...",
            "name": "IdleClip",
            "assetPath": "Assets/Animations/Idle.anim",
            "clipsAtPath": 1
          },
          "transitions": [
            {
              "to": "Walk",
              "hasExitTime": false,
              "exitTime": 0.0,
              "duration": 0.25,
              "conditions": [
                { "parameter": "Speed", "mode": "Greater", "threshold": 0.1 }
              ]
            }
          ]
        }
      ],
      "anyStateTransitions": []
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
  "blendParameterY": "",
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
      "directBlendParameter": "",
      "motion": { "type": "AnimationClip", "guid": "...", "name": "Walk", "assetPath": "...", "clipsAtPath": 1 }
    }
  ]
}
```

入れ子は深さ 10 まで返します。その深さにあるブレンドツリーは `"truncated": true` を持ち、`children` を**持ちません**。境界と葉を区別できるようにするためで、空の `children` 配列は文字どおり「子が無い」という意味のまま残ります。

### このレスポンスが記述しないもの

サブステートマシンは列挙されません。各レイヤーのルートステートマシン直下のステートだけが現れるため、ステートがサブステートマシンの中にあるレイヤーは `states` が空配列になります。

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | アセットが AnimatorController でない |
| 404 | 指定 GUID のアセットが見つからない |

---

## POST /api/assets/animator-controllers/{guid}/parameters

AnimatorController にパラメータを追加します。同名のパラメータが既に存在する場合は置換されます。

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
| `defaultValue` | ❌ | 既定値(Float/Int/Bool のみ) |

### レスポンス(HTTP 201)

```json
{ "added": "Speed", "type": "Float" }
```

---

## DELETE /api/assets/animator-controllers/{guid}/parameters

AnimatorController から名前を指定してパラメータを削除します。

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
{ "removed": "Speed" }
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 404 | パラメータが見つからない |

---

## POST /api/assets/animator-controllers/{guid}/layers

AnimatorController にレイヤーを追加します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimatorController アセットの GUID |

### リクエストボディ(JSON)

```json
{ "name": "Arms", "weight": 1.0 }
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `name` | ✅ | レイヤー名 |
| `weight` | ❌ | 既定のレイヤーウェイト(0〜1)。追加レイヤーの既定は 0 |

### レスポンス(HTTP 201)

```json
{ "added": "Arms", "layerIndex": 1 }
```

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
  "setAsDefault": false
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `name` | ✅ | ステート名 |
| `layerIndex` | ❌ | 対象レイヤーのインデックス(既定: 0) |
| `motion` | ❌ | AnimationClip アセットを参照する `guid` を持つオブジェクト |
| `speed` | ❌ | 再生速度(既定: 1.0) |
| `setAsDefault` | ❌ | `true` の場合、このステートをレイヤーの既定(エントリ)ステートに設定 |

### レスポンス(HTTP 201)

```json
{ "added": "Walk", "layerIndex": 0, "isDefault": false }
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `name` の欠落、`layerIndex` が範囲外、または motion の GUID が見つからない |

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
  "setAsDefault": true
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `name` | ✅ | 現在のステート名(ステートの特定に使用) |
| `layerIndex` | ❌ | レイヤーインデックス(既定: 0) |
| `newName` | ❌ | ステートの新しい名前 |
| `motion` | ❌ | 割り当てられたモーションクリップを差し替え |
| `speed` | ❌ | 再生速度 |
| `setAsDefault` | ❌ | このステートをレイヤーの既定に設定 |

### レスポンス

```json
{ "updated": "Run", "layerIndex": 0 }
```

### エラー

| ステータス | 原因 |
|--------|-------|
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
  "offset": 0.0,
  "conditions": [
    { "parameter": "Speed", "mode": "Greater", "threshold": 0.1 }
  ]
}
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `from` | ✅ | 遷移元ステート名、または `"AnyState"` |
| `to` | ✅ | 遷移先ステート名、または `"Exit"` |
| `layerIndex` | ❌ | レイヤーインデックス(既定: 0) |
| `hasExitTime` | ❌ | トランジションが exit time トリガーを持つかどうか |
| `exitTime` | ❌ | exit time が発火する正規化時間(`hasExitTime: true` の場合) |
| `duration` | ❌ | トランジションのブレンド時間(秒) |
| `offset` | ❌ | 遷移先ステートでの正規化時間オフセット |
| `conditions` | ❌ | 条件オブジェクトの配列 |

**条件の mode:** `If`、`IfNot`(Bool/Trigger)、`Greater`、`Less`、`Equals`、`NotEqual`(Float/Int)

### レスポンス(HTTP 201)

```json
{ "added": true, "from": "Idle", "to": "Walk", "layerIndex": 0 }
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `from` または `to` の欠落、または `"AnyState"` → `"Exit"` が要求された |
| 404 | 遷移元または遷移先のステートが見つからない |

---

## PATCH /api/assets/animator-controllers/{guid}/transitions

既存のトランジションを更新します。トランジションは `from` と `to` のステート名で特定されます。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

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
  "duration": 0.1,
  "conditions": [
    { "parameter": "Speed", "mode": "Greater", "threshold": 0.5 }
  ]
}
```

`from` と `to` 以外のすべてのフィールドは任意で、指定したフィールドのみ更新されます。

### レスポンス

```json
{ "updated": true, "from": "Idle", "to": "Walk", "layerIndex": 0 }
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 404 | トランジションが見つからない |

---

## DELETE /api/assets/animator-controllers/{guid}/transitions

AnimatorController からトランジションを削除します。トランジションは `from` と `to` のステート名で特定されます。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> Play モード中は `409 Conflict` を返します。

### パスパラメータ

| パラメータ | 説明 |
|-----------|-------------|
| `guid` | AnimatorController アセットの GUID |

### リクエストボディ(JSON)

```json
{ "from": "Idle", "to": "Walk", "layerIndex": 0 }
```

### レスポンス

```json
{ "removed": true, "from": "Idle", "to": "Walk", "layerIndex": 0 }
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 404 | トランジションが見つからない |
