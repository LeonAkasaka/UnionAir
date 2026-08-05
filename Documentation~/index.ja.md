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
https://github.com/LeonAkasaka/UnionAir.git#v0.4.0
```

または、`Packages/manifest.json` に直接依存関係を追加します:

```json
{
  "dependencies": {
    "com.leonakasaka.unionair": "https://github.com/LeonAkasaka/UnionAir.git#v0.4.0"
  }
}
```

パッケージをプロジェクトの `Packages/com.leonakasaka.unionair/` フォルダに配置した場合は、埋め込みパッケージとして Unity が自動検出します。

Test Runner API は任意機能です。プロジェクトに Unity Test Framework パッケージ(`com.unity.test-framework`)の **1.4.0 以降**が導入されている場合のみ表示され、UnionAir EditorWindow で **Test Runner** カテゴリを明示的に有効化するまで無効です。Unity 2022.3 と 2023.1 は既定でより古いバージョン(それぞれ 1.1.33 / 1.3.9)を使うため、この API は利用できません。詳細は [サポート対象バージョン](../README.ja.md#サポート対象バージョン) を参照してください。

**Profiling** カテゴリも既定で無効です。AI向けのmetric要約、frame単位NDJSON、Unity Profiler raw capture、Memory Profiler snapshotを提供します。詳細は[Profiling API](api/profiling.ja.md)を参照してください。

コンパイル結果の読み取りに設定は不要です。`GET /api/compile` は常時有効な **Read** カテゴリに属し、API 経由だけでなく IDE から開始されたコンパイルも記録します。`POST /api/compile` によるコンパイル要求には **Asset Write** カテゴリが必要です。書き込み・コンパイル・修正のサイクルを自動化する場合は、まず [コンパイルと修正のループ](api/compile.ja.md#コンパイルと修正のループ) を読んでください。コンパイルが成功しても domain reload が起きない場合に正しく終了する方法を扱っており、これがクライアントのハングの主な原因です。

### 2. サーバの確認

Unity Editor を開くと、REST サーバが自動的に起動します。デフォルトのport modeは
**Automatic** で、空いている具体的なloopback portを選択してdiscovery fileへ公開します。

```
Window > UnionAir > REST Bridge
```

上記メニューから EditorWindow を開き、サーバの状態を確認してください。

起動成功後は、実際の API Base URL も `<project>/.unionair/endpoint.txt` に公開されます。ファイルを
読み込んで空白を除去し、短い timeout で `{baseUrl}health` を呼び出して、その`projectPath`が
`<project>`と一致することを確認してから`{baseUrl}help?detail=full`を呼び出してください。ファイルが
ない場合、health checkに失敗した場合、projectが一致しない場合は、検証済みserverがないものとして
扱います。processが強制終了すると、別projectのEditorを指す古い発見情報が残る可能性があります。

### Project設定

EditorWindowのschema対象controlはworking configurationを更新し、変更のたびに完全な
`<project>/.unionair/settings.json`へ自動保存します。strictなv1 documentは次の形です:

```json
{
  "schemaVersion": 1,
  "server": { "port": 0, "autoStart": true },
  "api": { "enabledCategories": [], "customHandlers": false },
  "playMode": { "allowSceneChanges": false }
}
```

すべてのfieldが必須です。built-in categoryはbare ID、custom categoryは`custom:<id>`を使います。
常時有効な`read`は記載できません。未知または重複したfield/category、型違い、未対応schema、無効な
port、`customHandlers:true`なしのcustom categoryはdocument全体を不正にします。不正な設定では
auto-startを無効化し、Readだけの安全状態へ移行します。最初のUI変更時に、その安全値を基礎として
完全なdocumentへ修復します。

有効なファイルはauto-start判断より先にproject値を供給します。Built-in APIのcategory checkbox、
**Custom Handlers > Enable Custom Handlers**、Play Modeのscene change checkboxが唯一のcontrolであり、
その値を直接ファイルへ保存します。端末別の承認レイヤーはありません。
custom categoryのcheckboxは、Custom Handlersのmaster switchを有効にするまで操作できません。
**Disable All Sensitive APIs...**はportとauto-startを維持したまま、すべての任意category、
custom handler、Play Mode scene changeを無効化します。ファイルがない場合は、schema対象の最初の
UI変更までは従来のEditorPrefs/default動作を維持します。その変更時に現在のeffective値を完全な
v1 documentへ移行し、即座に保存します。UI変更は
最初にメモリへ反映され、UTF-8 BOMなしで原子的に書き込まれます。書き込み失敗はpendingのまま自動
再試行します。domain reloadではSessionStateからworking documentを復元してdiskを再読込せず、外部
編集は次回のEditor process起動時に読み込みます。Diagnostic Lifecycle LoggingはEditorPrefsに残り、
project fileを作成しません。

これらの設定は誤操作を防ぎ、UnionAirが公開するrouteを限定するためのものであり、認証境界、sandbox、
改ざん防止ではありません。ファイルは署名されません。projectを変更できるprocessはファイルを編集し、
Unity processと同じ権限のEditor codeを追加できます。projectとすべてのlocal API clientを信頼できる
ものとして扱い、実際のsecurity boundaryが必要な場合はOSまたは実行環境で隔離してください。

### 3. 動作確認

```bash
BASE_URL="$(tr -d '\r\n' < .unionair/endpoint.txt)"
curl "${BASE_URL}health"
# => {"status":"ok","unityVersion":"6000.3.5f2","projectPath":"C:\\Work\\MyProject"}
```

---

## クイックスタート

### シーン階層の取得

```bash
curl "${BASE_URL}scene/hierarchy"
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
curl --get "${BASE_URL}gameobjects" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}'
```

### アセット一覧の検索

```bash
# すべての Texture2D
curl "${BASE_URL}assets?type=Texture2D"

