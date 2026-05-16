# UnionAir — Unity REST Bridge

UnionAir は Unity Editor の状態をシンプルな **REST API** として外部に公開する Editor 専用パッケージです。  
LLM の MCP ブリッジ・開発ボット・CI ツールなど、HTTP を話せるあらゆるクライアントから Unity の情報を取得できます。

---

## セットアップ

### 1. パッケージのインポート

プロジェクトの `Packages/com.leonakasaka.unionair/` フォルダが存在すれば Unity が自動で検出します（embedded package）。特別な操作は不要です。

### 2. サーバーの確認

Unity Editor を開くと自動的に REST サーバーが起動します（デフォルトポート: **8765**）。

```
Window > UnionAir > REST Bridge
```

上記のメニューから EditorWindow を開き、サーバーの状態を確認してください。

### 3. 動作確認

```bash
curl http://localhost:8765/api/health
# => {"status":"ok","unityVersion":"6000.3.5f2"}
```

---

## クイックスタート

### シーンのヒエラルキーを取得する

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

### 特定 GameObject のコンポーネントを確認する

```bash
curl "http://localhost:8765/api/gameobjects?path=Main Camera"
```

### アセット一覧を検索する

```bash
# すべての Texture2D
curl "http://localhost:8765/api/assets?type=Texture2D"

# パスでフィルター
curl "http://localhost:8765/api/assets?path=Assets/UI"
```

---

## EditorWindow の操作

| 項目 | 説明 |
|------|------|
| **Status** | サーバーの起動状態とポート番号を表示 |
| **Port** | サーバーのリッスンポート（停止中のみ変更可） |
| **Auto Start on Load** | Editor 起動時に自動でサーバーを起動するか |
| **Start / Stop / Restart** | サーバーの手動制御 |
| **Request Log** | 受信したリクエストのログ（最新 100 件） |

---

## ドキュメント

- [API リファレンス](api-reference.md) — 全エンドポイントの詳細仕様

---

## ライフサイクル

- **Editor 起動時**: `[InitializeOnLoad]` によりサーバーが自動起動
- **Domain リロード時**: ポートを解放してスレッドを停止し、リロード後に自動再起動
- **プレイモード中**: サーバーは継続稼働。Exit Play Mode 後に停止していれば自動再起動
