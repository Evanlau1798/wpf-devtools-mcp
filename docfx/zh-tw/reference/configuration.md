# 設定參考

## MCP server runtime 變數

| 變數 | 用途 | 備註 |
| --- | --- | --- |
| `WPFDEVTOOLS_AUTH_SECRET` | 覆寫 persisted/default HMAC 驗證 secret | 必須是 base64 編碼；injection session 預設會啟用驗證 |
| `WPFDEVTOOLS_CERT_DIR` | 覆寫預設 TLS certificate directory | 請使用受保護的本機目錄；injection session 預設使用 TLS |
| `WPFDEVTOOLS_CERT_THUMBPRINT` | pin 預期憑證的 thumbprint | 在高限制部署中很有幫助 |
| `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` | allowlist raw-injection target executable | 以分號分隔的 exact absolute executable path；優先使用 SDK-hosted reuse |
| `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` | 限制所有 MCP `connect()` target | 以分號分隔的 exact absolute executable path；設定中若含 malformed entry 會 fail closed |
| `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS` | 啟用或停用 destructive MCP tool call | 涵蓋 runtime mutation、interaction 與 render measurement；接受 `true`/`false`、`1`/`0`、`yes`/`no`、`on`/`off`；未設定時保留目前本機開發工具面 |
| `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS` | 啟用或停用 `element_screenshot` | boolean 值同上；無效值會讓 screenshot call fail closed |
| `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION` | 啟用或停用 ViewModel inspection tool | boolean 值同上；停用時會擋下 `get_viewmodel`、`get_commands`、`modify_viewmodel` |
| `WPFDEVTOOLS_RATE_LIMIT_RPM` | 覆寫 MCP server request rate limit | 每分鐘 request 數，必須是正整數 |
| `WPFDEVTOOLS_TEXT_FALLBACK_MODE` | 控制 MCP `content[0].text` fallback 詳細程度 | 設為 `full` 會輸出完整 JSON text；未設定時使用 compact fallback |

## Installer 與 package 變數

| 變數 | 用途 | 備註 |
| --- | --- | --- |
| `WPFDEVTOOLS_SKIP_ELEVATION` | 讓 `run.bat` 與 installer registration 留在目前 shell | CLI registration 必須保持 unelevated 時設為 `1` |
| `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` | 用 certificate thumbprint pin 預期 release signer | verified release sidecars 不在相鄰位置時供 package-local install 使用 |
| `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` | 用 certificate subject pin 預期 release signer | package-local install 可用來取代 thumbprint pin |
| `WPFDEVTOOLS_ALLOW_ELEVATED_CLI_COMMAND_PATH` | elevated 狀態下啟用可信任 CLI command path override | 必須設為 `1`，否則 elevated Claude/Codex path override 會被忽略 |
| `WPFDEVTOOLS_CLAUDE_COMMAND_PATH` | 提供可信任的 Claude CLI 絕對路徑 | 只在上述 elevated opt-in 或可信任 unelevated shell 中使用 |
| `WPFDEVTOOLS_CODEX_COMMAND_PATH` | 提供可信任的 Codex CLI 絕對路徑 | 只在上述 elevated opt-in 或可信任 unelevated shell 中使用 |

除了已交付程式碼或 release-only maintainer guide 明確記錄的內容外，不應假設還有其他 `WPFDEVTOOLS_*` 變數存在。

## Build 模式

### Debug

- 最適合本機開發
- trusted local roots 可略過簽章驗證
- 是未簽署本機 build 最容易啟動的模式

### Release

- 用於生產環境部署
- 強制簽章驗證
- 建議搭配程式碼簽署與明確安全設定

## 架構專屬建置

managed build 請使用 `-p:Platform=`，native bootstrapper 請使用 `msbuild /p:Platform=`。
