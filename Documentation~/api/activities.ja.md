# API リファレンス — Editor アクティビティ

[English](activities.md) | **日本語**

> **注記**: 本ドキュメントは [英語版](activities.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

ベース URL: `http://localhost:<port>/api/`(デフォルトポート: **8765**)。レスポンスの規約とカテゴリ/セキュリティの注意事項は [API リファレンス索引](../api-reference.ja.md) を参照してください。

このページはエンドポイント群ではありません。「Unity Editor が X で処理中である」ことを表すために UnionAir が用いる単一の語彙を説明します。これは `GET /api/help`、`GET /api/editor/status`、そして「リクエストが誤っている」ではなく「後で再試行せよ」を意味するすべての `409` に現れます。

---

## アクティビティ一覧

| アクティビティ | 意味 | 識別子 |
|----------|---------|---------------|
| `buildTargetSwitch` | アクティブなビルドターゲットの切り替え中。再インポートと domain reload を伴います | 切り替えレコード。[Build](build.ja.md#post-apibuildtarget) を参照 |
| `build` | プレイヤービルドがキュー済みまたは実行中 | ビルドレコード |
| `testRun` | Unity Test Framework の実行中 | UnionAir が開始した実行のレコード |
| `playMode` | Play モード中、またはその開始/終了の遷移中 | なし。Editor から観測 |
| `compile` | スクリプトコンパイルがキュー済みまたは実行中 | UnionAir が追跡中のコンパイルレコード。追跡していなければなし |
| `assetUpdate` | アセットのインポートまたはリフレッシュ中 | なし。Editor から観測 |

上記の順序が**優先順位**です。複数が同時に実行中の場合、UnionAir は最も上のものを報告し、拒否理由として示します。これは意図的です。ビルドは自身のコンパイルを実行し、テスト実行は Play モードを駆動するため、内側のアクティビティを理由として示すと、クライアントは誤った対象を待ち、早すぎる再試行を行うことになります。

アクティビティには 2 つの出所があります。`build` / `buildTargetSwitch` / `testRun` は**宣言型**です。UnionAir のサービスが開始するか、外部ツールが開始したものを引き受け、その識別情報は domain reload をまたいで保持されます。`playMode` と `assetUpdate` は、リクエスト時に `EditorApplication` から**観測**されます。Unity が既に追跡しており、二重に保持しても誤りしか生まないためです。観測型のアクティビティは `source: null`、`id: null` を報告します。名前を付けるべき対象が存在しないからです。

`compile` は**その両方**です。UnionAir がサイクルを追跡している間は宣言型で、それ以外では `EditorApplication.isCompiling` からの観測型になります。Unity がサイクルを開始してから UnionAir が引き受けるまでの瞬間と、サイクルを記録し終えてから domain reload が始まるまでの末尾がそれにあたります。この窓では観測型と同じく `source: null`、`id: null` を報告します。指し示すレコードが実際に存在しないからです。代替手段は `GET /api/compile` で、こちらは常に `current` と `latest` を返します。

---

## Editor が何を実行中かを読む

`GET /api/editor/status` が報告します:

```json
{
  "settled": false,
  "activeActivity": { "activity": "testRun", "source": "unionAir", "id": "41a31ce1-7921-448c-9ca3-21d0b14d3094" }
}
```

Editor がアイドルのとき `activeActivity` は `null` です。

| フィールド | 型 | 説明 |
|-------|------|-------------|
| `activity` | string | 上記のアクティビティ名のいずれか |
| `source` | string \| null | UnionAir が開始した場合は `unionAir`、引き受けた場合は `external`、ビルド自身のコンパイルは `build`、観測型は `null` |
| `id` | string \| null | アクティビティを所有するレコード id。ポーリング対象がなければ `null` |

引き受けた外部アクティビティは空文字列ではなく `id: null` を報告します。背後に UnionAir のレコードが存在せず、空の id を返せばクライアントが存在しないレコードをポーリングしかねないためです。

---

## アクティビティによる拒否

アクティビティにブロックされたリクエストは `activeActivity` を伴う `409` を返します:

```json
{
  "error": "This endpoint cannot be used while a script compilation is active.",
  "activeActivity": { "activity": "compile", "source": "unionAir", "id": "c-20260802-093318-a1b2c3" }
}
```

これは即座に再試行すべきエラーではありません。Editor を占有しているアクティビティ(この例では `GET /api/compile/{id}`)をポーリングし、決着してからリクエストを再発行してください。

一部の拒否応答は、ブロックされたエンドポイント側の語彙で同じアクティビティを表す追加オブジェクトを含みます。これらは互換性のために維持されており、`activeActivity` 以上のことは述べません:

- テスト実行にブロックされたすべてのエンドポイントにおける `activeTestRun`。
- コンパイルが既に実行中のときの `POST /api/compile` における `activeCompile`。

---

## 各エンドポイントを何がブロックするか

`GET /api/help` はエンドポイントごと・カテゴリごとに `blockedDuring` を報告します:

```json
{
  "method": "POST",
  "path": "/api/editor/play",
  "category": "playMode",
  "playModePolicy": "allowed",
  "testRunPolicy": "blocked",
  "blockedDuring": ["buildTargetSwitch", "build", "testRun", "compile"]
}
```

`blockedDuring` は優先順位順の完全なリストです。`playModePolicy` と `testRunPolicy` が強制するアクティビティも含むため、クライアントは 3 つのフィールドから答えを再構成する代わりに 1 つの配列を読めば済みます。

その大半は**カテゴリ**単位で宣言されます。衝突は個々のルートではなくカテゴリが行うことの性質であることが多いからです。Scene Write / Asset Write / Editor Actions / Play Mode はいずれもビルド中とビルドターゲット切り替え中に拒否されます。どちらも実行中にプロジェクトをディスクから読み取るため、それと競合する書き込みは、呼び出し側が確認できるどの状態とも一致しない出力を生みます。個々のエンドポイントはそこに追加します。`POST /api/editor/play` はコンパイル中にも拒否されます。サイクル完了時に Unity が domain reload を行い、モード変更をそれと共に破棄するためです。

`POST /api/editor/stop` / `pause` / `step` は意図的にコンパイル中でもブロック**しません**。他の処理が進行中に実行中のゲームを止められることこそ、クライアントに必要な能力だからです。

---

## 強制と報告は別物

`playModePolicy` と `testRunPolicy` は引き続きこの 2 つのアクティビティを制御する仕組みであり、単純なアクティビティ判定とは異なる振る舞いを持ちます:

- テスト実行のゲートはカテゴリ判定の**前**に評価されます。そのため、テスト実行にブロックされるエンドポイントはカテゴリが無効でも `409` を返します。実行終了後に再試行すると `403` になることがあります。
- Play モードはアクティビティマスクでは表現できないリクエスト単位のオプトイン(`allowWhilePlaying`)に対応し、カテゴリ判定の**後**に評価されます。

それ以外の `compile` / `assetUpdate` / `build` / `buildTargetSwitch` は、Play モード判定の後に置かれた 1 つの汎用ステージが強制します。強制は統一されていませんがメタデータは統一されているため、クライアントが読むべきものは `blockedDuring` 1 つのままです。

---

## domain reload とクラッシュへの耐性

宣言型アクティビティは識別情報を Unity の `SessionState` に保持します。これは domain reload をまたいで残りますが、Editor プロセスの再起動時には消去されます。この差がクラッシュ復旧を双方向で成立させます:

- **レコードがあってアクティビティがない** — そのレコードは死んだプロセスのものです。所有元のサービスが次回の初期化時に終端化します。
- **アクティビティがあってレコードがない** — これは残骸です。`SessionState` はすべての reload より長生きするため、これを閉じるものは存在せず、Editor はセッションが終わるまで busy を報告し続け、衝突を宣言したすべてのエンドポイントを拒否し続けます。信用せず、次回の初期化時に Console 警告とともに解放します。この判定の対象は UnionAir が所有するアクティビティだけです。引き受けたテスト実行は設計上レコードを持たないため、Unity Test Framework がまだ実行中かどうかを監視して回収します。

片方だけでは詰む経路が残るため、双方向を確認します。これは `InputReplayService` が既に用いていたパターンであり、それを共有したものです。

### UnionAir が開始する操作は、レコードが永続化されるまで始まらない

追跡対象の操作を開始するリクエスト(`POST /api/compile`、`POST /api/builds`、`POST /api/build/target`、`POST /api/test-runs`)は、アクティビティを開く**前**にレコードを永続化し、その書き込みに失敗した場合は何も開始せずに `500` を返します。書き込みは 1 回だけ即時リトライします。ウイルス対策や検索インデクサが一瞬だけ対象を掴むのが一般的な一過性の原因で、これは自然に解消するためです。ディスク満杯や書き込み不可のディレクトリは解消しないので、待たずに報告します。

テスト実行は、このルールを守るために Unity Test Framework の run 識別子を諦めた事例です。framework がその識別子を返すのは run を投入した後であり、それを key にしたレコードを先に書くことはできません。UnionAir は独自の id を発行し、framework の識別子は cancel に必要な handle として内部で保持します。

UnionAir が**引き受ける**だけのアクティビティは拒否できません(IDE が起こしたコンパイルは既に走っています)。これらは best-effort で記録され、終端処理は書き込みの成否に関係なくアクティビティを解放します。

実際上の意味は次の通りです。Editor のクラッシュで中断されたコンパイル・テスト実行・ビルドは、永久に実行中のままにならず `aborted` に落ち着きます。id でポーリングしているクライアントは、待ち続ける代わりにそれを知ることができます。

---

## 関連ドキュメント

- [Editor API](editor.ja.md) — `GET /api/editor/status`
- [Compile API](compile.ja.md) — ビルドが所有するものを含むコンパイルレコード
- [API リファレンス索引](../api-reference.ja.md)
