# API リファレンス — Compile

[English](compile.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](compile.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`(デフォルトポート: **8765**)。レスポンスの規約とカテゴリ/セキュリティの注意事項は [API リファレンス索引](../api-reference.ja.md) を参照してください。

UnionAir はスクリプトのコンパイルサイクルを、メッセージごとの `file` / `line` / `column` / `code` を含む構造化された結果として記録します。UnionAir 以外から開始されたサイクルも記録されます。IDE でファイルを保存し、Unity がフォーカス取得時に自動リフレッシュするのが、プロジェクトが再コンパイルされる最も一般的な経路だからです。

以下の読み取りエンドポイントは **Read** カテゴリに属するため、既定で利用できます。

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
| `target` | string | アセンブリの出力先から判定した `editor` / `player` / `other` |
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

### state と result

| `state` | `result` | 意味 |
|---------|----------|------|
| `queued` | `null` | コンパイルが要求されたがまだ開始していない |
| `running` | `null` | コンパイル実行中 |
| `completed` | `succeeded` | 1つ以上のアセンブリがエラーなくコンパイルされた |
| `completed` | `upToDate` | コンパイルが必要なものが何もなかった |
| `completed` | `failed` | 1件以上のエラーが報告された |
| `aborted` | `aborted` | サイクルは開始したが結果を報告しなかった |
| `aborted` | `notStarted` | コンパイルは要求されたがサイクルが開始しなかった |

`aborted` は、Editor のプログレスバーからのコンパイル中止、強制的な domain reload、サイクル途中での終了を含みます。`.asmdef` が壊れている場合などサイクルがまったく開始しなかった要求は、`error` メッセージ付きの `notStarted` になります。

### domain reload について

Unity が assembly domain をリロードするのは、ビルド **全体** が成功したときだけです。またリロード中は UnionAir のサーバーが停止します。

- `failed` のサイクルではリロードは **起きません**。サーバーは動き続け、同じ接続のまま結果を読めます。エラー修正時の高速パスです。
- `succeeded` のサイクルでは通常リロードが起きますが、常にではありません。Play モード、assembly reload のロック、ロード対象がないサイクルでは抑制されます。`succeeded` をリロードの保証として扱わないでください。
- `upToDate` のサイクルではリロードは決して起きません。

1つのアセンブリの失敗がサイクル全体のリロードを抑制するため、`Assets` 配下の失敗するスクリプトは、新しくコンパイルされたパッケージのコードのロードも妨げます。

接続断のあとにリロード完了を確認するための `lifecycleGeneration` は [`GET /api/editor/status`](editor.ja.md#domain-reload-の検出) を参照してください。

### 例

```bash
curl http://localhost:8765/api/compile
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

## 関連ドキュメント

- [Editor API](editor.ja.md) — `lifecycleGeneration`、`settled`、Console ログ
- [API リファレンス索引](../api-reference.ja.md)
