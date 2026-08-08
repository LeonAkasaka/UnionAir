# API リファレンス - Profiling
[English](profiling.md) | **日本語**

ベース URL: `http://localhost:<port>/api/`。実際の URL は接続時に `<project>/.unionair/endpoint.txt` から読み取ってください。エンドポイントの発見手順は [API リファレンス索引](../api-reference.ja.md) を参照してください。

**Profiling** カテゴリは既定で無効です。キャプチャは大容量になる場合があり、メモリスナップショットにはプロジェクトやmanaged heapのデータが含まれるため、信頼できるローカルクライアントに対してのみ **Window > UnionAir > REST Bridge** で有効化してください。

UnionAirが計測するのはUnity Editorプロセスです。Play ModeのサンプルにもEditorのオーバーヘッドが含まれるため、ローカルデバッグと回帰調査向けであり、ターゲットハードウェア上のDevelopment Player計測の代替ではありません。

## GET /api/profiling/metrics

現在のEditorで`ProfilerRecorder`が利用できるcounter / markerを返します。返された`metricId`をセッション作成時に指定します。

query parameterは`search`、`category`、`offset`(既定`0`)、`limit`(既定`100`、最大`1000`)です。一般的なmarkerには`mainThreadTime`、`renderThreadTime`、`gcAllocInFrame`、`gcUsedMemory`、`totalUsedMemory`、`totalReservedMemory`という安定aliasがあります。それ以外のIDは`<category>:<marker>`です。

```json
{
  "schemaVersion": 1,
  "total": 1,
  "offset": 0,
  "limit": 100,
  "metrics": [{
    "metricId": "mainThreadTime",
    "category": "Internal",
    "marker": "Main Thread",
    "unit": "ms",
    "dataType": "Int64",
    "available": true
  }]
}
```

## POST /api/profiling/sessions

非同期profiling sessionを1件開始します。armedまたは実行中のsessionは同時に1件だけです。

```json
{
  "label": "inventory-scroll",
  "metrics": ["mainThreadTime", "gcAllocInFrame", "gcUsedMemory"],
  "warmupFrames": 60,
  "maxFrames": 600,
  "maxDurationSeconds": 30,
  "captureRaw": false
}
```

すべてのfieldは任意です。既定値は利用可能な標準alias、60 warmup frame、600計測frame、30秒、raw無効です。`metrics`は重複しないmetric IDを最大64件、`warmupFrames`は0-10000、`maxFrames`は1-100000、`maxDurationSeconds`は0より大きく3600以下です。recorder負荷とmemory上の統計を制限するため、64件を超えるmetricは`422`を返します。

明示したmetricが利用できない場合は`422`、別sessionまたは外部Profiler binary log設定との競合は`409`、保存量が5 GiB以上の場合は完了済み成果物が削除されるまで`507`です。

## GET /api/profiling/sessions

保持中のsession metadataを一覧します。UnionAirは共有5 GiB上限の範囲で新しい順に10 sessionを保持します。

## GET /api/profiling/sessions/{id}

設定、source、environment、サンプルの連続性、metric統計、成果物を返します。状態は`armed`、`warming`、`running`、`completed`、`aborted`、`failed`です。

metric統計はsessionが終端状態へ到達した時点で確定され、それ以降はcache済みmetadataから読み出されます。`armed`、`warming`、`running`の間は`metrics`が空objectになります。軽量なstateとsampling fieldをpollし、完了後に確定統計を取得してください。これにより、増加中のNDJSONを繰り返し走査して計測対象へ影響を与えることを防ぎます。

統計には単位とsamples、min、max、mean、p50、p95、p99、first、last、delta、nonZeroSamplesが含まれます。assembly reload後は同じIDの新しいsegmentとして再開してwarmupをやり直し、`continuous:false`と中断理由を返します。AI agentはsegment間を連続時系列として解釈しないでください。

assembly reload後にrecorderの復元が失敗した場合、UnionAirはsessionを`failed`にし、所有していたProfiler設定を復元してactive-session lockを解除します。メモリ上のrecorder状態が失われたactive recordは、`stop`の呼び出しでも修復されます。

成果物には`projectRelativePath`、download URL、size、SHA-256が含まれます。

## POST /api/profiling/sessions/{id}/stop

sessionを停止して確定します。確定済みsessionへの呼び出しは冪等で、現在の結果を返します。

## DELETE /api/profiling/sessions/{id}

完了済みsessionと全成果物を削除します。実行中は`409`です。

## GET /api/profiling/sessions/{id}/samples.ndjson

frame sampleを`application/x-ndjson`でダウンロードします。metric値の順序はsession設定と同じです。

このendpointはsession実行中も利用できます。その場合、downloadをopenした時点のfile長に固定した安定した部分snapshotを返します。以後に追加されたframeは次のrequestで取得できます。

```jsonl
{"segment":1,"frame":1,"segmentFrame":61,"elapsedMs":1016.7,"values":[7.2,0,104857600]}
```

## GET /api/profiling/sessions/{id}/profile.raw

`captureRaw:true`で実行して完了したsessionのUnity Profiler binary logをダウンロードします。

NDJSON、`.raw`、`.snap`のresponse bodyはrequest検証後にbackground I/O threadでstreamingされるため、大きなdownloadがUnity Editorのupdate loopを占有しません。clientが切断された場合はそのresponseだけが終了し、保持済み成果物は削除されません。

## Memory snapshot

### POST /api/memory-snapshots

managed objects、native objects、native allocationsを含むMemory Profiler snapshotを非同期取得します。同時取得は1件だけです。

```json
{
  "label": "after-20-load-cycles",
  "profilingSessionId": "optional-related-id",
  "testRunId": "optional-related-id"
}
```

### GET /api/memory-snapshots と GET /api/memory-snapshots/{id}

一覧または1件のsnapshotを返します。状態は`capturing`、`completed`、`failed`です。レスポンスにはenvironment、取得前後の概算memory counter、`.snap`成果物が含まれます。これらは調査の根拠であり、UnionAirは結果を確定したleakとは判定しません。

共有5 GiB上限の範囲で新しい順に4 snapshotを保持します。assembly reloadで中断した取得はfailedとなり、部分ファイルは公開されません。

### GET /api/memory-snapshots/{id}/snapshot

完了済み`.snap`をUnity Memory Profilerまたはローカル解析ツール向けに`application/octet-stream`でダウンロードします。

### DELETE /api/memory-snapshots/{id}

完了またはfailedのsnapshotを削除します。取得中は`409`です。

## Test Runner連携

Unity Test Framework導入時、`POST /api/test-runs`はsession作成と同じfieldを持つ任意の`profiling` objectを受け付けます。**Test Runner**と**Profiling**の両カテゴリを有効にする必要があります。

```json
{
  "mode": "playMode",
  "testNames": ["Example.InventoryPerformanceTest"],
  "profiling": {
    "metrics": ["mainThreadTime", "gcAllocInFrame"],
    "warmupFrames": 30,
    "maxFrames": 300
  }
}
```

testのレスポンスとstatusには`profilingSessionId`と`profilingUrl`が含まれます。sessionは実行前にarmedとなり、`RunStarted`で開始し、完了・cancel・abort時に確定します。test-run lock中もprofiling statusと成果物の取得、停止は利用できますが、新規sessionは作成できません。
