# カスタムコントローラ

[English](custom-controllers.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](custom-controllers.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

カスタムコントローラを使うと、UnionAir 本体を変更せずに、アプリケーション側の Editor アセンブリからプロジェクト固有の REST エンドポイントを追加できます。UnionAir 自身のアセンブリ内のコントローラは built-in として扱われ、それ以外の Editor アセンブリのコントローラは custom として `/api/custom/...` 配下に公開されます。

## コントローラのセットアップ

`[UnionAirController]` を付けたコントローラクラスと、`[UnionAirEndpoint]` を付けた1つ以上のメソッドを宣言します。エンドポイントメソッドは `UnionAirRequestContext` を1つだけ受け取り、`void` を返す必要があります。

```csharp
using LeonAkasaka.UnionAir.Editor;

[UnionAirController("my-tool")]
[UnionAirCategory(
    "debug",
    DisplayName = "Debug Tools",
    Risk = UnionAirEndpointRisk.Custom,
    CanDisable = true,
    EnabledByDefault = false)]
public class MyToolController
{
    [UnionAirEndpoint(
        "GET",
        "status",
        Category = "debug",
        Summary = "Returns custom tool status")]
    public void Status(UnionAirRequestContext ctx)
    {
        RestResponse.Send(ctx.Response, "{\"status\":\"ok\"}");
    }
}
```

この例は `GET /api/custom/my-tool/status` を登録します。

カスタムハンドラーは既定で無効です。**Window > UnionAir > REST Bridge > Custom Handlers** で有効化してください。その後、カスタムカテゴリを個別に有効/無効へ切り替えられます。Custom Handlersのmaster switchがオフの間、カテゴリのcheckboxは操作できません。

`Category` は文字列であり、カスタム拡張は `/api/help` と EditorWindow に表示される独自のグループ名を定義できます。built-in エンドポイントは `UnionAirEndpointCategories.Read`、`SceneWrite`、`AssetWrite`、`PlayMode`、`EditorActions`、`Profiling`、任意機能の `TestRunner` を使用します。カテゴリメタデータは有効化状態と既定のリスク報告を制御します。`Risk` はツールや LLM 向けの説明用メタデータであり、リクエストを受け付けるかどうかはカテゴリの有効化状態が決めます。ルートがカテゴリより狭いリスクプロファイルを持つ場合、エンドポイントは `UseRiskOverride = true` と `Risk = ...` を設定できます。

## リクエストとレスポンス

受信リクエストの確認には `UnionAirRequestContext.Request`、ルートテンプレートのパラメータには `UnionAirRequestContext.RouteValues`、レスポンスの書き込みには `UnionAirRequestContext.Response` を使用します。

これらの型は `System.Net.HttpListenerRequest` / `HttpListenerResponse` ではなく、UnionAir 自身の `UnionAirRequest` / `UnionAirResponse` です。transport は実装の詳細のままとなり、ハンドラーが書き込むバイトは必ず UnionAir が所有する型を通ります。リクエストは `HttpMethod`、`Url`、`QueryString`、`Headers`、`HasEntityBody`、`ContentType`、`ContentLength64` を、レスポンスは `StatusCode`、`ContentType`、`ContentLength64`、`OutputStream`、`AddHeader`、`Close` を公開します。リクエストボディはストリームからではなく `RequestBodyReader` 経由で読んでください。読み取り結果をキャッシュするため、同じリクエストを読むすべての箇所が同一のボディを見ます。

カスタムエンドポイントには UnionAir の transport policy が適用されます。`Origin` ヘッダーを持つリクエストは controller 実行前に拒否されるため、ブラウザの `fetch` と XMLHttpRequest は既定で非対応です。空でないボディを持つリクエストには `Content-Type: application/json` が必要です。空のリクエストに Content-Type は不要です。

`RequestBodyReader` は、UnionAir の built-in ハンドラーと同じ軽量 JSON ヘルパーを提供します:

```csharp
var body = RequestBodyReader.ReadString(ctx.Request);
var name = RequestBodyReader.GetString(body, "name");
var targetJson = RequestBodyReader.GetObject(body, "target");
if (!RequestBodyReader.TryGetStringArray(body, "tags", out var tags))
    RestResponse.SendError(ctx.Response, "tags must be a string array", 400);
```

`TryGetStringArray` は top-level key のみを読み取り、key がない場合は空配列を返し、存在する値が有効な文字列配列でない場合は `false` を返します。

`RestResponse` は built-in エンドポイントと同じ Content-Type、UTF-8 エンコーディング、エラー形式で JSON レスポンスを書き込みます。CORS ヘッダーは追加しません:

```csharp
RestResponse.Send(ctx.Response, "{\"status\":\"ok\"}");
RestResponse.SendError(ctx.Response, "Missing required field: target", 400);
```

`RestResponse.AddCorsHeaders` はソース互換性のため obsolete な no-op として残されています。ブラウザアクセスを必要とするカスタムコントローラは UnionAir のリクエストポリシーを迂回せず、独自の transport を実装して保護する必要があります。

後で完了するhandlerはreturn前に`ctx.Defer()`を呼び、最終的に自分でresponseをcloseする必要があります。deferしたbackground I/Oでは.NET response streamを使用できますが、worker threadからUnity APIを呼び出してはいけません。

`RestResponse.FormatNullableString(value)` はescape済みJSON文字列リテラルを返し、`value`がnullの場合だけJSONリテラル`null`を返します。空文字列は`""`のままです。

## 参照解決

カスタムコントローラは `UnionAirReferenceResolver` を使って、built-in エンドポイントが受け付けるものと同じ型付きオブジェクト参照をパース・解決できます。

```csharp
using LeonAkasaka.UnionAir.Editor;
using UnityEngine;

[UnionAirController("selection")]
[UnionAirCategory(
    "selectionTools",
    DisplayName = "Selection Tools",
    Risk = UnionAirEndpointRisk.SceneUpdate,
    EnabledByDefault = false)]
public class SelectionController
{
    [UnionAirEndpoint(
        "POST",
        "ping",
        Category = "selectionTools",
        PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
        Summary = "Resolves a GameObject target and returns its path",
        RequiredBody = new string[] { "target" },
        OptionalBody = new string[] { "scenePath", "allowWhilePlaying" },
        OptionalQuery = new string[] { "allowWhilePlaying" })]
    public void Ping(UnionAirRequestContext ctx)
    {
        var body = RequestBodyReader.ReadString(ctx.Request);

        if (!UnionAirReferenceResolver.TryResolveSceneFromRequest(
                ctx.Request, body, out var scene, out var error, out var statusCode) ||
            !UnionAirReferenceResolver.TryReadBody(
                body, "target", out var target, out error, out statusCode) ||
            !UnionAirReferenceResolver.TryResolveGameObject(
                scene, target, "target", out var go, out error, out statusCode))
        {
            RestResponse.SendError(ctx.Response, error, statusCode);
            return;
        }

        var id = UnionAirReferenceResolver.GetGlobalObjectId(go);
        RestResponse.Send(ctx.Response,
            "{\"name\":\"" + RestResponse.EscapeJson(go.name) + "\"," +
            "\"globalObjectId\":\"" + RestResponse.EscapeJson(id) + "\"}");
    }
}
```

サポートされる型付きオブジェクト参照ペイロード:

```json
{ "type": "hierarchyPath", "value": "Canvas/Button" }
{ "type": "componentPath", "value": "Canvas/Button:UnityEngine.UI.Text" }
{ "type": "globalObjectId", "value": "GlobalObjectId_V1-..." }
```

`type` を省略した場合は `hierarchyPath` になります。`scenePath` はロード済みシーンを選択する独立したセレクタであり、`hierarchyPath` と `componentPath` の解決にのみ使用されます。

主なリゾルバメソッド:

| メソッド | 用途 |
|--------|---------|
| `TryReadQuery` | クエリ文字列フィールドから型付きオブジェクト参照をパース |
| `TryReadBody` | JSON ボディフィールドから型付きオブジェクト参照をパース |
| `TryParse` | 生のオブジェクト参照 JSON 値をパース |
| `TryResolveSceneFromRequest` | クエリまたはボディから `scenePath` を解決(既定はアクティブシーン) |
| `TryResolveOptionalScene` | シーンパス/名前を解決、なければアクティブシーン |
| `TryResolveRequiredScene` | 必須のロード済みシーンパス/名前を解決 |
| `TryResolveGameObject` | `GameObject` への参照を解決 |
| `TryResolveComponent` | `Component` への参照を解決 |
| `TryResolveGameObjectOrComponent` | どちらの種類のオブジェクトへの参照も解決 |
| `TryResolveObject` | シーンの `GameObject` または `Component` を `UnityEngine.Object` として解決 |
| `TryResolveCamera` | GameObject または Camera コンポーネント参照からカメラを解決 |
| `GetGlobalObjectId` | Unity オブジェクトを `GlobalObjectId` 文字列にシリアライズ |
| `TryResolveGlobalObjectId` | `GlobalObjectId` 文字列を `UnityEngine.Object` に解決 |
| `TryResolveGlobalObjectIdAsGameObject` | `GlobalObjectId` 文字列を `GameObject` に解決 |
| `TryResolveGlobalObjectIdAsComponent` | `GlobalObjectId` 文字列を `Component` に解決 |
| `TryResolveGlobalObjectIdAsGameObjectOrComponent` | `GlobalObjectId` 文字列を `GameObject` または `Component` に解決 |
| `TryResolveAssetReference` | `assetGuid` または `assetPath` でアセットを解決 |

リゾルバメソッドは失敗時に `false` とエラーメッセージ・ステータスコードを返します。HTTP レスポンスは書き込まないため、コントローラ側でエラーをそのまま返すか、独自のバリデーションと組み合わせるかを選べます。

代表的なステータスコード:

| ステータス | 原因 |
|--------|-------|
| `400` | 入力の欠落または不正な形式 |
| `404` | シーン、オブジェクト、コンポーネント、またはアセットが見つからない |
| `409` | シーン名が曖昧 |
| `422` | 入力が誤った種類・型のオブジェクトに解決される |

## アセット参照

カスタムエンドポイントがシーンオブジェクトではなくプロジェクトアセットを解決する必要がある場合は、アセット参照を使用します。

```csharp
var body = RequestBodyReader.ReadString(ctx.Request);
var assetJson = RequestBodyReader.GetObject(body, "asset");

if (!UnionAirReferenceResolver.TryResolveAssetReference(
        assetJson,
        typeof(Texture2D),
        "asset",
        out var asset,
        out var error,
        out var statusCode))
{
    RestResponse.SendError(ctx.Response, error, statusCode);
    return;
}
```

アセット参照ペイロードには `assetGuid` または `assetPath` を使用できます。`assetType` は省略可能で、指定する場合は `UnityEngine.Object` 型に解決される必要があります。

```json
{ "assetGuid": "a1b2c3...", "assetType": "UnityEngine.Texture2D" }
{ "assetPath": "Assets/Textures/Icon.png" }
```

## Play モードとセキュリティ

カスタムコントローラは、それを定義する Editor アセンブリの権限で Unity Editor プロセス内で実行されます。UnionAir はカスタムコントローラのコードをサンドボックス化しません。Custom Handlersを無効化してもHTTP routeが削除されるだけであり、assemblyのloadや別のEditor entry pointからのcode実行は防ぎません。categoryとhandlerのtoggleはAPI露出範囲と誤操作を制御するもので、悪意あるproject codeへの防御ではありません。

カテゴリメタデータと `PlayModePolicy` は意図を持って設定してください:

| 設定 | 指針 |
|---------|----------|
| `UnionAirEndpointRisk.None` | 読み取り専用エンドポイント |
| `UnionAirEndpointRisk.SceneUpdate` | シーンオブジェクトやシーン状態を変更するエンドポイント |
| `UnionAirEndpointRisk.AssetUpdate` | アセット、プロジェクトファイル、プレハブ、保存済みシーンを変更するエンドポイント |
| `UnionAirEndpointRisk.PlayMode` | Play モードの開始・終了・一時停止・ステップを行うエンドポイント |
| `UnionAirEndpointRisk.Custom` | ツール固有または複合的な動作 |
| `UnionAirEndpointRisk.RequestDependent` | 副作用がリクエストパラメータやペイロードに依存するエンドポイント |
| `UnionAirEndpointRisk.EditorState` | シーンやアセットのデータを直接変更せず、Editor の UI や選択状態を変更するエンドポイント |
| `UnionAirEndpointRisk.Profiling` | profilingを有効化、またはプロジェクトデータを含む診断成果物を取得するエンドポイント |
| `UnionAirPlayModePolicy.Blocked` | Play モード中に決して実行すべきでない、永続的なシーン/アセット書き込み |
| `UnionAirPlayModePolicy.ExplicitOptIn` | Editor 側の許可と `allowWhilePlaying=true` の両方を要する一時的なシーンオブジェクト変更 |
| `UnionAirTestRunPolicy.Blocked` | 既定。Unity Test Framework の run 中は endpoint を拒否 |
| `UnionAirTestRunPolicy.Allowed` | テスト中も安全な運用監視・制御に限定して使用 |

UnionAir は built-in endpoint と同様に custom endpoint にも test-run lock を適用します。active run の監視・制御専用に設計した route でない限り、既定の `Blocked` を維持してください。

カスタムハンドラーは既定で無効であり、カスタムカテゴリは EditorWindow から個別に無効化できます。カスタムエンドポイントのスコープは狭く保ち、すべてのリクエストフィールドを検証し、拒否した操作には明示的なエラーを返してください。
