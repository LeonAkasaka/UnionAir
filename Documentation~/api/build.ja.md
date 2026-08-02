# API リファレンス — Build

[English](build.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](build.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`(デフォルトポート: **8765**)。レスポンスの規約とカテゴリ/セキュリティの注意事項は [API リファレンス索引](../api-reference.ja.md) を参照してください。

これらのエンドポイントは、プロジェクトがどのようにビルドされるよう構成されているかを報告します。どのターゲットがアクティブか、どのシーンが有効でそれぞれどのビルドインデックスを得るか、どのスクリプティングバックエンドと定義シンボルが適用されるか、そして Editor が実際にどのプラットフォームモジュールをインストールしているかです。

クライアントはコンパイル結果を解釈するためにこれを必要とします。Editor でコンパイルが通る変更でも、プレイヤーターゲットではスクリプティングバックエンド・ストリッピングレベル・定義シンボルが異なるために失敗しうるからです。しかもそのいずれもプロジェクトディレクトリからは読み取れません。`ProjectSettings/ProjectSettings.asset` はスクリプティング設定をプラットフォーム id をキーとする内部レイアウトで保持し、アクティブなビルドターゲットはユーザー単位の Editor 状態であり、どのモジュールがインストールされているかは *Editor* の性質であってプロジェクトの性質ではないため、プロジェクト内のどのファイルにも記録されていません。

> これらのエンドポイントは **Build** カテゴリに属し、**既定で無効**です。カテゴリのリスクは `executableOutput` です。これは同じカテゴリが後にプレイヤービルドも生成するためです。本ページの 2 つのエンドポイント自体は読み取り専用で、`GET /api/help` では `risk: ["readOnly"]` として報告されます。

どちらのエンドポイントも Play モード中およびテスト実行中に呼び出せます。構成を読み取るだけでどちらにも干渉せず、失敗した実行を診断しているクライアントこそがこれらを必要とする場面だからです。

---

## GET /api/build/settings

ビルド構成を返します。

| クエリ | 既定 | 説明 |
|-------|---------|-------------|
| `namedBuildTarget` | アクティブ | スクリプティング設定を報告する named build target。例: `Standalone`、`Android`、`WebGL`、`Server`。大文字小文字は区別しません |

`namedBuildTarget` に依存するのは `scripting` オブジェクトのみです。それ以外は Editor の現在の状態を表し、この指定では変化しません。

### レスポンス

```json
{
  "activeBuildTarget": "StandaloneWindows64",
  "activeBuildTargetGroup": "Standalone",
  "activeNamedBuildTarget": "Standalone",
  "selectedBuildTargetGroup": "Standalone",
  "standaloneBuildSubtarget": "Player",
  "activeBuildTargetInstalled": true,
  "scenes": [
    {
      "path": "Assets/Scenes/SampleScene.unity",
      "guid": "99c9720ab356a0642a771bea13969a05",
      "enabled": true,
      "buildIndex": 0
    }
  ],
  "sceneCount": 1,
  "enabledSceneCount": 1,
  "scripting": {
    "namedBuildTarget": "Standalone",
    "scriptingBackend": "Mono2x",
    "apiCompatibilityLevel": "NET_Standard_2_0",
    "il2CppCompilerConfiguration": "Release",
    "managedStrippingLevel": "Disabled",
    "defineSymbolsRaw": "",
    "defineSymbols": []
  },
  "options": {
    "development": false,
    "allowDebugging": false,
    "connectProfiler": false,
    "buildWithDeepProfilingSupport": false,
    "waitForManagedDebugger": false
  },
  "player": {
    "productName": "TestUnity6",
    "companyName": "DefaultCompany",
    "bundleVersion": "0.1.0",
    "unityVersion": "6000.0.80f1"
  }
}
```

| フィールド | 型 | 説明 |
|-------|------|-------------|
| `activeBuildTarget` | string | Editor が現在ビルド対象としている `BuildTarget` |
| `activeBuildTargetGroup` | string | アクティブターゲットの `BuildTargetGroup` |
| `activeNamedBuildTarget` | string | アクティブな構成が解決される named build target |
| `selectedBuildTargetGroup` | string | Build Settings ウィンドウで選択中のグループ。切り替え保留中はアクティブなものと異なることがあります |
| `standaloneBuildSubtarget` | string | Standalone ターゲットにおける `Player` または `Server` |
| `activeBuildTargetInstalled` | bool | アクティブターゲットのプラットフォームモジュールが導入済みか。`false` の場合、ビルド要求は失敗します |
| `scenes` | array | `EditorBuildSettings.scenes` をリスト順で。無効なエントリも含みます |
| `sceneCount` / `enabledSceneCount` | number | 総エントリ数と、実際に同梱されるエントリ数 |
| `scripting` | object | 要求された named build target のスクリプティング設定 |
| `options` | object | `EditorUserBuildSettings` のビルドフラグ |
| `player` | object | `PlayerSettings` の製品情報と Editor バージョン |

### シーンのフィールド

| フィールド | 型 | 説明 |
|-------|------|-------------|
| `path` | string | プロジェクト相対のシーンアセットパス |
| `guid` | string | シーンアセットの GUID |
| `enabled` | bool | Build Settings でチェックされているか |
| `buildIndex` | number \| null | 実行時にそのシーンが持つインデックス。エントリが無効な場合は `null` |

`buildIndex` は**有効なエントリのみ**を数えます。無効なエントリはリスト上の行を占めますがビルドインデックスを持たないため、`scenes` 内での位置は `SceneManager.LoadScene(int)` が読み込むものと一致しません。リスト位置ではなく `null` を返すことがこのフィールドの要点です。

### スクリプティングのフィールド

| フィールド | 型 | 説明 |
|-------|------|-------------|
| `namedBuildTarget` | string | これらの値を読み取った named build target |
| `scriptingBackend` | string | `Mono2x` または `IL2CPP` |
| `apiCompatibilityLevel` | string | `NET_Standard_2_0` などの .NET プロファイル |
| `il2CppCompilerConfiguration` | string | `Debug`、`Release`、`Master` |
| `managedStrippingLevel` | string | `Disabled`、`Low`、`Medium`、`High`。そう命名する Editor では `Minimal` として報告されます |
| `defineSymbolsRaw` | string | Unity が保持する定義シンボル文字列そのまま |
| `defineSymbols` | array | 同じシンボルを分割して trim したもの |

両方の形式を返すのは、Unity が Inspector に与えられた文字列をそのまま保持するためです。空要素や余分な空白は実プロジェクトでも発生します。`defineSymbols` はそれらを取り除き、`defineSymbolsRaw` は書き込み時に保全すべき元の値です。

これらはプロジェクト**自身**の定義シンボルです。Unity 組み込みの `UNITY_*` シンボルは含まれません。プロジェクト設定に保存されておらず、変更もできないためです。

要求されたターゲットについてこの Editor が報告できない値は、レスポンス全体を失敗させる代わりに空文字列として返されます。これはプラットフォームモジュールが未導入の場合に起こり、まさにクライアントがビルドを要求する前に問い合わせる状況なので、エラーよりも残りの回答のほうが価値があります。

### ステータスコード

`namedBuildTarget` がこの Editor の定義する名前でない場合は `400`。メッセージにはこの Editor で有効な名前が列挙されます:

```json
{
  "error": "Unknown namedBuildTarget 'Bogus'. Known values for this Editor: Android, EmbeddedLinux, LinuxHeadlessSimulation, Nintendo Switch, PS4, PS5, QNX, Server, Standalone, VisionOS, WebGL, Windows Store Apps, XboxOne, iPhone, tvOS."
}
```

この集合は Editor のバージョンと導入済みモジュールによって異なるため、固定リストを文書化するのではなくエンドポイントが報告します。空白を含む名前は URL エンコードが必要です。

```bash
curl http://localhost:8765/api/build/settings
curl "http://localhost:8765/api/build/settings?namedBuildTarget=Android"
```

---

## GET /api/build/targets

この Unity インストールが定義するビルドターゲットと、それぞれのプラットフォームモジュールが導入済みかどうかを一覧します。

| クエリ | 既定 | 説明 |
|-------|---------|-------------|
| `installed` | `false` | `true` でモジュール導入済みのターゲットのみを一覧 |

`total` と `installedCount` は常にカタログ全体を表すため、フィルタ後のレスポンスでもどれだけ除外されたかが分かります。

### レスポンス

```json
{
  "activeBuildTarget": "StandaloneWindows64",
  "total": 21,
  "installedCount": 2,
  "installedOnly": false,
  "targets": [
    {
      "buildTarget": "Android",
      "buildTargetGroup": "Android",
      "namedBuildTarget": "Android",
      "installed": false,
      "isActive": false
    },
    {
      "buildTarget": "StandaloneWindows64",
      "buildTargetGroup": "Standalone",
      "namedBuildTarget": "Standalone",
      "installed": true,
      "isActive": true
    }
  ]
}
```

| フィールド | 型 | 説明 |
|-------|------|-------------|
| `buildTarget` | string | `BuildTarget` 列挙子名 |
| `buildTargetGroup` | string | 所属する `BuildTargetGroup` |
| `namedBuildTarget` | string | `GET /api/build/settings?namedBuildTarget=` が受け付ける名前 |
| `installed` | bool | ビルドに必要なモジュールを Editor が持っているか |
| `isActive` | bool | アクティブなビルドターゲットか |

カタログはハードコードせず、リクエスト時に Editor から読み取ります。そのため Unity が後のバージョンで追加したターゲットは UnionAir を変更しなくても現れ、廃止されたターゲットは消えます。廃止済みターゲットは省略されます。Unity は列挙子に非推奨メンバーとして残しますが、ビルドできない名前を提示するのは一覧しないことより悪いためです。

`StandaloneWindows64`、`StandaloneWindows`、`StandaloneLinux64`、`StandaloneOSX` はいずれも `Standalone` であるように、複数のターゲットが 1 つのグループと 1 つの named build target を共有します。したがってスクリプティング設定は共有されますが、モジュールの導入状況は共有されません。

`Server` は `GET /api/build/settings` では named build target として現れますが、ここには行がありません。独立したビルドターゲットではなく Standalone のサブターゲットだからです。settings レスポンスの `standaloneBuildSubtarget` がそれを選択します。

```bash
curl http://localhost:8765/api/build/targets
curl "http://localhost:8765/api/build/targets?installed=true"
```

---

## POST /api/builds

**アクティブな**ビルドターゲットに対してプレイヤービルドを要求し、ポーリング対象の id とともに `202` を返します。

> Build カテゴリが有効な場合のみ呼び出せます。エンドポイントのリスクは `executableOutput` です。

### ビルド中は API が応答しません

ビルドは実行中ずっと Unity のメインスレッドを占有し、UnionAir は同じスレッドの `EditorApplication.update` から HTTP リクエストをディスパッチします。**ビルドが終わるまでいかなるリクエストも処理されません** — `GET /api/builds/{id}` すら例外ではありません。開発時の計測では Windows プレイヤービルドで約 72 秒、キャッシュが温まっている場合で 22〜34 秒でした。

クライアントの timeout はそれに合わせて設定し、ビルド中の接続拒否やハングは失敗ではなく想定された挙動として扱ってください。レコードを永続化して `202` をビルド開始**前**に送るのはこのためです。後から書き込んだレスポンスは、呼び出し側が既に諦めた接続に届くことになります。

同じ理由から、**進捗の逐次報告とキャンセルは提供しません**。どちらもインプロセスでは実現不可能です。メインスレッドが `BuildPipeline.BuildPlayer` の中にある間はいかなるコールバックも動作せず、Unity はプレイヤービルドのキャンセル API 自体を公開していません。これらを提供しうるアウトオブプロセスのビルドサービスはスコープ外です。

### リクエスト

```json
{
  "requestId": "nightly-1",
  "development": true,
  "allowDebugging": true
}
```

| フィールド | 必須 | 既定 | 説明 |
|-------|----------|---------|-------------|
| `requestId` | いいえ | 自動生成 | 呼び出し側が指定する id。文字種の規則は `POST /api/compile` と同じ |
| `development` | いいえ | プロジェクト設定 | Development build |
| `allowDebugging` | いいえ | プロジェクト設定 | Script debugging |
| `connectProfiler` | いいえ | プロジェクト設定 | Autoconnect Profiler |
| `deepProfiling` | いいえ | プロジェクト設定 | Deep profiling support |
| `waitForPlayerConnection` | いいえ | `false` | 起動時にプレイヤー接続を待機 |
| `clean` | いいえ | `false` | 先にビルドキャッシュを消去 |
| `strictMode` | いいえ | `false` | エラーが 1 つでもあればビルドを失敗させる |

**受け付けるオプションはこれらのみです。** 出力先はリクエストから受け取りません。ビルドターゲットも同様です。ターゲットの切り替えはビルドのパラメータではなく、それ自体が独立したライフサイクル操作です。

省略されたオプションは、プロジェクトの Build Settings ウィンドウで現在選択されている値にフォールバックします。したがって空のボディで要求したビルドは、人が Build を押して得られるビルドと同じです。上書きはそのビルド 1 回にのみ適用され、プロジェクトには**書き戻されません**。`clean` と `strictMode` はプロジェクトから継承しません。どちらも永続化されたプロジェクト設定ではないためです。

`allowDebugging` / `connectProfiler` / `deepProfiling` / `waitForPlayerConnection` は `development: true` を必要とし、そうでなければ `400` を返します。Unity 自身の Build Settings ウィンドウも development build なしではこれらを無効化しており、`BuildPipeline` は黙って落とすため、要求されたものとは静かに異なるビルドが生成されてしまうからです。

`requestId` を指定するとリクエストが回復可能になります。ビルド中は接続が落ちるため `202` の取りこぼしは稀な事態ではなく現実的な結果です。2 回目のリクエストを送るのではなく `GET /api/builds/{requestId}` をポーリングしてください。

### レスポンス — 202

```json
{
  "id": "b-20260802-101530-3f9ac1",
  "state": "queued",
  "buildTarget": "StandaloneWindows64",
  "sessionId": "f40cbf3fc3224a97b5b7ac7aa3b1ea38",
  "lifecycleGenerationAtRequest": 9,
  "statusUrl": "/api/builds/b-20260802-101530-3f9ac1",
  "note": "The build occupies the Unity main thread. UnionAir answers no request, including this status URL, until it finishes..."
}
```

### ステータスコード

読み込み済みシーンに**未保存の変更**がある場合、`code: "loaded_scene_unsaved_blocked"` を伴う `409`:

```json
{
  "error": "Cannot build while loaded scenes have unsaved changes. BuildPipeline.BuildPlayer reads scenes from disk, so the build would not contain them. Save the reported scenes explicitly before retrying.",
  "code": "loaded_scene_unsaved_blocked",
  "loadedScenes": [
    { "path": "Assets/Scenes/SampleScene.unity", "name": "SampleScene", "isDirty": true, "isActive": true, "reason": "unsaved" }
  ]
}
```

`BuildPipeline.BuildPlayer` はシーンを保存済みアセットから読み取り、Build Settings ウィンドウと異なりスクリプトから呼ばれた場合は確認ダイアログを出しません。API 経由で編集され保存されていないシーンは黙って除外され、ビルドは **Editor の表示と一致しない内容に対して成功を報告**します。これは自動化クライアントにとって最悪の失敗形態です。シーンが暗黙に保存されることはありません。ビルド要求の副作用として他人の未保存の作業をディスクへ書き込むことは、拒否されるよりも大きな驚きだからです。一度も保存されておらず保存先パスを持たないシーンは `reason` が `unsavedNewScene` になります。

これは[読み込み済みシーンの外部変更ガード](editor.ja.md#loaded-scene-conflict--409)とは別の判定です。あちらは読み込み済みシーンをディスク上のファイルと比較します。両方が同時に成立することもあり、一方が他方を含意しません。

保持期間内に `requestId` が既に使用されていた場合、`existingBuild` オブジェクトを伴う `409`。ボディには既存レコード全体が含まれます。

ビルドが既にキュー済みまたは実行中の場合、`activeBuild` オブジェクトを伴う `409`。

アクティブなビルドターゲットのプラットフォームモジュールが未導入の場合、ターゲット名と `GET /api/build/targets` を示す `409`。

コンパイル・アセットインポート・ビルドターゲット切り替えが実行中の場合、`activeActivity` を伴う `409`。[Editor アクティビティ](activities.ja.md) を参照してください。

Build Settings に有効なシーンが 1 つもない場合、`requestId` が不正な場合、`development` なしでデバッグ用オプションが要求された場合は `400`。

```bash
curl -X POST http://localhost:8765/api/builds \
  -H "Content-Type: application/json" \
  -d '{"requestId":"nightly-1"}'
```

---

## GET /api/builds

実行中のビルドを `current` として、保持されているレコードの要約と、アーティファクトが占有しているディスク量を返します。

```json
{
  "current": null,
  "total": 1,
  "storage": {
    "root": "Builds/UnionAir",
    "totalBytes": 140839956,
    "artifactCount": 1,
    "maxArtifactCount": 3,
    "maxTotalBytes": 2147483648,
    "retainedRecords": 20
  },
  "records": [
    {
      "id": "b-20260802-101530-3f9ac1",
      "state": "completed",
      "result": "succeeded",
      "buildTarget": "StandaloneWindows64",
      "requestedAt": "2026-08-02T09:41:12.0200139Z",
      "finishedAt": "2026-08-02T09:41:34.2200139Z",
      "durationSeconds": 22.2,
      "outputDirectory": "Builds/UnionAir/b-20260802-101530-3f9ac1",
      "outputBytes": 140838619,
      "outputAvailable": true,
      "compileId": "c-20260802-094112-9d15d8",
      "error": null,
      "statusUrl": "/api/builds/b-20260802-101530-3f9ac1"
    }
  ]
}
```

`storage` が存在するのは、アーティファクトが設計上 git から不可視であり(下記参照)、したがってディスクが埋まっていることに人が気づく通常の手段からも不可視だからです。発見可能性は代わりに API が提供します。レコードが出力パスとバイト数を持ち、このエンドポイントが合計を報告し、`DELETE` が回収し、リテンションが自動的に整理します。

---

## GET /api/builds/{id}

保持されているビルドレコードを 1 件返します。スナップショット化されたビルドレポートを含みます。

```json
{
  "id": "b-20260802-101530-3f9ac1",
  "state": "completed",
  "result": "succeeded",
  "buildTarget": "StandaloneWindows64",
  "options": { "development": true, "allowDebugging": true, "connectProfiler": false, "deepProfiling": false, "waitForPlayerConnection": false, "clean": false, "strictMode": false },
  "scenes": ["Assets/Scenes/SampleScene.unity"],
  "compileId": "c-20260802-094112-9d15d8",
  "outputDirectory": "Builds/UnionAir/b-20260802-101530-3f9ac1",
  "outputPath": "Builds/UnionAir/b-20260802-101530-3f9ac1/TestUnity6.exe",
  "outputBytes": 140838619,
  "outputAvailable": true,
  "reportPath": "Builds/UnionAir/b-20260802-101530-3f9ac1/report.json",
  "report": {
    "result": "succeeded",
    "platform": "StandaloneWindows64",
    "totalTimeSeconds": 21.99,
    "totalSizeBytes": 140838619,
    "totalErrors": 0,
    "totalWarnings": 0,
    "messages": [],
    "messagesTruncated": false
  },
  "error": null,
  "statusUrl": "/api/builds/b-20260802-101530-3f9ac1"
}
```

### state と result

| `state` | `result` | 意味 |
|---------|----------|---------|
| `queued` | ― | ビルドは受理されたがまだ開始していない |
| `running` | ― | ビルド実行中。この間、他のリクエストは処理されない |
| `completed` | `succeeded` | Unity がビルド成功を報告 |
| `failed` | `failed` | Unity がビルド失敗を報告。`report.messages` を参照 |
| `failed` | `cancelled` | Unity 内部でビルドがキャンセルされた |
| `aborted` | `aborted` / `notStarted` | 結果報告前に Editor がリロード・終了・クラッシュした |

`report` はライブオブジェクトではなく**スナップショット**です。`BuildReport` はネイティブ状態に裏打ちされた Unity オブジェクトで domain reload により失われるため、`BuildPipeline.BuildPlayer` が返った直後にプレーンなフィールドへコピーされます。これによりレコードは保持されている限り読み取り可能なままです。`messages` はエラーと警告のみを保持します。成功したビルドは情報レベルのエントリを数千件報告し、レコードはディスクに書かれポーリングのたびに全体が返されるためです。上限は 200 件で、打ち切りは `messagesTruncated` が報告します。

`compileId` は**このビルドが実行したプレイヤーコンパイル**のコンパイルレコードを指します。そのサイクルは無関係な external コンパイルとして引き受けられるのではなく `source: "build"` として記録されます。[ビルドが所有するコンパイル](compile.ja.md)を参照してください。

`report.startedAt` と `report.endedAt` は Unity 由来の値を UTC に正規化したものです。`report.outputPath` は Unity が報告した絶対パス、レコードの `outputPath` はプロジェクト相対パスです。

`outputAvailable` はレコード読み取り時に算出されます。リテンションはレコードよりずっと早く出力を削除するためです。`outputAvailable: false` のレコードも、そのビルドが何を生成したかは報告します。

---

## DELETE /api/builds/{id}

ビルドレコードとそのアーティファクトディレクトリを削除します。

```json
{
  "deleted": "b-20260802-101530-3f9ac1",
  "reclaimedBytes": 99727454,
  "outputAvailable": false,
  "totalBytes": 140839956
}
```

保持されていない id には `404` を返します。レスポンスの `outputAvailable: true` は、ディレクトリを完全に削除できなかったことを意味します。通常は中のファイルが開かれている場合です。

---

## アーティファクトの保存とリテンション

プレイヤー出力とその `report.json` は、プロジェクトルート直下の `Builds/UnionAir/{id}/` に一緒に置かれます。

コンパイルレコード・プロファイリング成果物・メモリスナップショットが `Library/UnionAir/` にあるにもかかわらず、`Library/` は意図的に**使いません**。Unity は任意のタイミングで `Library/` を再生成するため、数百 MB のアーティファクトが黙って破棄されるか、それを指すレコードから切り離されるかのどちらかになります。ビルド出力は Editor 内部の診断情報ではなくユーザー向けの成果物であり、人が探す場所にあるべきです。

**git からの除外は決定的にします。** ディレクトリ作成時に `*` を含む `.gitignore` を書き込みます。完了時ではなく作成時なのは、途中で失敗したビルドもコミット可能な出力を残さないためです。UnionAir は利用側プロジェクトの `.gitignore` に依存できません。Unity 標準テンプレートは `/[Bb]uilds/` を除外しますが、すべてのプロジェクトがそれを使うわけではないため、git 上の可視性はプロジェクトごとに異なり、挙動として文書化できなくなります。また計測された出力サイズは GitHub の 100 MiB ファイル上限に十分近く、誤コミットは煩わしさではなく重大な事故になります。

| 上限 | 値 |
|-----|-------|
| 保持するアーティファクトディレクトリ数 | 3 |
| アーティファクト合計サイズ | 2 GiB |
| 保持するレコード数 | 20 |

いずれかの上限を超えると、直前に完了したビルドを保護しつつ古いものから削除します。上限は 5 GB のプロファイリングクォータとは独立に設定しています。同じ値ではビルドを約 50 件保持してしまうためです。

レコードは小さく、アーティファクトより長く残ります。古いビルドについて問い合わせたクライアントは `404` ではなく、そのビルドが何を生成したかを知ることができます。

レコードはアーティファクトディレクトリ内に `report.json` としても書き出されます。API を介さずにそのディレクトリを見つけた人にも、内容が説明されるようにするためです。

### ビルド後も読み込み済みシーンは追跡されたまま

`BuildPipeline.BuildPlayer` はビルド対象シーンを自ら開き、読み込み済みシーンに対して `sceneClosed` を発火させますが、対応する `sceneOpened` は発火しません。その結果、[外部変更ガード](editor.ja.md#loaded-scene-conflict--409)が保持するディスク基準値が失われます。放置すると、以後の `POST /api/editor/refresh` や `POST /api/compile` はそのシーンを `untracked` として報告し続け、人が手動で保存または再オープンするまで解消しません。ビルド→コンパイルのループが理由もなく壊れてしまいます。

UnionAir はビルド前に基準値を退避し、ビルド後に復元します。ただし**ファイルが 1 バイトも変わっていないシーンに限ります**。ビルド中にディスク上で変更されたシーンはその比較に失敗し、untracked のまま残り、引き続きガードに引っかかります。

---

## 関連ドキュメント

- [Compile API](compile.ja.md) — これらの設定が説明するコンパイル結果
- [API リファレンス索引](../api-reference.ja.md)
