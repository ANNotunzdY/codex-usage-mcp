# Codex Usage MCP (.NET 10 / C#)

ローカルでログイン済みの **自分の Codex アカウント**について、週次利用枠の残り割合と、サービスが提供する無料／獲得済みリセット枠の件数・状態・有効期限を取得する読み取り専用 MCP サーバーです。独立したプロジェクトで、既存のアプリケーションやリポジトリには依存しません。

## 取得できるもの・制約

- `get_codex_usage_status` ツール。引数不要。
- 各バケットの primary / secondary を読み、`windowDurationMins == 10080` のものを `weekly`、300 を `five_hour` と識別します。secondary を無条件に週次とは判断しません。
- `remainingPercent = max(0, 100 - usedPercent)`。これは**利用枠の割合**で、残トークン数、金額、絶対クレジット数ではありません。
- `freeResetCredits.availableCount` はサービスが返した権威ある件数です。明細は一部だけの場合があり、配列の長さから件数を算出しません。
- リセット情報自体が `null` は「不明／未提供」で、0 件ではありません。`credits: null` は明細未取得、`[]` は明細が取得され空だった状態です。
- 明細の `expiryState`: `expires_at` は期限あり、`no_expiry` は API が明示的に expiresAt:null を返した期限なし、`unknown` は項目欠落／不正値です。
- `ordinaryUsageAllowed` が提供されたときのみ返します。残量やリセット時刻だけで利用再開可能とは判断しません。
- API は `codexRateLimits` と返しますが、すべての利用枠が完全にリセットされることをこのプロジェクトが保証するものではありません。適用範囲はサービスの判定に従います。
- API が情報を提供しないプラン・認証方式・古い CLI では取得不可の場合があります。アカウント横断の管理者照会、他メンバーの残量取得には対応しません。

## 必要環境

