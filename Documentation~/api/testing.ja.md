# API リファレンス — Test Runner

[English](testing.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](testing.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`(デフォルトポート: **8765**)。共通のレスポンス規約とセキュリティ上の注意は [API リファレンス索引](../api-reference.ja.md) を参照してください。

Test Runner API は `com.unity.test-framework` 導入時のみ表示されます。**Test Runner** カテゴリは既定で無効であり、**Window > UnionAir > REST Bridge** で有効化する必要があります。

UnionAir が保持するのは current run の metadata と、最後に完了した UnionAir run 1件だけです。履歴や case 単位の JSON 結果は保持しません。永続的なレポートが必要な場合は、完了後すぐに `results.xml` をダウンロードしてください。

---

## GET /api/tests

leaf test を発見し、フラットなページング済み一覧を返します。Editor 内での発見は非同期であり、同時に実行できる discovery request は1件だけです。

### クエリパラメータ

| パラメータ | 必須 | 既定 | 説明 |
|-----------|----------|---------|-------------|
| `mode` | はい | — | `editMode` または `playMode` |
| `search` | いいえ | — | name、full name、unique name の大文字小文字を区別しない部分一致 |
| `assembly` | いいえ | — | `.dll` を除いた test assembly 名との完全一致 |
| `category` | いいえ | — | NUnit category との大文字小文字を区別しない完全一致 |
| `offset` | いいえ | `0` | 0以上の結果 offset |
| `limit` | いいえ | `100` | 1から1000のページサイズ |

### レスポンス

```json
{
  "mode": "editMode",
  "total": 1,
  "offset": 0,
  "limit": 100,
  "tests": [{
    "name": "SavesAsset",
    "fullName": "Example.EditorTests.SavesAsset",
    "uniqueName": "Example.EditorTests.dll/Example/[Example.EditorTests.SavesAsset]",
    "assembly": "Example.EditorTests",
    "categories": ["Smoke"],
    "runState": "Runnable",
    "description": "",
    "skipReason": ""
  }]
}
```

不正な parameter は `400`、同時 discovery は `409` です。assembly reload で中断された pending discovery は、可能な場合 `409` で完了します。

---

## POST /api/test-runs

EditMode または PlayMode の非同期 run を1件開始し、`202 Accepted` を返します。filter をすべて省略すると、指定 mode の全テストを実行します。filter field は Unity Test Framework の `Filter` semantics に従い、field の組み合わせも同 framework が処理します。

### リクエスト

```json
{
  "mode": "editMode",
  "testNames": ["Example.EditorTests.SavesAsset"],
  "groupNames": ["^Example\\."],
  "categoryNames": ["Smoke"],
  "assemblyNames": ["Example.EditorTests"],
  "profiling": {
    "metrics": ["mainThreadTime", "gcAllocInFrame"],
    "warmupFrames": 30,
    "maxFrames": 300
  }
}
```

`mode` は必須です。4つの filter は空でない文字列の配列として任意指定できます。`groupNames` は正規表現であり、実行前に検証されます。`profiling`は任意で、[Profiling session設定](profiling.ja.md#post-apiprofilingsessions)を使用します。指定時はTest RunnerとProfilingの両カテゴリを有効にする必要があります。

### レスポンス — 202

```json
{
  "id": "0dc6f2b8-9c31-4da0-82df-7dc8fb0dc352",
  "state": "queued",
  "statusUrl": "/api/test-runs/0dc6f2b8-9c31-4da0-82df-7dc8fb0dc352",
  "resultUrl": "/api/test-runs/0dc6f2b8-9c31-4da0-82df-7dc8fb0dc352/results.xml",
  "profilingSessionId": "8c3fe76a-4a6a-4dd9-a678-02b628ce5d12",
  "profilingUrl": "/api/profiling/sessions/8c3fe76a-4a6a-4dd9-a678-02b628ce5d12"
}
```

Editor が Play 中または Play mode 遷移中、compile/update 中、別のテスト実行中は `409` です。導入済みの Unity Test Framework versionから同時実行防止に必要なactive run状態を取得できない場合は`503`を返し、UnionAirはこの互換性エラーをdomain loadごとに1回記録します。UnionAir 独自 timeout はなく、ハングや cancel cleanup は Unity Test Framework の動作に従います。

---

## GET /api/test-runs/{id}

current run または最後に完了した UnionAir run を返します。それ以前の ID は `404` です。

```json
{
  "id": "0dc6f2b8-9c31-4da0-82df-7dc8fb0dc352",
  "state": "completed",
  "result": "passed",
  "mode": "editMode",
  "filters": {
    "testNames": ["Example.EditorTests.SavesAsset"],
    "groupNames": [],
    "categoryNames": [],
    "assemblyNames": []
  },
  "startedAt": "2026-07-18T05:00:00.0000000Z",
  "finishedAt": "2026-07-18T05:00:01.0000000Z",
  "currentTest": null,
  "progress": { "completed": 1, "total": 1 },
  "summary": {
    "passed": 1,
    "failed": 0,
    "skipped": 0,
    "inconclusive": 0,
    "duration": 0.15,
    "assertCount": 1
  },
  "resultFileAvailable": true,
  "resultUrl": "/api/test-runs/0dc6f2b8-9c31-4da0-82df-7dc8fb0dc352/results.xml",
  "profilingSessionId": null,
  "profilingUrl": null
}
```

`state` は `queued`、`running`、`canceling`、`completed`、`aborted` です。完了前の `result` は `null`、完了後は `passed`、`failed`、`skipped`、`inconclusive`、`canceled`、`aborted` のいずれかです。時刻は UTC ISO 8601 文字列です。

current metadata は domain reload を越えて保持されます。Editor 再起動後に未完了 metadata が残っている場合、UnionAir は以前の latest XML を置き換えずに `aborted` と確定します。

テスト単位の progress はメモリから直接返し、ディスクへは最大で 1 秒に 2 回保存します。run の状態遷移時と assembly reload 前には即時保存します。したがってプロセスが突然クラッシュした場合、最大で約 0.5 秒分の progress metadata が失われる可能性がありますが、保持済みの latest result XML には影響しません。

---

## DELETE /api/test-runs/{id}

active UnionAir run の cancel を要求し、`canceling` state とともに `202` を返します。不明または外部 run ID は `404`、完了済み、canceling 済み、その他 cancel 不能な run は `409` です。cancel は非同期なので status の polling を継続してください。

---

## GET /api/test-runs/{id}/results.xml

最後に完了した UnionAir run について、Unity Test Framework が保存した完全な NUnit XML をダウンロードします。

- Content type: `application/xml; charset=utf-8`
- Content disposition: `attachment; filename="TestResults-{id}.xml"`
- active run ID: `409`
- 古い ID、外部 run、XML のない aborted run、利用不能結果: `404`

UnionAir は `Library/UnionAir/TestRuns/latest.xml` を、temp file、旧fileのbackup、pending marker、SHA-256整合性検証を使う復旧可能なtransactionとして書き込みます。起動時に中断transactionが見つかった場合、完全なcommitとして確定するか、以前のXMLとmetadataへrollbackします。filesystem path は内部詳細であり API では公開しません。新しい run の実行中も以前の latest XML はダウンロードできます。`RunFinished` で保存に成功すると置き換わり、以前の ID は `404` になります。XML のない aborted run は以前の結果を維持します。

---

## テスト実行中の同時利用

Test Runner Window から開始したものを含む、すべての Unity Test Framework run が UnionAir の操作を lock します。利用可能なのは次だけです。

- `GET /api/health`、`GET /api/help`、`GET /api/editor/status`、`GET /api/editor/logs`
- 上記 run status、result XML、cancel endpoint
- `Origin` のない `OPTIONS`(`Origin` を持つリクエストは、この lock の評価前に `403` で拒否されます)

その他の built-in / custom endpoint は、active run source と該当する場合は UnionAir run ID を含む `409` を返します。このlockはcategory enablementより先に評価されるため、無効カテゴリが通常返す`403`より優先されます。外部 run は editor status と logs で観測できますが、cancel と結果保持の対象外です。完了callbackを取りこぼした場合、UnionAirはUnity Test Frameworkがgrace periodにわたってidleだと確実に観測できた場合のみstale gateを解除し、stale UnionAir runは`aborted`と確定します。
