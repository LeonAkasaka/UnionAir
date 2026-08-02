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

## PATCH /api/build/settings

1 つの named build target について、スクリプティング設定とビルドフラグを変更します。

> Build カテゴリが有効な場合のみ呼び出せます。Play モード、テスト実行、コンパイル、アセットインポート、ビルド、ビルドターゲット切り替えの実行中は拒否されます。

### これらの変更は永続的であり、その永続性は一様ではありません

Git の差分から気づかせるのではなく、API が明示します。変更ごとに `persistence` を報告します:

| `persistence` | ファイル | 影響範囲 |
|---------------|------|----------------|
| `project` | `ProjectSettings/ProjectSettings.asset` | プロジェクトに関わる全員。Git 差分として現れます |
| `project` | `ProjectSettings/EditorBuildSettings.asset` | 同上。ビルドシーン一覧 |
| `user` | `Library/EditorUserBuildSettings.asset` | このマシンのこのユーザーのみ。共有もコミットもされません |

スクリプティング設定とシーン一覧はプロジェクト全体に及びます。`development` などのビルドフラグはユーザー単位です。この 2 つを同じ「設定」として提示することこそが誤解を招く部分なので、レスポンスは変更ごとに区別します。

### リクエスト

```json
{
  "namedBuildTarget": "Standalone",
  "addDefineSymbols": ["UNIONAIR_SAMPLE"],
  "development": true
}
```

| フィールド | 永続性 | 説明 |
|-------|-------------|-------------|
| `namedBuildTarget` | ― | スクリプティング設定の適用対象。既定はアクティブなターゲット |
| `scriptingBackend` | project | `Mono2x` または `IL2CPP` |
| `apiCompatibilityLevel` | project | .NET プロファイル |
| `managedStrippingLevel` | project | ストリッピングレベル |
| `il2CppCompilerConfiguration` | project | IL2CPP コンパイラ構成 |
| `defineSymbols` | project | 一覧全体を置き換え |
| `addDefineSymbols` | project | 既存を保ったままシンボルを追加 |
| `removeDefineSymbols` | project | 既存を保ったままシンボルを削除 |
| `development` | user | Development build |
| `allowDebugging` | user | Script debugging |
| `connectProfiler` | user | Autoconnect Profiler |
| `buildWithDeepProfilingSupport` | user | Deep profiling support |
| `waitForManagedDebugger` | user | マネージドデバッガの待機 |

少なくとも 1 つの設定が必要です。何も変更しないリクエストは、受け付けるフィールドを列挙した `400` を返します。

`defineSymbols` は `addDefineSymbols` / `removeDefineSymbols` と併用できません。両者は同じ一覧に対する異なる意図を表しており、両方を適用すると結果がリクエストの記述にない順序に依存してしまうためです。

各定義シンボルは英字またはアンダースコアで始まり、英数字とアンダースコアのみを含む必要があります。Unity は与えられた値をそのまま保存し、失敗するのは後のコンパイル時であり、そのエラーは設定について一切言及しません。したがって不正なシンボルは、メッセージがそれを名指しできるこの時点で拒否します。保存される文字列はセミコロン区切りに書き直され、カンマや空白が混じって蓄積された一覧が正規化されます。

列挙値の照合は大文字小文字を区別しません。未知の値は、**この Editor で有効な名前を列挙した** `400` を返します。有効な集合はバージョンごとに異なるためです。

### 書き込み前にすべて検証されます

すべての値が先に検証されます。1 つでも不正な列挙値を含むリクエストは、何一つ変更しません。説明を要しない唯一の部分的失敗の形だからです。

型も検証対象です。フィールドが存在するのに想定した種類の値でない場合(`true` ではなく `"development": "true"` など)、そのフィールド名を示す `400` を返します。無視すると後述の応答規則が壊れます。呼び出し側は値を設定したのに、それに対する結果を一切受け取れないからです。

### 部分的失敗は報告され、ロールバックはされません

