# API リファレンス — General

[English](general.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](general.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`。実際の URL は接続時に `<project>/.unionair/endpoint.txt` から読み取ってください。エンドポイントの発見手順、レスポンスの規約、カテゴリ/セキュリティの注意事項は [API リファレンス索引](../api-reference.ja.md) を参照してください。

---

## GET /api/help

このドキュメントに直接アクセスできない LLM・MCP ブリッジ・各種ツール向けに、コンパクトな API マニフェストを返します。エンドポイント一覧は `[UnionAirController]` と `[UnionAirEndpoint]` のルートメタデータから生成されます。

### レスポンス

```json
{
  "name": "com.leonakasaka.unionair",
  "displayName": "UnionAir - Unity REST Bridge",
  "version": "0.5.1",
  "baseUrl": "http://localhost:51234/api",
  "description": "UnionAir exposes Unity Editor state and selected Editor operations as a local REST API.",
  "categories": [
    {
      "id": "read",
      "displayName": "Read",
      "source": "builtin",
      "enabled": true,
      "canDisable": false,
      "enabledByDefault": true,
      "risk": ["readOnly"],
      "blockedDuring": []
    }
  ],
  "endpoints": [
    {
      "method": "GET",
      "path": "/api/health",
      "routeTemplate": "/api/health",
      "source": "builtin",
      "enabled": true,
      "category": "read",
      "summary": "Checks whether the server is running and identifies its Unity project.",
      "risk": ["readOnly"],
      "playModePolicy": "allowed",
      "testRunPolicy": "allowed",
      "blockedDuring": [],
      "pathParams": [],
      "requiredQuery": [],
      "optionalQuery": [],
      "requiredBody": [],
      "optionalBody": []
    }
  ]
}
```

各エンドポイント項目には HTTP メソッド、パス、カテゴリ、短い概要、リスクメタデータ、およびコンパクトなパラメータ/ボディフィールド一覧が含まれます。カテゴリ項目は API のグループ分け、現在の有効化状態、そのカテゴリ内エンドポイントのリスクプロファイルを表します。

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `categories[].id` | string | エンドポイントから参照される安定的なカテゴリ ID |
| `categories[].displayName` | string | 人間可読なカテゴリ名 |
| `categories[].source` | string | `builtin` または `custom` |
| `categories[].enabled` | bool | カテゴリ内のエンドポイントが現在有効かどうか |
| `categories[].canDisable` | bool | EditorWindow でカテゴリを無効化できるかどうか |
| `categories[].enabledByDefault` | bool | ユーザーによる変更前の初期状態が有効かどうか |
| `categories[].risk` | string[] | `readOnly`、`sceneUpdate`、`assetUpdate`、`playMode`、`custom`、`requestDependent`、`editorState`、`profiling`、または `executableOutput` |
| `categories[].blockedDuring` | string[] | そのカテゴリのエンドポイントが拒否される Editor アクティビティ。[Editor アクティビティ](activities.ja.md) を参照 |
| `endpoints[].source` | string | `builtin` または `custom` |
| `endpoints[].enabled` | bool | エンドポイントが現在有効かどうか |
| `endpoints[].routeTemplate` | string | 属性ルーターが使用するルートテンプレート |
| `endpoints[].category` | string | ディスカバリ/UI グループ分けに使うカテゴリ。built-in 定数は `read`、`sceneWrite`、`assetWrite`、`playMode`、`editorActions`、`testRunner`、`profiling`、`build`、`custom`。カスタムエンドポイントは任意の安定的なカテゴリ文字列を使用可能 |
| `endpoints[].risk` | string[] | カテゴリから継承したリスク。エンドポイントがより具体的なリスクオーバーライドを宣言している場合はそちら |
| `endpoints[].playModePolicy` | string | `allowed`、`blocked`、または `explicitOptIn`。`blocked` のエンドポイントは Play モード中 `409` を返します。`explicitOptIn` のエンドポイントは Play モード中、Editor 設定と `allowWhilePlaying=true` の両方が必要です |
| `endpoints[].testRunPolicy` | string | `allowed` または `blocked`。明示的に許可されない endpoint はテスト実行中 blocked |
| `endpoints[].blockedDuring` | string[] | そのエンドポイントが拒否される Editor アクティビティの完全な一覧(優先順位順)。`playModePolicy` と `testRunPolicy` が強制するものも含みます。3 つのフィールドから再構成する代わりにこの配列を読んでください。[Editor アクティビティ](activities.ja.md) を参照 |
| `endpoints[].requiredQuery` | string[] | 必須のクエリ文字列パラメータ |
| `endpoints[].optionalQuery` | string[] | 任意のクエリ文字列パラメータ |
| `endpoints[].requiredBody` | string[] | 必須の JSON ボディフィールド |
| `endpoints[].optionalBody` | string[] | 任意の JSON ボディフィールド |

### クエリパラメータ

| パラメータ | 既定値 | 説明 |
|-------------|-----------|------|
| `detail` | (compact) | `full` を指定すると、各エンドポイント項目にリクエスト/レスポンス例などの詳細フィールドが追加されます。 |
| `category` | (all) | カテゴリとエンドポイントを単一のカテゴリ ID で絞り込みます(大文字小文字は区別しない)。例: `read`、`sceneWrite`、`assetWrite`、`playMode`、`editorActions`、`testRunner`、`profiling`、`build`。 |
| `includeDisabled` | `false` | `true` を指定すると、無効化されたカスタムカテゴリ/エンドポイントおよびルート競合のあるエンドポイントも含めます。built-in のカテゴリ/エンドポイントは常に現在の `enabled` 状態付きで一覧されます。 |
| `source` | `all` | `builtin`、`custom`、または `all` |

> このエンドポイントは意図的に軽量なディスカバリマニフェストであり、完全な OpenAPI スキーマではありません。詳細なリクエスト/レスポンス例は本ドキュメントを参照してください。エンドポイントを追加・変更する際は、`/api/help`・ルーティング・EditorWindow のエンドポイント一覧が同期するように `[UnionAirEndpoint]` メタデータを更新してください。

---

## カスタムコントローラ

アプリケーション側の Editor アセンブリは `/api/custom/...` 配下にカスタムコントローラを追加できます。コントローラのセットアップ、カテゴリメタデータ、リクエストのパース、参照解決ヘルパー、Play モードポリシー、セキュリティ指針は [カスタムコントローラ](../custom-controllers.ja.md) を参照してください。

---

## GET /api/health

サーバが稼働しているかを確認し、そのEditor processが公開するUnity projectを識別します。
`.unionair/endpoint.txt`からURLを発見したclientは、`projectPath`をそのファイルがあるproject
directoryと比較する必要があります。一致しない場合、発見ファイルはstaleであり、別projectの
Editorを指しています。

### レスポンス

```json
{
  "status": "ok",
  "unityVersion": "6000.3.5f2",
  "projectPath": "C:\\Work\\MyProject"
}
```

`projectPath`はprojectの`Assets` directoryの絶対parent pathです。host platformのpath semantics
(Windowsでは大文字小文字を区別しない)で比較してください。

---

## オブジェクト参照

シーン上の GameObject と Component は、読み取りレスポンスで Unity の `GlobalObjectId` 文字列を公開します。書き込み系・詳細系の API は、target・source・parent の指定に型付きオブジェクト参照を使用します。

参照の形式:

```json
{ "type": "hierarchyPath", "value": "Canvas/Button" }
```

オブジェクト参照は JSON オブジェクトで指定する必要があります。`"Canvas/Button"` のような裸の文字列は受け付けられず、`400 Bad Request` を返します。

| Type | 値 |
|------|-------|
| `hierarchyPath` | `Canvas/Button` のような GameObject 階層パス。`type` 省略時の既定値 |
| `componentPath` | `GameObjectPath:ComponentType` 形式のコンポーネントパス。例: `Canvas/Button:UnityEngine.UI.Text` |
| `globalObjectId` | シーンの GameObject または Component を指す Unity GlobalObjectId 文字列 |

`scenePath` はロード済みシーンを選択する独立したセレクタであり、`hierarchyPath` と `componentPath` の解決にのみ使用されます。シーンアセットのレスポンスはアセットの `guid` 値を使用し、`globalObjectId` は使用しません。

カスタムコントローラは `UnionAirReferenceResolver` で同じ参照形式をパース・解決できます。[カスタムコントローラ](../custom-controllers.ja.md) を参照してください。
