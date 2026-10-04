# 検証記録

2026-10-04 に Linux x64、Microsoft .NET SDK 10.0.401 / runtime 10.0.12 で実施。

## 成功

- NuGet から固定バージョン ModelContextProtocol.AspNetCore 2.2.0 を restore。
- `dotnet build CodexUsageMcp.slnx -c Release --no-restore -m:1`: 警告 0、エラー 0。
- `dotnet tests/CodexUsageMcp.Tests/bin/Release/net10.0/CodexUsageMcp.Tests.dll`: 26 チェック成功。
  - 週次／5 時間枠を duration で識別、multi-bucket 優先、超過時 0%、不正値は不明。
  - nullable リセット／明細、64-bit の権威ある件数、期限なしと不明の区別、Unix 秒。
  - 模擬子プロセスによる initialize→initialized→read、通知の無視、追加操作要求の拒否、大量 stderr の読み捨て。
  - エラー、壊れた JSON、途中 EOF、サイズ超過、タイムアウト、取消、実行ファイル不在、上流詳細の非開示。
- `python tests/smoke.py /path/to/dotnet`: 4 グループ成功。
  - `--once` JSON、任意の作業ディレクトリからの実行、設定ファイルと環境変数の組み合わせ。
  - 実際の MCP stdio initialize、tools/list、tools/call。
  - HTTP healthz、Origin/Host 拒否、MCP initialize。
  - 追加 Kestrel endpoint 設定時の起動拒否。

テストは必ず `dotnet <test.dll>` で起動します。テスト用 apphost を直接起動する形式には対応していません。

- `dotnet publish src/CodexUsageMcp/CodexUsageMcp.csproj -c Release --no-restore -o <temporary-directory> -m:1`: 成功。配布用 ZIP はソース一式で、SDK や認証情報、ビルド生成物を含めません。

## 未検証

- Windows 365 / Windows 上での実行そのもの。
- 実在アカウントに対する Codex CLI の照会、各プランの実応答。
- インターネット公開や OAuth。そもそも実装対象外です。
- タイムアウトテストは結果と処理完了を確認しますが、OS のプロセス一覧から全子孫の終了を別途検査するテストではありません。

ビルド中に、この実行環境特有の読み取り専用ホームディレクトリで NuGet キャッシュ書き込みに失敗しました。DOTNET_CLI_HOME / NUGET_PACKAGES / NUGET_HTTP_CACHE_PATH / XDG_DATA_HOME を書き込み可能な一時ディレクトリへ変更し、restore/build を再実行して成功しました。利用者環境にこの変更を要求するものではありません。
