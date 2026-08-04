# セキュリティポリシー

[English](SECURITY.md) | **日本語**

> **注記**: 本ドキュメントは [英語版 SECURITY](SECURITY.md) の翻訳です。内容に乖離がある場合は英語版が優先されます。

## セキュリティモデル

UnionAir は Unity Editor 内で HTTP サーバを実行します。書き込み系カテゴリを有効化する前に、次のモデルを理解してください:

- サーバは **`localhost` のみ**にバインドされ、他のマシンからは到達できません。
- **認証はありません。** 同じマシン上の任意のプロセスが、有効化されているすべてのエンドポイントを呼び出せます。
- `Origin` ヘッダーを持つリクエストはルーティング前に拒否され、レスポンスには CORS を許可するヘッダーが含まれません。ブラウザの `fetch` と XMLHttpRequest は既定で非対応です。ブラウザ以外のローカルクライアントは `Origin` を送信しないでください。
- 空でないボディを持つリクエストには `Content-Type: application/json` が必要です。空の POST に Content-Type は不要です。
- 既定で有効なのは **Read** カテゴリのみです。Scene Write / Asset Write / Play Mode / Editor Actions / Test Runner / Profiling / Build はオプトインです。有効化すると、プロジェクトの任意のテストコード、Unity Editor のメニュー実行、アセット削除、ダウンロード可能な heap snapshot、スクリプティング定義シンボルなどのビルド構成、共有される `ProjectSettings/` ファイルへ書き込まれるビルド設定の変更、プロジェクトディレクトリへ実行可能なプログラムを書き出すプレイヤービルドを含む操作と診断成果物が、任意のローカルプロセスに公開されます。すべてのローカルクライアントを信頼できる場合にのみ有効化してください。
- Built-in APIのcategory checkbox、Custom Handlersのenablement、Play Mode scene change設定が、UnionAirの公開routeを直接制御します。これらは誤操作防止と露出範囲の制御に限られ、認証、悪意あるcodeに対する認可、sandbox、設定改ざんへの防御ではありません。
- `.unionair/settings.json`は通常のproject設定です。署名も改ざん耐性もなく、commitしてGitで共有できます。不正なproject設定はauto-startを無効化しReadだけを公開するfail-closed動作になりますが、これは不正な形式に対する保護であり、悪意ある書き換えへの防御ではありません。
- `.unionair/settings.json`がない場合、category enablementは従来どおりEditorPrefsに保存され、同じユーザーとEditor versionで開くproject間で共有されます。schema対象の最初のUI変更でproject fileを作成し、それ以降の変更は即時保存します。外部編集はEditor processを再起動するまで再読込しません。
- projectを変更できるprocessはsettings fileを編集したり、C# Editor codeを追加したりできます。Unity Editor内で動作するcodeはUnionAirの設定を変更でき、UnionAirを使わずにUnity、filesystem、networkへの特権操作も実行できます。そのため、API toggleは悪意あるproject codeやcode実行能力を持つlocal agentを封じ込められません。信頼できないcodeやclientとの間に実際の境界が必要な場合は、別のOS account、制限されたfilesystem permission、VMなどの隔離環境を使用してください。
- **Disable All Sensitive APIs...**は便宜的なresetです。server設定を維持したまま任意category、custom handler、Play Mode scene changeを無効化しますが、Editor process内で既に動作するcodeを無力化するものではありません。
- テストコードはハング、シーンやアセットの変更、Play モードへの移行、ファイルシステムやネットワークへのアクセスなど、Unity Editor プロセスに許可された任意のコードを実行できます。Test Runner API はテストをサンドボックス化せず、timeout も設けません。
- Memory Profiler の snapshot は Editor のマネージドヒープを取得します。そのため snapshot にはその時点で Editor がメモリ上に保持していた任意の文字列が含まれる可能性があり、認証のない同じローカルポートから配信されます。
- `.unionair/endpoint.txt` は発見用 metadata であり、認証でも生存証明でもありません。clientは`GET /api/health`を呼び出し、その`projectPath`を発見ファイルがあるdirectoryと比較する必要があります。Editorが強制終了すると、別projectのEditorを指す古い内容が残る可能性があります。

本パッケージは**実験的**であり、セキュリティ姿勢は将来のリリースで変更される可能性があります。

## サポート対象バージョン

最新リリースのみをサポートします。過去バージョンへの修正のバックポートは行いません。

## 脆弱性の報告

脆弱性は公開 Issue ではなく、[GitHub Security Advisories](https://github.com/LeonAkasaka/UnionAir/security/advisories/new) から非公開で報告してください。再現手順と、使用した Unity / パッケージのバージョンを含めてください。
