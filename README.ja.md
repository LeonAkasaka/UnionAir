# UnionAir — Unity REST Bridge

[English](README.md) | **日本語**

> **⚠️ Experimental(実験的)**
> 本パッケージはベータ以前の実験的な試作品です。後方互換性・バージョンの安定性・動作は**一切保証されません**。すべての API は予告なく変更・削除される可能性があります。

> **注記**: 本ドキュメントは [英語版 README](README.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

UnionAir は Unity Editor の状態をシンプルな **REST API**(HTTP)として公開し、LLM MCP ブリッジ・開発ボット・CI ツールなど、任意の HTTP クライアントから簡単に利用できるようにします。

## 設計思想

UnionAir は、クライアントがプロジェクトディレクトリに直接アクセスできることを前提とします。汎用的なファイルの読み書きはこのパッケージが提供すべきものではなく、提供しません。ここで公開するのは Editor 自身が定義する操作と、その操作が生み出す成果物です。

クライアントがファイルシステムから得られないのは、Editor 自身の振る舞いです。Unity プロジェクトの状態はディスク上のファイルだけではなく、インポート後に Editor が保持しているもの — GUID の解決、シリアライズされた参照、ロード済みのシーングラフ、アセットデータベース、スクリプトが属するドメイン — でもあります。`.unity` や `.asset` を手で編集することは、Editor の規則を外側から再現することにほかならず、正しさが失われるのはその再現においてです。

Editor を経由することがファイル編集より明確に優れている場合に、エンドポイントはこのパッケージに置く価値を持ちます。

- **Unity の規則と検証を、再実装せずに利用できること。** `POST /api/assets/move` は `AssetDatabase.MoveAsset` に委譲し、アセットの GUID と、それを介して解決されるシリアライズ参照を保持します(ただし、プロジェクトが単なる文字列として保持しているパスは、どちらの手段でも更新されません)。`DELETE /api/assets/{guid}` はアセットと `.meta` を同時に削除し、対象がロード済みシーンであれば拒否します。注意深いファイル操作でも同じ結果には到達できますが、そのためにはクライアント自身が Unity の規則を抱え込み、毎回それを正しく守り続ける必要があります。
- **Editor の内側にしか存在しない状態。** Play Mode、選択、Console、コンパイル結果、プロファイラのサンプル、ロード済みシーン一覧。読むべきファイルが存在しません。
- **Unity 自身のセマンティクスで定義された操作。** Prefab の apply/revert、プロジェクト定義の ScriptableObject に対する対応済みの `SerializedObject` 書き込み、Texture2D ではなく Sprite サブアセットを解決しなければならないアニメーションカーブ。これらを YAML 編集として表現することは、Unity のシリアライザを再実装することを意味します。
- **ループを終了させるためのフィードバック。** コンパイル診断は、それ自身が引き起こすドメインリロードを越えて保持されるため、write-compile-fix サイクルを終了させられます。[コンパイルと修正のループ](Documentation~/api/compile.ja.md#コンパイルと修正のループ)を参照してください。
- **Unity が提供する範囲での Undo 統合。** シーン編集(GameObject とコンポーネントの作成・更新・削除)は `Undo` に登録されるため、Editor の前にいる人間が Ctrl+Z で取り消せます。Material、AnimationClip、インポーター設定、ScriptableObject などのアセット書き込みは対象外です。

これらのいずれかを満たすことは必要条件であって、十分条件ではありません。Unity Editor API が存在すること自体は、それを公開する理由になりません。エンドポイントはさらに、実際のクライアントの必要に応え、依存するに足る契約 — Unity のバージョンを越えて意味を保ち、API 全体と整合する契約 — を提供しなければなりません。ラップできるメソッドがそこにあった、というだけで追加されるべきではありません。

素朴なファイル操作で既に正しく扱える作業は、意図的に対象外とします。C# ソースの編集、`manifest.json` の読み取り、プロジェクトツリーの走査 — これらはクライアントが直接行うべきです。そのためのエンドポイントは、クライアントと、既にこなせる作業との間に HTTP を挟むだけになります。

## 動作要件

- Unity **2022.3** 以降
- Unity Test Framework **1.4.0** 以降(任意。Test Runner API を使う場合のみ必要)
- Input System `com.unity.inputsystem`(任意。Play Mode の入力アクションと入力リプレイを使う場合のみ必要)。あわせて Unity の **Active Input Handling** を *Input System Package* または *Both* に設定する必要があります。

### サポート対象バージョン

| Unity | サポート水準 |
| --- | --- |
| **6000.0 LTS 以降** | **全面対応。** 最優先ターゲットであり、全機能をこの環境で開発・検証しています。基準は 6000.0.80f1 です。 |
| **2022.3 LTS** | **対応。** ビルドと基本的な動作を検証しています(2022.3.62f2)。 |
| 2023.x | ベストエフォート。同じコード経路を通るため動作する見込みですが、変更ごとの検証は行っていません。 |

Unity 6 が最優先ターゲットです。2022.3 LTS は意図的に維持しており、同バージョンに対する不具合報告も受け付けます。

#### Test Runner と Unity Test Framework について

Test Runner API は独立したアセンブリとして提供され、`com.unity.test-framework` の
**1.4.0 以降**が存在する場合にのみコンパイルされます。UnionAir が依存している
結果保存 API とテスト実行キャンセル API が 1.4.0 で追加されたためです。

Unity 6000.0 は既定で十分新しいバージョンを同梱していますが、Unity 2022.3 と 2023.1 は
既定がそれぞれ 1.1.33 / 1.3.9 であり、**要件を満たしません**。これらのバージョンでは
Test Runner アセンブリ自体がスキップされるため **コンパイルエラーは発生しません**が、
`/api/tests` と `/api/test-runs` のエンドポイント、および Test Runner カテゴリは利用できません。

2022.3 / 2023.x で Test Runner API を使う場合は、プロジェクトの
`Packages/manifest.json` で対応バージョンを指定してください。

```json
"dependencies": {
  "com.unity.test-framework": "1.4.6"
}
```

UnionAir 自身はこの依存を宣言していません。Test Runner カテゴリは既定で無効であり、
パッケージの更新はプロジェクト側の判断に委ねるべきだと考えているためです。

## インストール

### Package Manager(Git URL)から

1. **Window > Package Manager** を開きます。
2. **+** をクリックし、**Install package from git URL...** を選択します。
3. 次の URL を入力します:

```
https://github.com/LeonAkasaka/UnionAir.git#v0.3.0
```

### manifest.json から

`Packages/manifest.json` に依存関係を追加します:

```json
{
  "dependencies": {
    "com.leonakasaka.unionair": "https://github.com/LeonAkasaka/UnionAir.git#v0.3.0"
  }
}
```

タグを固定してください。`main` は常にリリース可能な状態を保っていますがタグより先行しており、UPM の Git URL は固定 ref しか受け付けません(バージョン範囲の指定構文はありません)。

## セットアップ

1. Unity Editor のロード時に HTTP サーバが自動的に起動します。
2. **Window > UnionAir > REST Bridge** を開くと、状態の確認とポートの設定ができます。

## デフォルトポート

`8765` — EditorWindow から変更可能です。

## エンドポイント

| グループ | 範囲 | セキュリティ |
|-------|-------|----------|
| **Read** | シーン階層、ロード済みシーン、GameObject、アセット、カメラ、ログ、検索、コンパイル結果 | 常時有効 |
| **Scene Write** | シーンの作成/オープン/アンロード、GameObject・コンポーネントの作成/更新/削除 | 既定で無効 |
| **Asset Write** | プレハブ、マテリアル、アセットファイル、AssetDatabase リフレッシュ、コンパイル要求 | 既定で無効 |
| **Play Mode** | Play モードの開始/終了/一時停止/ステップ、Input System アクション、Canvas UI 操作 | 既定で無効 |
| **Editor Actions** | 選択、オブジェクトの ping、アセットのオープン、Unity Editor メニュー項目の実行 | 既定で無効 |
| **Test Runner** | EditMode / PlayMode テストの発見、実行、監視、キャンセル、結果ダウンロード | 既定で無効。Unity Test Framework 1.4.0 以降の導入時のみ利用可能 |
| **Profiling** | ProfilerRecorder metric、NDJSON sample、Profiler raw capture、memory snapshot | 既定で無効 |

> コンパイル結果は構造化されており、診断ごとに `severity`、`code`、プロジェクト相対の `file`、`line`、`column` を持ち、コンパイル成功時の domain reload をまたいで保持されます。IDE から開始されたコンパイルも記録されます。**[コンパイルと修正のループ](Documentation~/api/compile.ja.md#コンパイルと修正のループ)** を参照してください。
> Unity Console のログは domain reload をまたいで保持され、増分取得用の `since` カーソルに対応しています。
> Edit モードでのシーン編集は Undo に登録され、Unity Editor 上で取り消せます(Ctrl+Z)。アセット書き込みは対象外です。
> シーン上の GameObject と Component は読み取りレスポンスに `globalObjectId` を含み、書き込みリクエストでは型付きオブジェクト参照で指定できます。
> 書き込み API は `GET /api/help` で Play モードの安全性を宣言します。永続的なシーン/アセット変更は Play モード中はブロックされ、一部のシーンオブジェクト変更は Editor 設定と `allowWhilePlaying=true` の両方が必要です。
> エンドポイントの全一覧とリクエスト/レスポンスの詳細は **[API リファレンス](Documentation~/api-reference.ja.md)** を参照してください。

## セキュリティ

書き込み系カテゴリを有効化する前に必ずお読みください:

- サーバは **`localhost` のみ**にバインドされ、ネットワーク上の他のマシンからは到達できません。
- **認証はありません。** 同じマシン上で動作する任意のプロセスが、有効化されているすべてのエンドポイントを呼び出せます。
- `Origin` ヘッダーを持つリクエストはルーティング前に拒否され、レスポンスは CORS を許可しません。そのためブラウザの `fetch` と XMLHttpRequest は既定で非対応です。`Origin` を送信しないローカル CLI や連携クライアントは引き続き利用できます。
- 空でないボディを持つリクエストには `Content-Type: application/json` が必要です。空の POST は Content-Type なしでも引き続き有効です。
- 既定で有効なのは **Read** カテゴリのみです。Scene Write / Asset Write / Play Mode / Editor Actions / Test Runner / Profiling はオプトインであり、有効化するとプロジェクトの任意のテストコード、heap snapshot、Unity Editor のメニュー実行、アセット削除を含む操作や診断成果物が、任意のローカルプロセスに公開されます。すべてのローカルクライアントを信頼できる場合にのみ有効化してください。
- カテゴリの有効/無効は**プロジェクト単位ではありません**。設定は `EditorPrefs` に保存され、Unity はこれをユーザーアカウントと Editor バージョン単位で管理します。あるプロジェクトで有効化したカテゴリは、同じ Editor で開く他のすべてのプロジェクトでも有効なままになります。

## クイックサンプル

```bash
# ヘルスチェック
curl http://localhost:8765/api/health

# シーン階層
curl http://localhost:8765/api/scene/hierarchy

# ロード済みシーン
curl http://localhost:8765/api/scenes

# 特定の GameObject
curl --get "http://localhost:8765/api/gameobjects" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}'

# Texture2D 型のアセット一覧
curl "http://localhost:8765/api/assets?type=Texture2D"

# 空の GameObject を作成(Scene Write カテゴリの有効化が必要)
curl -X POST http://localhost:8765/api/gameobjects \
  -H "Content-Type: application/json" \
  -d '{"name":"MyObject","parent":{"type":"hierarchyPath","value":"Canvas"}}'
```

## MCP ブリッジ

LLM MCP クライアントから利用するには、これらの REST エンドポイントを呼び出す Node.js 製 MCP ブリッジを別途実行してください。Unity パッケージ自体は MCP に依存しません。

## ドキュメント

- **[はじめに](Documentation~/index.ja.md)** — セットアップ、EditorWindow ガイド、ライフサイクル
- **[API リファレンス](Documentation~/api-reference.ja.md)** — 全エンドポイントのリクエスト/レスポンス例付きリファレンス
- **[カスタムコントローラ](Documentation~/custom-controllers.ja.md)** — アプリケーション側 UnionAir API の拡張ガイド

## 既知の制限

- 自動テストは `Tests/Editor` の EditMode テストのみで、Editor に依存しないロジックだけを対象としています。コンパイル、ドメインリロード、Play モード、HTTP サーバは手動で確認しており、CI は未整備です。実行方法は [テスト](CONTRIBUTING.ja.md#テスト) を参照してください。
- リクエストボディの JSON パースは軽量な独自リーダーであり、深くネストした JSON や特殊なケースにエッジケースがあります。
- レスポンスの JSON シリアライズはエンドポイントごとの手書き実装であり、共有シリアライザへのリファクタリングを予定しています。
- ブラウザ由来の `fetch` と XMLHttpRequest は非対応であり、設定可能な Origin 許可リストは現在ありません。

## コントリビュート

[CONTRIBUTING.ja.md](CONTRIBUTING.ja.md) を参照してください。開発規約は [AGENTS.md](AGENTS.md)(英語)にあります。

## ライセンス

[MIT](LICENSE)
