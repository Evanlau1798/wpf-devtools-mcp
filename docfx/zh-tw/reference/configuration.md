# 設定參考

## MCP server runtime 變數

### WPFDEVTOOLS_AUTH_SECRET

- 用途：覆寫 persisted/default HMAC 驗證 secret。
- 備註：必須是 base64 編碼，且至少 32 decoded bytes (256 bits)；injection session 預設會啟用驗證。

### WPFDEVTOOLS_CERT_DIR

- 用途：覆寫預設 TLS certificate directory。
- 備註：請使用受保護的本機目錄；injection session 預設使用 TLS。

### WPFDEVTOOLS_CERT_THUMBPRINT

- 用途：pin 預期憑證的 thumbprint。
- 備註：在高限制部署中很有幫助。

### WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS

- 用途：allowlist raw-injection target executable。
- 備註：以分號分隔的 exact local absolute executable path；malformed entry 會回 `InvalidPolicyConfiguration`；優先使用 SDK-hosted reuse。

### WPFDEVTOOLS_MCP_ALLOWED_TARGETS

- 用途：限制所有 MCP `connect()` target。
- 備註：必填，以分號分隔的 exact local absolute executable path；未設定會回 `SecurityError`，malformed entry 會回 `InvalidPolicyConfiguration`。

### WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS

- 用途：啟用或停用 destructive MCP tool call。
- 備註：涵蓋 runtime mutation、interaction、render measurement 與 session state-consuming tools，例如 `capture_state_snapshot` 與 `drain_events`；接受 `true`/`false`、`1`/`0`、`yes`/`no`、`on`/`off`；未設定、無效或 false 會 fail closed。

### WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS

- 用途：啟用或停用 `element_screenshot`。
- 備註：boolean 值同上；未設定、無效或 false 會讓 screenshot call fail closed。

### WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS

- 用途：啟用或停用 sensitive runtime read tools。
- 備註：涵蓋 target UI text、DependencyProperty 與 binding values、routed-event payloads、tree/scene summaries 與 state snapshots；boolean 值同上；未設定、無效或 false 會 fail closed。

### WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION

- 用途：啟用或停用 ViewModel inspection tool。
- 備註：boolean 值同上；未設定、無效或 false 會擋下 `get_viewmodel`、`get_commands`、`get_datacontext_chain`、`modify_viewmodel` 與 `execute_command`。當 `capture_state_snapshot` 要求 `viewModelPropertyNames`、`batch_mutate` capture 或 mutate ViewModel state，或 `wait_for_dp_change_after_mutation` 的 trigger mutation 使用 ViewModel tool 時，也會套用同一 gate。

### WPFDEVTOOLS_MCP_ALLOW_COMPOSER_RUNTIME_APPROVALS

- 用途：允許 `preview_ui_blueprint` 接受已審查的 `runtimePackApprovalTokens`。
- 備註：每個 content-bound token 只會在一次 preview call 中核准精確的 pack root、id、version 與 fingerprint，不會持久化；preview 仍需要 `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`。

### WPFDEVTOOLS_MCP_SKIP_EXISTING_HOST_REUSE

- 用途：在 `connect()` 中略過既有 SDK-hosted Inspector reuse probe。
- 備註：僅供診斷；正常 production usage 請保持 unset，讓 SDK-hosted reuse 維持 preferred path。

### WPFDEVTOOLS_RATE_LIMIT_RPM

- 用途：覆寫 MCP server request rate limit。
- 備註：每分鐘 request 數，必須是正整數；預設值為 300；超過 10000 的值會被 clamp 為 10000。

### WPFDEVTOOLS_TEXT_FALLBACK_MODE

- 用途：控制 MCP `content[0].text` fallback 詳細程度。
- 備註：只有 legacy text-only client 才設定為 `full`；base64 screenshots、log dumps 等大型或敏感 payload 欄位仍會從 text fallback 省略。未設定時使用 compact fallback。

`WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION` 不只是直接的 ViewModel tools gate。只要 snapshot、batch operation 或 wait-after-mutation trigger 要求 ViewModel state，也必須先明確啟用此 gate。

internal per-process rate limiter cache 上限為 1000 筆；滿額時會淘汰 least recently used entry。這個 cache capacity 目前不能透過環境變數調整。

`WPFDEVTOOLS_MCP_SKIP_EXISTING_HOST_REUSE` 只應用於診斷 SDK-host reuse 行為。除非你刻意驗證 raw-injection fallback，否則不要在一般 onboarding 或 production deployment 中設定。

## Installer 與 package 變數

### WPFDEVTOOLS_SKIP_ELEVATION

- 用途：讓 `run.bat` 與 installer registration 留在目前 shell。
- 備註：CLI registration 必須保持 unelevated 時設為 `1`。

### WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT

- 用途：用 certificate thumbprint pin 預期 release signer。
- 備註：production payload signature verification 必要且獨立的 trust root；sidecar 不能取代它。

### WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT

- 用途：加上預期 release signer certificate subject 檢查。
- 備註：只能作為 additional constraint；package-local install 仍需要 thumbprint trust root。

### WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY

- 用途：讓 portable checksum-only package run 找到 trusted release metadata。
- 備註：只有在 package-local `run.bat` 或 `bin\wpf-devtools-<arch>.exe` 與原始 release sidecars 分離時才使用。該目錄必須包含原始 archive 與 `SHA256SUMS.txt` 等 checksum metadata。這不能取代 signed production package 的 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`。

### WPFDEVTOOLS_CLAUDE_COMMAND_PATH

- 用途：提供可信任的 Claude CLI 絕對路徑。
- 備註：只在可信任的 unelevated shell 中使用；elevated installer 會拒絕環境變數提供的 CLI command path。

### WPFDEVTOOLS_CODEX_COMMAND_PATH

- 用途：提供可信任的 Codex CLI 絕對路徑。
- 備註：只在可信任的 unelevated shell 中使用；elevated installer 會拒絕環境變數提供的 CLI command path。

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
