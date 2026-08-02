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

## 関連ドキュメント

- [Compile API](compile.ja.md) — これらの設定が説明するコンパイル結果
- [API リファレンス索引](../api-reference.ja.md)
