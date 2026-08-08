# API リファレンス — Test Runner

[English](testing.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](testing.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`。実際の URL は接続時に `<project>/.unionair/endpoint.txt` から読み取ってください。エンドポイントの発見手順、共通のレスポンス規約、セキュリティ上の注意は [API リファレンス索引](../api-reference.ja.md) を参照してください。

Test Runner API は `com.unity.test-framework` 導入時のみ表示されます。**Test Runner** カテゴリは既定で無効であり、**Window > UnionAir > REST Bridge** で有効化する必要があります。

UnionAir が保持するのは current run の metadata と、最後に完了した UnionAir run 1件だけです。履歴や case 単位の JSON 結果は保持しません。永続的なレポートが必要な場合は、完了後すぐに `results.xml` をダウンロードしてください。

---

## GET /api/tests

leaf test を発見し、フラットなページング済み一覧を返します。Editor 内での発見は非同期であり、同時に実行できる discovery request は1件だけです。

namespace、class、パラメータ化 method といった suite node は一覧に含まれません。これらは [POST /api/test-runs](#post-apitest-runs) の `testNames` および `groupNames` に指定できる正当な値です。

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

EditMode または PlayMode の非同期 run を1件開始し、`202 Accepted` を返します。filter をすべて省略すると、指定 mode の全テストを実行します。4つの filter field がテストをどう選択するかは [フィルタ](#フィルタ) を参照してください。

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

`mode` は必須です。`profiling`は任意で、[Profiling session設定](profiling.ja.md#post-apiprofilingsessions)を使用します。指定時はTest RunnerとProfilingの両カテゴリを有効にする必要があります。

### フィルタ

4つの filter field は空でない文字列の配列として任意指定でき、そのまま Unity Test Framework に渡されます。`groupNames` は実行前に正規表現として構文検証されますが、それ以外に「その名前のテストが実在するか」を検査する field はありません。

| Field | 照合 | 大文字小文字 | suite に当たる |
|-------|----------|------|----------------|
| `testNames` | full name との完全一致。正規表現ではない | 区別する | あり |
| `groupNames` | full name に対する非アンカー正規表現 | パターン次第 | あり |
| `categoryNames` | 各 category に対する非アンカー正規表現 | パターン次第 | — |
| `assemblyNames` | `.dll` を除いた assembly 名との完全一致 | 区別しない | — |

同一 field 内の複数値は OR、異なる field 同士は AND で結合されます。先頭が `!` の値は、選択ではなく除外として扱われます。category を宣言していないテストは `Uncategorized` として照合されます。

`testNames` と `groupNames` は test case だけでなく suite にも当たります。namespace、class、パラメータ化 method はいずれも test tree の node であり、これらを指定すると配下のテストがすべて実行されます。これらの名前は leaf test のみを返す [GET /api/tests](#get-apitests) には含まれないため、class や namespace で絞り込む場合は呼び出し側が名前を組み立てることになります。パラメータ化 method の node 名は開き括弧の直前までであり、開き括弧自体を含めてはいけません。`Example.EditorTests.Rounds` は `Example.EditorTests.Rounds(1,2)` のすべての case を選択しますが、`Example.EditorTests.Rounds(` は何にも一致しません。method 配下の leaf 名は引数リストを含み、引数リスト自体が `.` や `(` を含むことがあります。

### 何にも一致しない run

filter が1件も一致しないことはエラーではありません。実行されたテストがなく、したがって失敗したテストもないため、run は `result: "passed"`、`progress.completed: 0`、すべて 0 の summary で完了します。assembly 名の綴り違い、rename された test assembly、存在しなくなった category、一致しなくなった `groupNames` パターンは、いずれもこの状態に至ります。

`result` が報告するのは「何かが失敗したか」であって、「filter が呼び出し側の意図通りに選択したか」ではありません。filter を使うクライアントは、`progress.completed` を、その filter で選択されるはずのテスト件数と突き合わせてください。`progress.total` はその件数ではありません。[GET /api/test-runs/{id}](#get-apitest-runsid) を参照してください。

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

`id` は UnionAir が発行します。このページのすべての endpoint が受け取るのはこの id であり、Unity Test Framework 自身の run 識別子ではありません。framework が自身の識別子を返すのは run を投入した後であり、run を開始する前に記録するには遅すぎるためです。framework の識別子は cancel 用として UnionAir が内部で保持します。

Editor が Play 中または Play mode 遷移中、compile/update 中、別のテスト実行中は `409` です。導入済みの Unity Test Framework versionから同時実行防止に必要なactive run状態を取得できない場合は`503`を返し、UnionAirはこの互換性エラーをdomain loadごとに1回記録します。run record を書き込めなかった場合は `500` を返し、このとき Unity Test Framework には何も渡されていません([Editor アクティビティ](activities.ja.md) を参照)。UnionAir 独自 timeout はなく、ハングや cancel cleanup は Unity Test Framework の動作に従います。

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
  "progress": { "completed": 1, "total": 128 },
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

`progress.completed` は terminal result が報告された test case の件数、すなわち `summary` の4項目の合計であり、body が実行された件数ではありません。skipped の case は body が実行されないまま計上されます。inconclusive の case は pass でも fail でもない terminal result として計上されるもので、`Assert.Inconclusive` は test body や setup の実行中に報告されるため、case 自体は実行されています。run を解釈する際は `summary.skipped` と `summary.inconclusive` をそれぞれ確認してください。`progress.total` は当該 mode の test tree 全体の件数であり、filter によって絞り込まれません。したがって filter が選択した件数ではなく上限値です。1つの assembly に絞った run は、選択されたテストがすべて成功しても `completed` が `total` を大きく下回ったまま終了します。期待件数と突き合わせるのは `total` ではなく `completed` です。[何にも一致しない run](#何にも一致しない-run) を参照してください。

current metadata は domain reload を越えて保持されます。Editor 再起動後に未完了 metadata が残っている場合、UnionAir は以前の latest XML を置き換えずに `aborted` と確定します。

テスト単位の progress はメモリから直接返し、ディスクへは最大で 1 秒に 2 回保存します。run の状態遷移時と assembly reload 前には即時保存します。したがってプロセスが突然クラッシュした場合、最大で約 0.5 秒分の progress metadata が失われる可能性がありますが、保持済みの latest result XML には影響しません。

---

## DELETE /api/test-runs/{id}

active UnionAir run の cancel を要求し、`canceling` state とともに `202` を返します。不明または外部 run ID は `404`、完了済み、canceling 済み、その他 cancel 不能な run は `409` です。Unity Test Framework の handle が得られない run も `409` になりますが、これは framework が run を受理しながら識別子を返さなかった場合に限られます。cancel は非同期なので status の polling を継続してください。

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