検証を通過した後でも、Unity が拒否すれば変更は失敗しえます。その場合、先に適用された変更は元に戻され**ません**。元に戻す操作自体も失敗しうるため、要求された状態でも元の状態でもない第 3 の状態が残る可能性があるからです。代わりに:

- 変更ごとに `outcome`(`applied` / `unchanged` / `failed`)を報告します。
- いずれかが失敗した場合はステータス `207`、それ以外は `200` です。
- `settings` に**結果としての**状態を含めるため、呼び出し側は推測ではなく事実を読み取れます。

all-or-nothing が必要な場合は、1 リクエストにつき 1 変更としてください。

`unchanged` は黙って省略せず報告します。値を設定したのに何の応答も得られない呼び出し側は、no-op とフィールドの取りこぼしを区別できないためです。

### レスポンス

```json
{
  "changes": [
    {
      "setting": "defineSymbols",
      "outcome": "applied",
      "persistence": "project",
      "file": "ProjectSettings/ProjectSettings.asset",
      "previous": null,
      "value": "UNIONAIR_SAMPLE",
      "error": null
    },
    {
      "setting": "development",
      "outcome": "applied",
      "persistence": "user",
      "file": "Library/EditorUserBuildSettings.asset",
      "previous": "false",
      "value": "true",
      "error": null
    }
  ],
  "persistent": true,
  "compilationExpected": true,
  "lifecycleGeneration": 13,
  "note": "Changes are permanent. ...",
  "settings": { }
}
```

`settings` は `GET /api/build/settings` が返すものと同じオブジェクトで、変更適用後に読み取った値です。

### コンパイルと domain reload

スクリプティングバックエンド・API 互換性レベル・定義シンボルの変更はコンパイルと domain reload を引き起こします。このリクエストがそれを引き起こしたかどうかは `compilationExpected` が報告します。ビルドフラグとストリッピングレベルは引き起こしません。コンパイラがそれらを読まないためです。

レスポンスはリロードが始まる前に書き込まれます。Unity は再コンパイルをその場で実行するのではなく**キューに入れる**ため、リロードと競合せずに設定を適用し実際の結果を報告できます。このエンドポイントが `POST /api/compile` のように遅延実行せず同期的に適用するのはそのためです。

その後、当該サイクルは Compile API から観測できます。UnionAir ではなく Unity が開始したものであるため `source: "external"` として記録されます:

```
1. PATCH /api/build/settings   -> 200 { compilationExpected: true, lifecycleGeneration: 13 }
2. 接続断を許容しつつ GET /api/editor/status をポーリング
3. lifecycleGeneration > 13 かつ settled == true -> リロード完了
4. GET /api/compile            -> その変更が引き起こしたサイクル
```

```bash
curl -X PATCH http://localhost:8765/api/build/settings \
  -H "Content-Type: application/json" \
  -d '{"addDefineSymbols":["UNIONAIR_SAMPLE"]}'
```

---

## POST /api/build/scenes

ビルドシーン一覧を置き換えます。

この一覧はパッチではなく**置換**です。順序が有効な各シーンのビルドインデックスを決めるため、部分更新は所属だけでなく位置についても意図を表現する必要があり、その簡略記法はどう定めても推測になります。

### リクエスト

```json
{
  "scenes": [
    { "path": "Assets/Scenes/SampleScene.unity", "enabled": true },
    "Assets/Scenes/Menu.unity"
  ]
}
```

各要素はシーンパスの文字列(既定で有効)か、`path` と任意の `enabled` を持つオブジェクトです。`enabled` が存在するのに boolean でない場合は `400` を返します。`true` に落とすと、呼び出し側が除外を指示したシーンを同梱してしまうためです。`scenes` は必須で、空配列は一覧を消去します。これは正当な要求です。シーンが 1 つもないビルドは、理由を説明できる `POST /api/builds` の側で拒否されます。

すべてのパスは `.unity` で終わり、重複せず(Unity はシーンごとに 1 つのビルドインデックスを割り当てます)、**インポート済み**のアセットを指す必要があります。AssetDatabase の確認は重要です。Unity は存在しないものを指すビルド設定エントリを受け付け、ビルド時に初めて失敗するからです。

