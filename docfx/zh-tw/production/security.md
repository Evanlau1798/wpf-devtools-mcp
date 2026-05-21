# 安全模型

本頁記錄的是目前已交付程式碼中實際存在的安全控制，而不是未來規劃。

## 威脅模型

這個 server 可以直接檢查並修改執行中的 WPF UI 狀態，因此下列風險必須納入考量：

- 未授權存取 inspector pipe
- 在 `connect` 過程中載入非預期的 inspector DLL
- 透過本機檔案洩漏金鑰或憑證
- 在 named-pipe 通道上遭受中間人攻擊或身分偽冒

## 已實作的控制措施

### DLL 驗證

`connect` 在載入 inspector DLL 前會先驗證它。

- **Debug build**：受信任的本機路徑可略過簽章驗證，方便本機開發。
- **Release build**：強制進行簽章驗證。
- **Path validation**：目前發佈版本只接受 trusted roots 內的路徑。

### Raw injection 目標政策

預設情況下，server 不會對任意同使用者 WPF process 做 raw DLL injection。

- 目前 shipping server 不會隱式信任目前 repository root 底下的 project-scoped target。
- 若 target executable 沒有被明確 allowlist，而且沒有更早的 default-pipe compatibility failure 先中止連線流程，`connect()` 會直接 fail closed，回傳 `errorCode: SecurityError` 與 `requiresExplicitTargetOptIn: true`，而不是繼續注入。
- 若預期的 pipe 名稱已經被 stale 或 incompatible 的 default-pipe host 佔用，`connect()` 可能會先回傳 `errorCode: CompatibilityError`，也就是先回傳 compatibility rejection；但 raw injection 仍然會維持 blocked。
- 只有在你明確要對某個 app 做 raw injection 時，才設定 `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS`，其值必須是以分號分隔的 exact absolute executable path 清單。
- 若希望在 production 對外部 target 做診斷而不擴大 raw injection 範圍，應優先使用 `InspectorSdk.Initialize()` 的 SDK-hosted reuse 路徑。

### MCP tool 與 target policy gates

server 會在把高風險 MCP `tools/call` 派送到 tool implementation 前先做政策檢查。