1. [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（ビルド用）。公開 DLL の実行には .NET 10 ASP.NET Core Runtime が必要です。
2. [公式 Codex CLI](https://learn.chatgpt.com/docs/cli) の、`account/rateLimits/read` に対応したバージョン。
3. 利用者自身が同じ OS ユーザーで `codex login` を実行済みであること。必要に応じて `codex login status` で確認します。ただしログイン状態の存在はサービスへのアクセス成功まで保証しません。

このプログラムは認証ファイルを探索・解析しません。CLI 自身が通常の既存ログインを利用します。MCP 経由でログイン・ログアウト・リセット・購入・請求変更・スレッド／推論開始は行いません。CLI が通常行う認証更新やローカル診断の副作用まで無効化するものではありません。

## ビルド・検証

```sh
dotnet restore CodexUsageMcp.slnx
dotnet build CodexUsageMcp.slnx -c Release --no-restore
dotnet tests/CodexUsageMcp.Tests/bin/Release/net10.0/CodexUsageMcp.Tests.dll
# Optional: Python 3 smoke tests
python tests/smoke.py /absolute/path/to/dotnet
dotnet publish src/CodexUsageMcp/CodexUsageMcp.csproj -c Release -o publish
```

テストは外部テストフレームワークを使わないコンソール実行型です。`dotnet test` では実行されないため、必ず上記 DLL を実行してください。模擬 App Server も同じテスト DLL 内にあり、本物の Codex／認証にはアクセスしません。

## Windows 365 で利用する場合

Windows 365 上の利用者セッション内でビルド・実行し、同じユーザーで Codex にログインしてください。共有マシン上のサービスアカウントや SYSTEM アカウントへ認証情報をコピーしないでください。

PowerShell の例（展開先を `C:\Tools\CodexUsageMcp` とした場合）:

```powershell
cd C:\Tools\CodexUsageMcp
dotnet build CodexUsageMcp.slnx -c Release
dotnet tests\CodexUsageMcp.Tests\bin\Release\net10.0\CodexUsageMcp.Tests.dll
dotnet publish src\CodexUsageMcp\CodexUsageMcp.csproj -c Release -o publish
$env:Codex__Executable = 'C:\Your\Actual\Path\codex.exe'
dotnet C:\Tools\CodexUsageMcp\publish\CodexUsageMcp.dll --once
```

**Windows の npm 経由インストールが返す codex.cmd / codex.ps1 は、このプログラムの直接起動対象にはできません。** `Get-Command codex -All` で確認し、実際のネイティブ `codex.exe` の絶対パスを設定してください。公式 CLI 配布のネイティブ実行ファイルを使用します。場所はインストール方法によって異なるため固定値は仮定していません。cmd.exe / PowerShell / シェルコマンド文字列を Executable/Arguments に指定しないでください。

`Codex__Executable` などの環境変数、または publish フォルダ内の `appsettings.json` で設定できます。設定ファイルは起動時の作業ディレクトリではなく DLL の隣から読み込みます。再起動すると変更が反映されます。

## 3 つの起動方法

### 1. ローカル MCP stdio（推奨）

```sh
dotnet /absolute/path/publish/CodexUsageMcp.dll --stdio
```

Codex の MCP 設定に追加する例は `samples/codex-config.toml` を参照してください。パスは自分の環境に置き換えます。**MCP の command に dotnet run を指定しないでください。** ビルドログが stdout に混ざる可能性があるため、事前 publish した DLL を実行します。

### 2. JSON を 1 回出力

```sh
dotnet /absolute/path/publish/CodexUsageMcp.dll --once
```

標準出力に JSON オブジェクトを 1 行出力して終了します。成功／情報未提供は終了コード 0、取得エラーは 1。`status` も必ず確認します。シェルやローカル自動化から利用できます。

### 3. ローカル Streamable HTTP

```sh
dotnet /absolute/path/publish/CodexUsageMcp.dll
```

- MCP URL: `http://127.0.0.1:5078/mcp`
- 死活確認: `http://127.0.0.1:5078/healthz`（プロセスの稼働のみ。Codex ログインや残量の取得成功は検証しません）
- SDK の `HttpServerSessionMode.Stateless` を明示設定。
- IPv4 loopback のみにバインド。ポートは `Http__Port` で変更可能（1024–65535）。`Kestrel:Endpoints` の追加設定は拒否します。
- Origin ヘッダー付きブラウザー要求と localhost / 127.0.0.1 以外の Host を拒否します。CORS は有効化しません。

**HTTP は利用者認証を実装していません。** 同じマシンの他のアプリ／ユーザーは照会できる可能性があります。信頼できる単一ユーザー環境のみで使用し、可能なら stdio を選びます。ポート転送、リバースプロキシ、公開トンネルでインターネットに公開しないでください。Windows 365 が常時稼働でも、このプロジェクトはインターネット公開用の認証付き MCP サービスではありません。

公式 App Server ドキュメントは、App Server の認証利用についてローカル／オープンソース用途と hosted/commercial services の制約を説明しています。クラウドに個人の Codex 認証を置く構成はここでは提供しません。MCP 自体の OAuth 認証設計と、上流 Codex の利用条件・本人認証は別の問題です。

## 設定と安全性

`appsettings.json` の `Codex` セクション:

| 項目 | 初期値 | 意味 |
|---|---|---|
| Executable | codex | 信頼済み Codex ネイティブ実行ファイル。絶対パス推奨 |
| Arguments | app-server, --listen, stdio:// | 引数の配列。シェル展開なし |
| TimeoutSeconds | 20 | キュー待ち・起動・取得の合計期限（1–120 秒） |
| MaxResponseChars | 1048576 | JSONL 1 行の最大文字数 |

設定は信頼できる管理者だけが変更してください。Executable/Arguments はプログラム実行権限を持つ設定です。MCP ツール引数から変更できません。秘密情報を引数・設定に埋め込まないでください。

各照会で子プロセスを起動し、initialize 応答 → initialized 通知 → account/rateLimits/read の順に送信。照会を直列化し、終了・取消・タイムアウト時に子プロセスツリーを終了します。サーバーからの追加操作要求は承認せず、unsupported を返します。stderr はデッドロック防止のため読み捨て、ログへ転送しません。エラーには認証情報を含み得る上流メッセージを出しません。

## エラーの見方

- `start_failed`: CLI のインストールと実行ファイル設定を確認。Windows の .cmd シムにも注意。
- `upstream_error`: CLI 自身でログイン状態・プラン・バージョンを確認。元のエラーメッセージは安全のため非公開。
- `timeout`: 回線・CLI の応答を確認。必要に応じて TimeoutSeconds を増やす。
- `transport_closed` / `invalid_response`: CLI の互換性を確認。
- `status: unavailable`: 応答は取得できたが対応する残量・リセット情報が提供されなかった。

CLI のアップデートで API が変わる場合があります。インストール済みバージョンの正確なスキーマは `codex app-server generate-json-schema --out ./schemas` で確認できます。

## 一次資料

- [Codex App Server: protocol, initialization, rate limits, authentication](https://learn.chatgpt.com/docs/app-server)
- [Codex developer commands](https://learn.chatgpt.com/docs/developer-commands)
- [OpenAI schema: RateLimitWindow](https://github.com/openai/codex/blob/main/codex-rs/app-server-protocol/schema/typescript/v2/RateLimitWindow.ts)
- [OpenAI schema: reset credit](https://github.com/openai/codex/blob/main/codex-rs/app-server-protocol/schema/typescript/v2/RateLimitResetCredit.ts)
- [OpenAI schema: reset credit summary](https://github.com/openai/codex/blob/main/codex-rs/app-server-protocol/schema/typescript/v2/RateLimitResetCreditsSummary.ts)
- [Official C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [SDK v2.2.0 HTTP transport options](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.AspNetCore/HttpServerTransportOptions.cs)
- [NuGet: ModelContextProtocol.AspNetCore 2.2.0](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore/2.2.0)

実施した検証と未検証範囲は `VALIDATION.md` に記載しています。
