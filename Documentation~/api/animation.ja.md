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
      "defaultWeight": 0.0,
      "isBaseLayer": true,
      "blendingMode": "Override",
      "avatarMask": null,
      "iKPass": false,
      "syncedLayerIndex": -1,
      "syncedLayerAffectsTiming": false,
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