- `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 會把所有 `connect()` target 限制在 exact absolute executable path 清單內，且會在 SDK-hosted reuse 或 raw injection 前套用；未設定或 malformed entry 會 fail closed。
- `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true` 會 opt in runtime mutation、interaction、render-measurement 與 session state-consuming tools，例如 `set_dp_value`、`click_element`、`execute_command`、`measure_element_render_time`、`capture_state_snapshot`、`restore_state_snapshot`、`drain_events`、`batch_mutate`。
- `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` 會 opt in MCP 邊界的 `element_screenshot`。
- `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` 會 opt in `get_viewmodel`、`get_commands`、`modify_viewmodel` 與 `execute_command`。
- boolean gate 未設定、false 或無效時會讓受影響類別 fail closed。

### Named-pipe 驗證

以 injection 為基礎的 `connect` session 預設就會使用 HMAC challenge-response 驗證。

- 這個 secret 必須是 base64 編碼。
- 若未設定 `WPFDEVTOOLS_AUTH_SECRET`，server 會先產生一組預設 secret，之後在同一個使用者 profile 下跨 server restart 重用。
- 若需要 deterministic 的 shared secret，可設定 `WPFDEVTOOLS_AUTH_SECRET` 來覆寫 server 產生的預設 secret。
- 在 injection-based bootstrap 期間，server 會把短生命週期的 auth-secret handoff file 寫成 DPAPI-protected payload，native bootstrapper 讀取後會刪除該檔案。這可以避免 temp file 直接暴露 plaintext secret，但已經以同一 Windows 使用者執行的本機程式仍屬於 local trust boundary。
- 若要讓 `connect()` 重用 SDK-hosted Inspector，`WPFDEVTOOLS_AUTH_SECRET` 與 `WPFDEVTOOLS_CERT_DIR` 必須在兩邊一起設定，並且要在呼叫 `InspectorSdk.Initialize()` 前完成。預設 hardened 的 MCP server 不會重用 plaintext SDK host。
- 若其中任一值缺漏，或兩者都未設定，`InspectorSdk.Initialize()` 現在會直接 fail closed，不再啟動 plaintext SDK host。

### Named pipes 上的 TLS

以 injection 為基礎的 `connect` session 預設就會使用 inspector 連線的 TLS。

- secure named-pipe transport 目前固定使用 TLS 1.2，以維持 .NET 8 與 .NET Framework 4.8 runtime path 的相容性。
- server 會在該目錄建立或重用憑證。
- 若未設定 `WPFDEVTOOLS_CERT_DIR`，server 會使用 `%APPDATA%\WpfDevTools\certs` 下的預設憑證目錄。
- 若有明確設定 `WPFDEVTOOLS_CERT_DIR`，它必須是 local absolute directory。Network paths are not allowed；UNC path 與 mapped network drive 會被拒絕。
- client 會驗證 subject 並 pins the expected thumbprint。
- `WPFDEVTOOLS_CERT_THUMBPRINT` 可用來覆寫預期 thumbprint。
- 若 target app 會呼叫 `InspectorSdk.Initialize()`，只有在兩邊使用相同的 `WPFDEVTOOLS_AUTH_SECRET`，並且使用同一個 local absolute `WPFDEVTOOLS_CERT_DIR` 值時，`connect()` 才能重用既有的 SDK-hosted Inspector。
- 即使不是 SDK-host reuse，任何使用 default-pipe 的 `connect()` 嘗試，在 client 接受連線前也會先驗證 named-pipe server 確實屬於指定 target process，且該 host 回報的 protocol/build fingerprint 與目前 MCP server 相容。
- 在重用既有 host 前，client 也會驗證 named-pipe server 確實屬於指定 target process，且該 host 回報的 protocol/build fingerprint 與目前 MCP server 相容。

### Pipe 存取限制與 server 端保護

- Pipe ACL 只開放給目前使用者與 SYSTEM。
- 請求會序列化並受 framing 大小限制保護。
- server 會在 session 層級做 rate limiting。
- Tool policy gates 可以在送出任何 target-process request 前，先擋下 destructive tools、screenshots、ViewModel inspection 與未 allowlist 的 target。

## 建議的生產環境姿態

1. 使用 `Release` build。
2. 對 inspector DLL 做 Authenticode 簽署。
3. 保留預設的 injection-based transport hardening。
4. 若需要 deterministic secret rotation 或 SDK mode coordination，設定 `WPFDEVTOOLS_AUTH_SECRET`。
5. 若需要 deterministic 的憑證儲存位置或要與 SDK mode 共用，請在兩邊設定相同的 local absolute `WPFDEVTOOLS_CERT_DIR`。
6. 視需要設定 `WPFDEVTOOLS_CERT_THUMBPRINT`。
7. 預設停用 raw injection；只有在 executable path 經過明確審查後，才使用 `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS`。
8. 將 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 設為 server 可連線的已審查 executable path。
9. 若 session 不需要 destructive tools、screenshots 或 ViewModel inspection，請用 `WPFDEVTOOLS_MCP_ALLOW_*` gates 停用。
10. 限制哪些人可以在工作站或 VM 上啟動 server。

## 重要限制

- TLS 預設使用本機自管憑證，而非企業 PKI。
- SDK-hosted Inspector 需要先設定相同的 transport configuration，`connect()` 才能重用既有 host；啟用 TLS 時也必須使用相同的 local absolute `WPFDEVTOOLS_CERT_DIR`。Network paths are not allowed。
- 目前已交付的 transport 是 STDIO 加 named-pipe inspector 通訊；HTTP transport 不在目前二進位發佈範圍內。
