# API リファレンス — Compile

[English](compile.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](compile.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`(デフォルトポート: **8765**)。レスポンスの規約とカテゴリ/セキュリティの注意事項は [API リファレンス索引](../api-reference.ja.md) を参照してください。

UnionAir はスクリプトのコンパイルサイクルを、メッセージごとの `file` / `line` / `column` / `code` を含む構造化された結果として記録します。UnionAir 以外から開始されたサイクルも記録されます。IDE でファイルを保存し、Unity がフォーカス取得時に自動リフレッシュするのが、プロジェクトが再コンパイルされる最も一般的な経路だからです。

以下の読み取りエンドポイントは **Read** カテゴリに属するため、既定で利用できます。

---

## POST /api/compile

スクリプトのコンパイルを要求し、ポーリング対象の id とともに `202` を返します。

> Asset Write カテゴリが有効な場合のみ呼び出せます。
> エンドポイントのリスクは `assetUpdate` です。

### リクエスト

```json
{
  "refresh": true,
  "clean": false,
  "requestId": "my-run-1"
}
```

| フィールド | 必須 | 既定値 | 説明 |
|-------|----------|---------|-------------|
| `refresh` | いいえ | `true` | コンパイル前に未反映のアセット変更をインポートする |
| `clean` | いいえ | `false` | ビルドキャッシュを破棄し全アセンブリを再ビルドする |
| `requestId` | いいえ | ― | 呼び出し側が指定する id。英数字・ハイフン・アンダースコアで最大 64 文字。`CON`、`NUL`、`COM1`、`LPT1` など Windows の予約デバイス名は拒否される |

ファイルが既にインポート済みでない限り `refresh` は有効のままにしてください。新規に書き込んだ `.cs` ファイルは Unity がインポートするまでどのアセンブリにも属さないため、リフレッシュなしのコンパイルでは認識されません。スクリプトに変更があればリフレッシュ自体がサイクルを開始するため、UnionAir が明示的にコンパイルを要求するのは開始されなかった場合だけです。これが `upToDate` を観測可能にしています。

`clean` は `RequestScriptCompilationOptions.CleanBuildCache` に対応し、すべてを再ビルドするため数分かかることがあります。

`requestId` を指定するとリクエストが回復可能になります。レスポンスを失った場合は、2回目のリクエストを送るのではなく `GET /api/compile/{requestId}` をポーリングしてください。

`refresh` が `true` の場合、コンパイルレコードを作成する前に `POST /api/editor/refresh` と同じ[読み込み中シーンの外部変更ガード](editor.ja.md#読み込み中シーンの競合--409)を実行します。報告されたすべてのシーンを明示的に保存または unload してから再試行してください。必要なファイル変更が既にインポート済みの場合に限り、`refresh: false` を指定してください。

### レスポンス — 202

```json
{
  "id": "c-20260728-040030-67c0fd",
  "state": "queued",
  "source": "unionAir",
  "sessionId": "f40cbf3fc3224a97b5b7ac7aa3b1ea38",
  "lifecycleGenerationAtRequest": 6,
  "statusUrl": "/api/compile/c-20260728-040030-67c0fd"
}
```

レコードは永続化され、このレスポンスはコンパイル作業が始まる **前** に送信されます。リフレッシュとコンパイルは Unity のメインスレッドをブロックし、domain reload で終わることがあります。そうなると、ポーリングに必要な id を呼び出し側が受け取る前に接続が切れてしまうためです。

### ステータスコード

コンパイルが既に実行中の場合は `activeCompile` オブジェクトを伴う `409`:

```json
{
  "error": "A script compilation is already active.",
  "activeCompile": { "id": "c-20260728-041549-2194f1", "source": "unionAir", "state": "queued" }
}
```

これは IDE 由来のコンパイルとの競合に負けたときの想定される応答であり、失敗ではありません。リクエストを再試行するのではなく `GET /api/compile` のポーリングに切り替えてください。

保持期間内で `requestId` が既に使われている場合は `existingCompile` オブジェクトを伴う `409` を返します。ボディには既存のレコード全体が含まれます。

`refresh: true` で読み込み中のシーンが外部変更されている場合は、`code: "loaded_scene_external_change_blocked"` を伴う `409` を返します。レスポンスの `loadedScenes` フィールドと復旧手順は [`POST /api/editor/refresh`](editor.ja.md#読み込み中シーンの競合--409) と同じです。この事前拒否ではコンパイルレコードを作成しません。

`202` レスポンス後、スケジュールされた refresh までの短い間にシーンが変更される可能性があるため、ガードは refresh の直前にも再実行されます。この競合が発生した場合、保持されたコンパイルレコードは `state: "aborted"`、`result: "notStarted"` となり、`error` に競合したシーンパスが記録されます。`AssetDatabase.Refresh()` は呼び出されません。

Editor が Play モードに入る途中または Play モード中、あるいはアセット更新中の場合も `409` を返します。`requestId` に使用できない文字が含まれる場合、または Windows の予約デバイス名の場合は `400` を返します。

```bash
curl -X POST http://localhost:8765/api/compile \
  -H "Content-Type: application/json" \
  -d '{"refresh":true}'
```

---

## GET /api/compile

実行中のコンパイルを `current`、直近に完了した **Editor** のコンパイルを `latest` として返します。どちらも `null` になり得ます。

両方を1つのレスポンスで返すのは、ポーリングするクライアントが同一スナップショットで両者を必要とするためです。別々のリクエストにすると、その間に完了したサイクルが `current` から `latest` へ移動して見落とされます。

### レスポンス

```json
{
  "current": null,
  "latest": {
    "id": "c-20260728-040030-67c0fd",
    "source": "external",
    "state": "completed",
    "result": "failed",
    "target": "editor",
    "sessionId": "f40cbf3fc3224a97b5b7ac7aa3b1ea38",
    "requestedAt": "2026-07-28T04:00:30.8656327Z",
    "startedAt": "2026-07-28T04:00:30.8656327Z",
    "finishedAt": "2026-07-28T04:00:32.0433942Z",
    "durationSeconds": 1.1777615,
    "lifecycleGenerationAtRequest": 6,
    "lifecycleGenerationAtFinish": 6,
    "errorCount": 1,
    "warningCount": 0,
    "assemblies": [
      {
        "name": "Assembly-CSharp",
        "path": "Library/ScriptAssemblies/Assembly-CSharp.dll",
        "outputDirectory": "Library/ScriptAssemblies",
        "compiled": true,
        "errorCount": 1,
        "warningCount": 0
      }
    ],
    "unchangedAssemblyCount": 71,
    "messages": [
      {
        "severity": "error",
        "code": "CS0103",
        "file": "Assets/Scratch/Player.cs",
        "line": 9,
        "column": 19,
        "assembly": "Assembly-CSharp",
        "message": "The name 'bar' does not exist in the current context",
        "raw": "Assets\\Scratch\\Player.cs(9,19): error CS0103: The name 'bar' does not exist in the current context"
      }
    ],
    "messagesTruncated": false,
    "error": null
  }
}
```

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `id` | string | このサイクルの識別子 |
| `source` | string | API 経由の要求は `unionAir`、それ以外は `external` |
| `state` | string | `queued` / `running` / `completed` / `aborted` |
| `result` | string \| null | 下記の result 表を参照。実行中は `null` |
| `target` | string | すべてのコンパイル出力が Editor アセンブリなら `editor`、すべてがプレイヤーアセンブリなら `player`、それ以外は `other` |
| `sessionId` | string | このレコードが属する Editor プロセス |
| `durationSeconds` | number | `startedAt` から `finishedAt` までの時間 |
| `lifecycleGenerationAtRequest` | number | サイクルを記録した時点の `lifecycleGeneration` |
| `lifecycleGenerationAtFinish` | number | サイクルが結果を報告した時点の `lifecycleGeneration` |
| `errorCount` / `warningCount` | number | `messages` が切り詰められていても、常に実際の総数 |
| `assemblies` | array | 実際にコンパイルされたアセンブリ |
| `unchangedAssemblyCount` | number | Unity がコンパイル不要と報告したアセンブリ数 |
| `messages` | array | 診断。エラーを先頭に、その後 file、line の順 |
| `messagesTruncated` | bool | 診断が 200 件を超えたかどうか |
| `error` | string \| null | 中断した場合、その理由 |

### メッセージのフィールド

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `severity` | string | コンパイラのメッセージ種別に基づく `error` / `warning` / `info` |
| `code` | string \| null | `CS0103` や `UNT0001` などの診断コード。存在しない場合は `null` |
| `file` | string \| null | スラッシュ区切りのプロジェクト相対パス。ビルドシステムの診断では `null` |
| `line` / `column` | number \| null | 1 始まりの位置。ソース位置を持たない診断では `null` |
| `message` | string | Unity が付与する位置とコードのプレフィックスを除いたテキスト |
| `raw` | string | コンパイラが報告したままのメッセージ |

> `severity` はメッセージ本文ではなく、常にコンパイラのメッセージ種別から取得します。"error" と "warning" という語はローカライズされますが、コードのトークンはされないためです。
> 個々のメッセージは 4000 文字、リストは 200 件で打ち切られます。

Player 出力には、従来の `Library/PlayerScriptAssemblies` ディレクトリと Unity 6 の `Library/Bee/PlayerScriptAssemblies` ディレクトリの両方が含まれます。大文字小文字と区切り文字を区別せずに照合しますが、名前が似ている無関係なディレクトリは `other` のままです。

### state と result

| `state` | `result` | 意味 |
|---------|----------|------|
| `queued` | `null` | コンパイルが要求されたがまだ開始していない |
| `running` | `null` | コンパイル実行中 |
| `completed` | `succeeded` | 1つ以上のアセンブリがエラーなくコンパイルされた |
| `completed` | `upToDate` | Unity がコンパイル済みアセンブリを0件と報告した。削除だけのサイクルでも発生することがある |
| `completed` | `failed` | 1件以上のエラーが報告された |
| `aborted` | `aborted` | サイクルは開始したが結果を報告しなかった |
| `aborted` | `notStarted` | コンパイルは要求されたがサイクルが開始しなかった |

`aborted` は、Editor のプログレスバーからのコンパイル中止、強制的な domain reload、サイクル途中での終了を含みます。`.asmdef` が壊れている場合などサイクルがまったく開始しなかった要求は、`error` メッセージ付きの `notStarted` になります。

### domain reload について

Unity が assembly domain をリロードするのは、ビルド **全体** が成功したときだけです。またリロード中は UnionAir のサーバーが停止します。

- `failed` のサイクルではリロードは **起きません**。サーバーは動き続け、同じ接続のまま結果を読めます。エラー修正時の高速パスです。
- `succeeded` のサイクルでは通常リロードが起きますが、常にではありません。Play モード、assembly reload のロック、ロード対象がないサイクルでは抑制されます。`succeeded` をリロードの保証として扱わないでください。
- `upToDate` のサイクルでも、最後のユーザースクリプトを削除してアセンブリがなくなる場合などはリロードされることがあります。`succeeded` と同様、リロードの有無は予測しません。

1つのアセンブリの失敗がサイクル全体のリロードを抑制するため、`Assets` 配下の失敗するスクリプトは、新しくコンパイルされたパッケージのコードのロードも妨げます。

接続断のあとにリロード完了を確認するための `lifecycleGeneration` は [`GET /api/editor/status`](editor.ja.md#domain-reload-の検出) を参照してください。

### 例

```bash
curl http://localhost:8765/api/compile
```

---

## GET /api/compile/records

保持されている終端状態のコンパイルレコードを、件数制限された新しい順の summary として列挙します。実行中の `current` は含まれないため、`GET /api/compile` から取得してください。完全なレコードは各 summary の `statusUrl` から取得できます。

| query | 既定値 | 説明 |
|-------|--------|------|
| `offset` | `0` | filter 適用後の結果に対する0以上の offset |
| `limit` | `20` | 1から100のページサイズ |
| `target` | すべて | `editor`、`player`、`other` の完全一致 filter。大文字小文字は区別しません |
| `source` | すべて | `unionAir`、`external` の完全一致 filter。大文字小文字は区別しません |
| `state` | すべて | `completed`、`aborted` の完全一致 filter。大文字小文字は区別しません |

filter はページングの前に適用されます。レコードは `finishedAt`、`requestedAt`、`id` の順にそれぞれ降順で並び、履歴が変わらない限り結果は決定的です。`total` はページング前の filter 済み件数、`hasMore` は返されたページの後に別のレコードがあるかを表します。

```json
{
  "total": 1,
  "offset": 0,
  "limit": 20,
  "hasMore": false,
  "records": [
    {
      "id": "c-20260728-040030-67c0fd",
      "source": "external",
      "state": "completed",
      "result": "succeeded",
      "target": "player",
      "requestedAt": "2026-07-28T04:00:30.0000000Z",
      "startedAt": "2026-07-28T04:00:30.1000000Z",
      "finishedAt": "2026-07-28T04:00:34.0000000Z",
      "durationSeconds": 3.9,
      "errorCount": 0,
      "warningCount": 0,
      "statusUrl": "/api/compile/c-20260728-040030-67c0fd"
    }
  ]
}
```

不正な filter またはページング値には `400` を返します。履歴が空の場合は `total: 0` と空の `records` 配列を返します。保持レコードのディレクトリを列挙できない場合は、不完全な scan を完全な結果として見せずに `500` を返します。

```bash
curl "http://localhost:8765/api/compile/records?target=player&offset=0&limit=20"
```

---

## GET /api/compile/{id}

保持されているコンパイルレコードを1件返します。

**特定の** サイクルが完了したことを確認するには、`latest` ではなくこちらを使用してください。IDE から開始されたコンパイルはいつでも `latest` を置き換えるため、`latest` が答えるのは「何らかのコンパイルが完了したか」であって「自分のコンパイルが完了したか」ではありません。

UnionAir は直近 20 件のレコードを `Library/UnionAir/Compile/records` 配下に保持します。追い出されたか未知の id は `404` を返します。英数字・ハイフン・アンダースコア以外を含む id は `400` を返します。

レスポンスボディは上記と同じレコードオブジェクトで、`current` / `latest` のラッパーはありません。

```bash
curl http://localhost:8765/api/compile/c-20260728-040030-67c0fd
```

---

## コンパイルと修正のループ

自動化クライアントが回すループは、`.cs` ファイルを書き、コンパイルを要求し、診断を読み、修正して繰り返す、というものです。

```
1. ファイルを書く。
2. POST /api/compile                 -> 202 { id, lifecycleGenerationAtRequest }
3. GET /api/compile/{id} を state が "completed" または "aborted" になるまでポーリングする。
4. result == "failed"    -> messages[].file / line / column を使って修正し、2 へ戻る
   result == "succeeded" -> 完了
   result == "upToDate"  -> 完了
   state  == "aborted"   -> エラーを報告する。やみくもに再試行しない
```

1サイクルは通常、数秒で決着します。

### 正しく終了する

自動化クライアントが最もハングしやすいのがここです。`succeeded` と `upToDate` のどちらも domain reload の有無を予測しません。Play モードや assembly reload のロックはリロードを抑制する一方、削除だけのサイクルはコンパイル済みアセンブリが0件でもリロードすることがあります。Unity の API から UnionAir が事前に判断する手段はありません。`lifecycleGeneration` の増加を無条件で待つと、リロードが起きない場合に永久に待ち続けます。

代わりに次のように終了してください。

1. `failed` — **完了。リロードを待ってはいけません**。whole build の失敗ではリロードされません。
2. `succeeded` または `upToDate` で、サーバーが応答し `settled: true` — コンパイル結果は完了です。事前にリロードを待たず次へ進みます。
3. どちらかの成功結果のあとに接続が **切れた場合** — 再接続し、`lifecycleGeneration` が `lifecycleGenerationAtRequest` を超えるまで待ちます。これによりクラッシュではなくリロードが完了したことを確認できます。
4. **すべての待機に明示的なタイムアウトを設けてください。** 上記のどのステップも無制限にポーリングしてはいけません。

> `settled` はスナップショットであり保証ではありません。コンパイルはネイティブ側の domain reload が始まるわずか前に `isCompiling` をクリアするため、接続を失う直前に `settled: true` を観測することが正当に起こり得ます。ステップ3が存在し、タイムアウトが必須である理由がこれです。

### あらゆる箇所で接続断を許容する

接続拒否は、コンパイル要求の後だけでなく **すべての** リクエストで通常の状態として扱ってください。誰かが IDE でファイルを保存すればいつでもコンパイルと domain reload が発生し、リロード中はサーバーが停止します。

コンパイルの同期的な終盤で、Unity はメインスレッドをブロックします。`EditorApplication.update` もそれに伴い停止し、UnionAir は同じループからキューされた HTTP リクエストを処理するため、応答が遅延します。数秒のレイテンシは想定内であり、失敗のシグナルではありません。

### 1つの失敗がすべてを止める

Unity が assembly domain をリロードするのは、ビルド全体が成功したときだけです。したがって `Assets` 配下の1つの失敗するスクリプトが、正常にコンパイルされたパッケージを含む **すべての** 新しいコードのロードを妨げます。パッケージコードの変更が反映されていないように見えるときは、他を調べる前に `GET /api/compile` で無関係な失敗がないか確認してください。

### 競合に負けた場合

リクエスト送信中に他所でコンパイルが開始されていた場合、`POST /api/compile` は `activeCompile` オブジェクトを伴う `409` を返します。これは再試行すべきエラーではなく正しい応答です。`GET /api/compile` のポーリングに切り替え、実行中のサイクルを待機対象として扱ってください。

---

## 関連ドキュメント

- [Editor API](editor.ja.md) — `lifecycleGeneration`、`settled`、Console ログ
- [API リファレンス索引](../api-reference.ja.md)
