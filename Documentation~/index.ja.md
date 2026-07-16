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
| **Start / Stop / Restart** | サーバの手動制御 |
| **Request Log** | 受信リクエストのログ(最新100件) |

---

## ドキュメント

- [API リファレンス](api-reference.ja.md) — 全エンドポイントの詳細仕様
- [カスタムコントローラ](custom-controllers.ja.md) — アプリケーション側 UnionAir API の拡張ガイド

---

## ライフサイクル

- **Editor 起動時**: `[InitializeOnLoad]` によりサーバが自動起動
- **Domain reload 中**: ポートを解放してスレッドを停止し、リロード後に自動再起動
- **Play モード中**: サーバは稼働し続けます。Play モード終了後に停止していた場合は自動的に再起動します