### レスポンス

`PATCH /api/build/settings` と同じ形式で、`scenes` の変更が 1 件含まれます。`previous` と `value` は各パスを有効なら `+`、無効なら `-` 付きで列挙するため、差分が一目で読めます:

```json
{
  "changes": [
    {
      "setting": "scenes",
      "outcome": "applied",
      "persistence": "project",
      "file": "ProjectSettings/EditorBuildSettings.asset",
      "previous": "Assets/Scenes/SampleScene.unity+",
      "value": "Assets/Scenes/SampleScene.unity+;Assets/Scenes/Menu.unity+",
      "error": null
    }
  ],
  "persistent": true,
  "compilationExpected": false,
  "lifecycleGeneration": 13,
  "settings": { }
}
```

```bash
curl -X POST http://localhost:8765/api/build/scenes \
  -H "Content-Type: application/json" \
  -d '{"scenes":["Assets/Scenes/SampleScene.unity"]}'
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

Build Settings に有効なシーンが 1 つもない場合、`requestId` が不正な場合、`development` なしでデバッグ用オプションが要求された場合、オプションが存在するのに JSON boolean でない場合は `400`。型が違うオプションは無視せず拒否します。プロジェクト既定値へフォールバックすると、呼び出し側が要求していないビルドが、レスポンスに何の手がかりも残さずに生成されるためです。

ビルドレコードを書き込めなかった場合は `500` を返し、**ビルドは開始されません**。ビルド中は何も応答しないため、`202` の id が呼び出し側の唯一の手がかりです。結果を報告できないと分かっている 1 分間の処理を開始するより、断るほうが良いからです。リクエストを失敗させる前に、書き込みは 1 回だけ即時リトライされます。

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

キュー済みまたは実行中のビルドに対しては `activeBuild` オブジェクトを伴う `409` を返します。キュー済みビルドが待っているレコードを削除すると、遅延実行される開始処理は実行対象を失い、ビルドアクティビティを解放するものが無くなって、Editor セッションが終わるまで衝突するエンドポイントがブロックされ続けるためです。終端状態に達してから削除してください。

---

## POST /api/build/target

アクティブなビルドターゲットを切り替え、ポーリング対象の id とともに `202` を返します。

> Build カテゴリが有効な場合のみ呼び出せます。エンドポイントのリスクは `executableOutput` です。

### これは設定の書き込みではなくライフサイクル操作です

ターゲットの切り替えは新しいプラットフォーム向けに**すべてのアセット**を再インポートし、再コンパイルし、domain reload で終わります。大規模プロジェクトでは秒ではなく分の単位になり、その大半の間 UnionAir は応答しません。ビルドが引き起こすのと同種の停止が、より長く続きます。これが、`PATCH /api/build/settings` のフィールドではなく、永続レコードを伴う追跡対象アクティビティとして扱う理由です。

要点はレコードです。レコードは切り替えが引き起こす domain reload を越えて残るため、接続が切れたクライアントは戻ってきて結果を読み取れます。アクティブなターゲットから推測する必要はありません。Unity はリロードをまたいで結果を報告しないため、UnionAir はリロード後にアクティブなターゲットと要求されたターゲットを比較してレコードを確定させます。

### リクエスト

```json
{ "buildTarget": "StandaloneWindows64", "requestId": "ci-1" }
```

| フィールド | 必須 | 説明 |
|-------|----------|-------------|
| `buildTarget` | はい | [`GET /api/build/targets`](#get-apibuildtargets) が報告する `BuildTarget` 名 |
| `requestId` | いいえ | 呼び出し側が指定する id。文字種の規則は `POST /api/compile` と同じ |

### レスポンス — 202

```json
{
  "id": "t-20260802-103012-5ba71c",
  "state": "queued",
  "requestedTarget": "StandaloneWindows64",
  "previousTarget": "StandaloneWindows",
  "sessionId": "f40cbf3fc3224a97b5b7ac7aa3b1ea38",
  "lifecycleGenerationAtRequest": 16,
  "statusUrl": "/api/build/target/t-20260802-103012-5ba71c",
  "note": "Switching reimports every asset for the new platform..."
}
```

### ステータスコード

対象のモジュールが未導入の場合、`code: "platform_module_not_installed"` を伴う `409`:

```json
{
  "error": "The platform module for 'Android' is not installed in this Unity Editor. Install it through the Unity Hub for this Editor version, then retry.",
  "code": "platform_module_not_installed",
  "buildTarget": "Android",
  "installedTargets": ["StandaloneWindows", "StandaloneWindows64"]
}
```

これは汎用的な失敗ではなく独立した条件として報告します。解決方法(Unity Hub からモジュールを導入する)は、切り替え失敗のメッセージからは決して示唆されないものだからです。`installedTargets` は、この Editor が切り替え*可能*な対象を列挙します。

要求されたターゲットが既にアクティブな場合は `state: "unchanged"` を伴う `200`。再インポートは行われず、レコードも作成されません。

切り替えが既に実行中の場合は `activeSwitch` オブジェクトを伴う `409`、`requestId` の再送には `existingSwitch` を伴う `409`。

`buildTarget` の欠落・未知の値、`requestId` の不正には `400`。

コンパイル・アセットインポート・ビルドの実行中は `activeActivity` を伴う `409`。

```bash
curl -X POST http://localhost:8765/api/build/target \
  -H "Content-Type: application/json" \
  -d '{"buildTarget":"StandaloneWindows64"}'