# パスで絞り込み
curl "${BASE_URL}assets?path=Assets/UI"
```

---

## EditorWindow の使い方

| 項目 | 説明 |
|------|------|
| **Status** | サーバの稼働状態とポート番号を表示 |
| **Port Mode** | Automatic(デフォルト)またはFixed。Fixedでは`1..65535`を指定可能。稼働中の変更は即時保存され、Restartで反映 |
| **Auto Start on Load** | Editor 起動時にサーバを自動起動するかどうか |
| **Disable All Sensitive APIs...** | server設定を維持したまま、任意API category、Custom Handlers、Play Mode scene changeをすべて無効化 |
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

起動に成功するたび `.unionair/endpoint.txt` を原子的に置換します。clean stop、replacement start、
assembly reload、Editor 終了、検出可能な予期しない listener 停止では、ファイルがその server
instance のものと一致する場合だけ URL を削除します。runtime discovery と一時ファイルは
`.unionair/.gitignore` によって ignore されます。接続が拒否されたクライアントはファイルを再読込
してください。

Fixed modeでは、一時的なaddress-in-useエラーの後、同じconfigured portを約4秒間に最大5回再試行します。Automatic modeでは、まずreloadをまたいで保持したconcrete portを試します。そのportがまだ使用中なら0.1秒待ってもう一度試し、それでも失敗した場合にfresh candidateへ移ります。その後は最大8個の異なるfresh portを即時に試し、競合するURL reservationなどcandidate固有のlistener拒否は次のcandidateへ進みます。probeによる割り当てまたはlistener threadの起動に失敗した場合は、短いエラーを1回出して試行を中断します。途中のaddress-in-useエラーはライフサイクルトレースにだけ保持され、Consoleや`/api/editor/logs`には出力されません。listener threadが予期せず終了した場合は、listenerの清掃を完了してから診断トレースを出力し、domainあたり最大3回の遅延付き復旧を行います。それ以降の予期せぬ終了では自動復旧を停止し、無制限の再起動ループに入らず短いエラーを出力します。UnionAirはdomain reloadをまたぐ固定長のライフサイクル履歴を通常は出力せずに保持し、起動または清掃に失敗した場合はdomainあたり1回だけ自動的にまとめて出力します。通常時にもprocess、reload generation、listenerの清掃、thread、native socketの詳細を逐次確認するには **Diagnostic Lifecycle Logging** を有効にしてください。

deferred handler は response の生存期間を自身で管理します。停止時に残っている deferred 接続は listener を閉じることで中断されるため、deferred handler は reload またはサーバ停止後の response 書き込み失敗を処理する必要があります。
