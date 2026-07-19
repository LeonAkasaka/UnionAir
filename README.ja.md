# UnionAir — Unity REST Bridge

[English](README.md) | **日本語**

> **⚠️ Experimental(実験的)**
> 本パッケージはベータ以前の実験的な試作品です。後方互換性・バージョンの安定性・動作は**一切保証されません**。すべての API は予告なく変更・削除される可能性があります。

> **注記**: 本ドキュメントは [英語版 README](README.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

UnionAir は Unity Editor の状態をシンプルな **REST API**(HTTP)として公開し、LLM MCP ブリッジ・開発ボット・CI ツールなど、任意の HTTP クライアントから簡単に利用できるようにします。

## 動作要件

- Unity **6000.0** 以降
- Unity Test Framework(任意。Test Runner API を使う場合のみ必要)

## インストール

### Package Manager(Git URL)から

1. **Window > Package Manager** を開きます。
2. **+** をクリックし、**Install package from git URL...** を選択します。
3. 次の URL を入力します:

```
https://github.com/LeonAkasaka/UnionAir.git
```

### manifest.json から

`Packages/manifest.json` に依存関係を追加します:

```json
{
  "dependencies": {
    "com.leonakasaka.unionair": "https://github.com/LeonAkasaka/UnionAir.git"
  }
}
```

## セットアップ

1. Unity Editor のロード時に HTTP サーバが自動的に起動します。
2. **Window > UnionAir > REST Bridge** を開くと、状態の確認とポートの設定ができます。

## デフォルトポート

`8765` — EditorWindow から変更可能です。

## エンドポイント

| グループ | 範囲 | セキュリティ |
|-------|-------|----------|
| **Read** | シーン階層、ロード済みシーン、GameObject、アセット、カメラ、ログ、検索 | 常時有効 |
| **Scene Write** | シーンの作成/オープン/アンロード、GameObject・コンポーネントの作成/更新/削除 | 既定で無効 |
| **Asset Write** | プレハブ、マテリアル、アセットファイル、AssetDatabase リフレッシュ | 既定で無効 |
| **Play Mode** | Play モードの開始/終了/一時停止/ステップ、Input System アクション、Canvas UI 操作 | 既定で無効 |
| **Editor Actions** | 選択、オブジェクトの ping、アセットのオープン、Unity Editor メニュー項目の実行 | 既定で無効 |
| **Test Runner** | EditMode / PlayMode テストの発見、実行、監視、キャンセル、結果ダウンロード | 既定で無効。Unity Test Framework 導入時のみ利用可能 |
| **Profiling** | ProfilerRecorder metric、NDJSON sample、Profiler raw capture、memory snapshot | 既定で無効 |

> Edit モードでの書き込み操作は Unity Editor 上で Undo(Ctrl+Z)できます。
> シーン上の GameObject と Component は読み取りレスポンスに `globalObjectId` を含み、書き込みリクエストでは型付きオブジェクト参照で指定できます。
> 書き込み API は `GET /api/help` で Play モードの安全性を宣言します。永続的なシーン/アセット変更は Play モード中はブロックされ、一部のシーンオブジェクト変更は Editor 設定と `allowWhilePlaying=true` の両方が必要です。
> エンドポイントの全一覧とリクエスト/レスポンスの詳細は **[API リファレンス](Documentation~/api-reference.ja.md)** を参照してください。

## セキュリティ

書き込み系カテゴリを有効化する前に必ずお読みください:

- サーバは **`localhost` のみ**にバインドされ、ネットワーク上の他のマシンからは到達できません。
- **認証はありません。** 同じマシン上で動作する任意のプロセスが、有効化されているすべてのエンドポイントを呼び出せます。
- レスポンスには `Access-Control-Allow-Origin: *` が含まれるため、**同じマシンのブラウザで開いている任意の Web ページ**からも API を呼び出してレスポンス(シーン階層、アセット、ログ、スクリーンショット)を読み取れます。
- 既定で有効なのは **Read** カテゴリのみです。Scene Write / Asset Write / Play Mode / Editor Actions / Test Runner / Profiling はオプトインであり、有効化するとプロジェクトの任意のテストコード、heap snapshot、Unity Editor のメニュー実行、アセット削除を含む操作や診断成果物が、任意のローカルクライアントとブラウザオリジンに公開されます。すべてのローカルクライアント(およびブラウザタブ)を信頼できる場合にのみ有効化してください。

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

- 自動テスト・CI は未整備です。
- リクエストボディの JSON パースは軽量な独自リーダーであり、深くネストした JSON や特殊なケースにエッジケースがあります。
- レスポンスの JSON シリアライズはエンドポイントごとの手書き実装であり、共有シリアライザへのリファクタリングを予定しています。
- CORS のワイルドカードポリシー(`Access-Control-Allow-Origin: *`)は将来のリリースで厳格化される可能性があります。

## コントリビュート

[CONTRIBUTING.ja.md](CONTRIBUTING.ja.md) を参照してください。開発規約は [AGENTS.md](AGENTS.md)(英語)にあります。

## ライセンス

[MIT](LICENSE)
