# API リファレンス

[English](api-reference.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](api-reference.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`。port mode の既定は Automatic であり固定のデフォルトポートは存在しないため、実際の URL は接続時に `<project>/.unionair/endpoint.txt` から読み取り、接続を拒否された場合は読み直してください。手順は [サーバの確認](index.ja.md#2-サーバの確認) にあります。

エンドポイントに別の media type の記載がない限り、レスポンスは `Content-Type: application/json; charset=utf-8` で返されます。NUnit 結果のダウンロードは `application/xml` です。レスポンスには CORS を許可するヘッダーが含まれません。`Origin` ヘッダーを持つリクエストは `403` で拒否されるため、ブラウザの `fetch` と XMLHttpRequest は既定で非対応です。ローカル CLI や連携クライアントは `Origin` を送信しないでください。
JSON レスポンス内の文字列フィールドは制御文字を含め一貫してエスケープされます。
非有限の浮動小数点値(`NaN`、`Infinity`、`-Infinity`)は JSON 数値フィールドでは `null` として出力されます。

空でないボディを持つすべてのリクエストには `Content-Type: application/json` が必要です。`charset=utf-8` などの media type parameter は使用できます。それ以外または未指定の Content-Type は `415` を返します。空のリクエストに Content-Type は不要です。

ボディが任意または未使用の POST エンドポイントは空のボディを受け付けます。クライアントは空の POST に `Content-Length: 0` を付ける必要があります。`Content-Length` と `Transfer-Encoding` のどちらもない POST は、UnionAir に到達する前に Windows の `HttpListener` によって `411 Length Required` で拒否される場合があります。標準的な HTTP ライブラリと `curl -X POST` は通常、長さゼロのヘッダーを自動的に追加します。

エラーは `{"error":"<message>"}` 形式の JSON と適切な HTTP ステータスコードで返されます。無効化されたカテゴリの endpoint は通常 `403` を返します。テスト実行中はtest-run lockが先に評価されるため、許可対象外のendpointはカテゴリが無効でも`activeTestRun`を含む`409`を返します。run終了後の再試行では`403`になる場合があります。

全エンドポイントの機械可読マニフェストは [`GET /api/help`](api/general.ja.md#get-apihelp) から取得できます。

---

## カテゴリ

エンドポイントは UnionAir EditorWindow(**Window > UnionAir > REST Bridge**)で有効/無効を切り替えられるカテゴリにグループ化されています。常時有効なのは **Read** のみで、それ以外のカテゴリはすべて既定で無効です。

| カテゴリ | ID | 既定 |
|----------|----|---------|
| Read | `read` | 有効(無効化不可) |
| Scene Write | `sceneWrite` | 無効 |
| Asset Write | `assetWrite` | 無効 |
| Play Mode | `playMode` | 無効 |
| Editor Actions | `editorActions` | 無効 |
| Test Runner | `testRunner` | 無効。Unity Test Framework 導入時のみ表示 |
| Profiling | `profiling` | 無効 |
| Build | `build` | 無効 |
| Custom | `custom` | カスタムカテゴリごと |

---

## ページ一覧

### [General](api/general.ja.md)

`GET /api/help` · `GET /api/health` · カスタムコントローラ概要 · **オブジェクト参照**(すべての書き込み/詳細 API で使う型付き参照形式)

### [Editor](api/editor.ja.md)

Editor の状態、ログ、選択、カメラ、隔離 object/animation preview、キャプチャ、リフレッシュ、メニュー項目:

`GET /api/editor/status` · `GET /api/editor/logs` · `GET /api/editor/logs.ndjson` · `GET|POST /api/editor/selection` · `POST /api/editor/ping` · `GET /api/cameras` · `GET /api/cameras/capture` · `GET /api/cameras/capture/image` · `POST /api/previews/render` · `POST /api/previews/render/image` · `POST /api/editor/refresh` · `GET /api/editor/menu-items` · `POST /api/editor/menu-item` · `GET /api/editor/capture` · `GET /api/editor/capture/image`

### [Scenes](api/scenes.ja.md)

シーン情報、階層、マルチシーン管理、統計、保存:

`GET /api/scene` · `GET /api/scene/hierarchy` · `GET /api/scenes` · `POST /api/scenes/new` · `POST /api/scenes/open` · `POST /api/scenes/unload` · `POST /api/scenes/active` · `GET /api/scene/stats` · `POST /api/scene/save`

### [GameObjects & Components](api/gameobjects.ja.md)

GameObject の読み取り、検索、作成/更新/削除、コンポーネント操作:

`GET /api/gameobjects` · `GET /api/search/gameobjects` · `POST|DELETE|PATCH /api/gameobjects` · `POST /api/gameobjects/primitive` · `POST /api/gameobjects/instantiate` · `POST /api/gameobjects/duplicate` · `POST /api/gameobjects/reparent` · `POST /api/gameobjects/batch` · `POST|DELETE|PATCH /api/gameobjects/components`

### [Assets](api/assets.ja.md)

アセットの閲覧、検索、プレハブ、マテリアル、ScriptableObject、インポーター:

`GET /api/assets` · `GET /api/assets/{guid}` · `GET /api/search/asset-refs` · `GET /api/assets/dependents` · `POST /api/assets/prefabs`(+ `apply`、`revert`) · `GET /api/assets/materials/{guid}` · `POST|PATCH /api/assets/materials` · `GET /api/assets/shaders/{guid}` · `GET /api/assets/shaders` · `DELETE /api/assets/{guid}` · `POST /api/assets/move` · `POST /api/assets/open` · `POST /api/assets/reimport` · ScriptableObject CRUD(`/api/assets/scriptableobjects`) · `PATCH /api/assets/texture-importer/{guid}` · `GET|PATCH /api/assets/audio-importer/{guid}` · ModelImporter の core、material/remap、rig/Avatar、imported clip 設定 (`GET|PATCH /api/assets/model-importer/{guid}`、`POST /api/assets/model-importer/{guid}/preflight`)

### [Animation](api/animation.ja.md)

AnimationClip / AnimatorController のオーサリング:

`POST /api/assets/animation-clips` · `GET|PATCH /api/assets/animation-clips/{guid}` · `POST|DELETE .../curves` · `POST|DELETE .../events` · `POST /api/assets/animator-controllers` · `GET /api/assets/animator-controllers/{guid}` · parameters / layers / states / transitions / blend-trees / state-machines / state-machine-transitions サブエンドポイント

### [Play Mode](api/playmode.ja.md)

Play モード制御、Input System シミュレーション、画面クエリ、UI 操作:

`POST /api/editor/play` · `POST /api/editor/stop` · `POST /api/editor/pause` · `POST /api/editor/step` · `GET /api/playmode/input/actions` · `POST /api/playmode/input/perform` · `POST /api/playmode/input/set` · `POST /api/playmode/input/pointer` · `GET /api/playmode/input/result` · `POST /api/playmode/screen/hittest` · `GET /api/playmode/ui/elements` · `POST /api/playmode/ui/click` · `POST /api/playmode/ui/text` · `POST /api/playmode/ui/scroll` · `POST /api/playmode/ui/value`

### [Compile](api/compile.ja.md)

UnionAir 以外から開始されたサイクルも含む、構造化されたスクリプトコンパイル結果:

`POST /api/compile` · `GET /api/compile` · `GET /api/compile/records` · `GET /api/compile/{id}`

### [Test Runner](api/testing.ja.md)

Unity Test Framework のテスト発見、非同期実行、監視、キャンセル、NUnit XML ダウンロード:

`GET /api/tests` · `POST /api/test-runs` · `GET /api/test-runs/{id}` · `DELETE /api/test-runs/{id}` · `GET /api/test-runs/{id}/results.xml`

### [Profiling](api/profiling.ja.md)

ProfilerRecorder metric、NDJSON sample、Profiler raw capture、Memory Profiler snapshot、Test Runner連携:

`GET /api/profiling/metrics` · `POST|GET /api/profiling/sessions` · `GET|DELETE /api/profiling/sessions/{id}` · `POST .../{id}/stop` · `GET .../{id}/samples.ndjson` · `GET .../{id}/profile.raw` · `POST|GET /api/memory-snapshots` · `GET|DELETE /api/memory-snapshots/{id}` · `GET .../{id}/snapshot`

### [Editor アクティビティ](api/activities.ja.md)

「Editor が X で処理中である」ことを表す共通語彙。アクティビティの一覧、`409` がそれをどう報告するか、`blockedDuring` の意味。

### [Build](api/build.ja.md)

ビルド構成、プラットフォームモジュールの導入状況、設定の永続的変更、ビルドターゲットの切り替え、プレイヤービルド:

`GET|PATCH /api/build/settings` · `GET /api/build/targets` · `POST /api/build/scenes` · `POST|GET /api/build/target` · `GET /api/build/target/{id}` · `POST /api/builds` · `GET /api/builds` · `GET /api/builds/{id}` · `DELETE /api/builds/{id}`

---

## 関連ドキュメント

- [はじめに](index.ja.md) — セットアップ、EditorWindow ガイド、ライフサイクル
- [カスタムコントローラ](custom-controllers.ja.md) — `/api/custom/...` 配下へのアプリケーション側エンドポイントの追加
