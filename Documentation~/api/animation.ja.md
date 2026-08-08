# API リファレンス — Animation

[English](animation.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](animation.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`。実際の URL は接続時に `<project>/.unionair/endpoint.txt` から読み取ってください。エンドポイントの発見手順、レスポンスの規約、カテゴリ/セキュリティの注意事項は [API リファレンス索引](../api-reference.ja.md) を参照してください。

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

### リクエストボディ(JSON)

```json
{
  "bindings": [
    { "relativePath": "Hips", "type": "Transform", "property": "localPosition.y" },
    { "relativePath": "", "type": "UnityEngine.UI.Image", "property": "m_Sprite" }
  ]
}
```

### レスポンス

```json
{
  "removed": ["localPosition.y", "m_Sprite"],
  "errors": []
}
```

### エラー

| ステータス | 原因 |
|--------|-------|
| 400 | `bindings` の欠落・空、またはバインディングエントリが不正 |
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
          "motion": { "guid": "d4e5f6...", "name": "IdleClip" },
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