```

---

## GET /api/build/target

アクティブなターゲット、実行中の切り替え(`current`)、保持されている切り替えレコードを返します。

```json
{
  "activeBuildTarget": "StandaloneWindows64",
  "activeBuildTargetGroup": "Standalone",
  "current": null,
  "total": 2,
  "records": [
    {
      "id": "t-20260802-103012-5ba71c",
      "source": "unionAir",
      "state": "completed",
      "requestedTarget": "StandaloneWindows64",
      "requestedTargetGroup": "Standalone",
      "requestedNamedBuildTarget": "Standalone",
      "previousTarget": "StandaloneWindows",
      "activeTarget": "StandaloneWindows64",
      "durationSeconds": 3.3,
      "lifecycleGenerationAtRequest": 18,
      "lifecycleGenerationAtFinish": 18,
      "error": null,
      "statusUrl": "/api/build/target/t-20260802-103012-5ba71c"
    }
  ]
}
```

`GET /api/build/target/{id}` は 1 件のレコードを返します。UnionAir は最新 20 件を保持します。

### state

| `state` | 意味 |
|---------|---------|
| `queued` | 受理済み。切り替えは未開始 |
| `switching` | 再インポートと再コンパイル中。Editor は応答しません |
| `completed` | アクティブなターゲットが要求どおりになった |
| `failed` | Unity が切り替えを拒否したか、実行せずにリロードした |
| `aborted` | 切り替え中に Editor が終了または再起動した |

`lifecycleGenerationAtRequest` と `lifecycleGenerationAtFinish` は、切り替えが domain reload をまたいだ場合に異なり、Unity がその場で完了した場合は一致します。どちらも起こります。ビルドターゲットグループ**内**の切り替え(たとえば `StandaloneWindows64` から `StandaloneWindows`)はリロードを必要としないことが多く、グループ間の切り替えは必要とします。

### 断絶をまたいだポーリング

```
1. POST /api/build/target        -> 202 { id, lifecycleGenerationAtRequest }
2. 接続拒否を想定内として GET /api/build/target/{id} をポーリング
3. state == "completed"          -> 完了。新しいターゲットがアクティブ
   state == "failed"             -> 'error' を読む。アクティブなターゲットは変わっていない
   state == "aborted"            -> Editor が停止した。GET /api/build/target を確認
4. 待機には明示的な timeout を設ける。大規模プロジェクトでは数分かかりうる
```

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
