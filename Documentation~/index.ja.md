# UnionAir — Unity REST Bridge

[English](index.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](index.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

UnionAir は Unity Editor の状態をシンプルな **REST API** として外部公開する、Editor 専用パッケージです。
LLM MCP ブリッジ・開発ボット・CI ツールなど、HTTP を扱える任意のクライアントから Unity の情報を取得できます。

---

## セットアップ

### 1. パッケージのインストール

**Window > Package Manager** を開き、**+** から **Install package from git URL...** を選択して次の URL を入力します:

```
https://github.com/LeonAkasaka/UnionAir.git
```

または、`Packages/manifest.json` に直接依存関係を追加します:

```json
{
  "dependencies": {
    "com.leonakasaka.unionair": "https://github.com/LeonAkasaka/UnionAir.git"
  }
}
```

パッケージをプロジェクトの `Packages/com.leonakasaka.unionair/` フォルダに配置した場合は、埋め込みパッケージとして Unity が自動検出します。

Test Runner API は任意機能です。プロジェクトに Unity Test Framework パッケージ(`com.unity.test-framework`)が導入されている場合のみ表示され、UnionAir EditorWindow で **Test Runner** カテゴリを明示的に有効化するまで無効です。

**Profiling** カテゴリも既定で無効です。AI向けのmetric要約、frame単位NDJSON、Unity Profiler raw capture、Memory Profiler snapshotを提供します。詳細は[Profiling API](api/profiling.ja.md)を参照してください。

コンパイル結果の読み取りに設定は不要です。`GET /api/compile` は常時有効な **Read** カテゴリに属し、API 経由だけでなく IDE から開始されたコンパイルも記録します。`POST /api/compile` によるコンパイル要求には **Asset Write** カテゴリが必要です。書き込み・コンパイル・修正のサイクルを自動化する場合は、まず [コンパイルと修正のループ](api/compile.ja.md#コンパイルと修正のループ) を読んでください。コンパイルが成功しても domain reload が起きない場合に正しく終了する方法を扱っており、これがクライアントのハングの主な原因です。

### 2. サーバの確認

Unity Editor を開くと、REST サーバが自動的に起動します(デフォルトポート: **8765**)。

```
Window > UnionAir > REST Bridge
```

上記メニューから EditorWindow を開き、サーバの状態を確認してください。

### 3. 動作確認

```bash
curl http://localhost:8765/api/health
# => {"status":"ok","unityVersion":"6000.3.5f2"}
```

---

## クイックスタート

### シーン階層の取得

```bash
curl http://localhost:8765/api/scene/hierarchy
```

```json
{
  "scene": "SampleScene",
  "objects": [
    {
      "name": "Main Camera",
      "path": "Main Camera",
      "isActive": true,
      "tag": "MainCamera",
      "layer": 0,
      "transform": {
        "position": { "x": 0, "y": 1, "z": -10 },
        "rotation": { "x": 0, "y": 0,  "z": 0  },
        "scale":    { "x": 1, "y": 1,  "z": 1  }
      },
      "children": []
    }
  ]
}
```

### 特定の GameObject のコンポーネントを確認

```bash
curl --get "http://localhost:8765/api/gameobjects" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}'
```

### アセット一覧の検索

```bash
# すべての Texture2D
curl "http://localhost:8765/api/assets?type=Texture2D"

# パスで絞り込み
curl "http://localhost:8765/api/assets?path=Assets/UI"
```

---

## EditorWindow の使い方

| 項目 | 説明 |
|------|------|
| **Status** | サーバの稼働状態とポート番号を表示 |
| **Port** | サーバの待ち受けポート(停止中のみ変更可能) |
| **Auto Start on Load** | Editor 起動時にサーバを自動起動するかどうか |
| **Diagnostic Lifecycle Logging** | listener の詳細なライフサイクルイベントを Console へ逐次出力するかどうか(デフォルトでは無効) |
| **Start / Stop / Restart** | サーバの手動制御 |
| **Request Log** | 受信リクエストのログ(最新100件) |

---

## ドキュメント

- [API リファレンス](api-reference.ja.md) — 全エンドポイントの詳細仕様
- [カスタムコントローラ](custom-controllers.ja.md) — アプリケーション側 UnionAir API の拡張ガイド

---

## ライフサイクル

- **Editor 起動時**: `[InitializeOnLoad]` によりサーバが自動起動
- **Domain reload 中**: listener を閉じ、background thread の終了を短時間待機し、キュー内の response を閉じ、listener が所有する処理中または deferred の接続を中断してから、リロード後に自動再起動
- **Play モード中**: サーバは稼働し続けます。Play モード終了後に停止していた場合は自動的に再起動します

リロード直後の自動起動で一時的な address-in-use エラーが発生した場合、UnionAir は初回の試行に続いて約4秒間に最大5回再試行します。途中の address-in-use エラーはライフサイクルトレースにだけ保持され、Console や `/api/editor/logs` には出力されません。その他の起動失敗は常に短いエラーとして Console に出力されます。listener thread が予期せず終了した場合は、listener の清掃を完了してから診断トレースを出力し、ドメインあたり最大3回の遅延付き復旧を行います。それ以降の予期せぬ終了では自動復旧を停止し、無制限の再起動ループに入らず短いエラーを出力します。UnionAir は Domain reload をまたぐ固定長のライフサイクル履歴を通常は出力せずに保持し、起動または清掃に失敗した場合はドメインあたり1回だけ自動的にまとめて出力します。通常時にも process、reload generation、listener の清掃、thread、native socket の詳細を逐次確認するには **Diagnostic Lifecycle Logging** を有効にしてください。

deferred handler は response の生存期間を自身で管理します。停止時に残っている deferred 接続は listener を閉じることで中断されるため、deferred handler は reload またはサーバ停止後の response 書き込み失敗を処理する必要があります。
