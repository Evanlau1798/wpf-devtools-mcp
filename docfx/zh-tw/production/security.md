# 安全模型

本頁記錄的是目前已交付程式碼中實際存在的安全控制，而不是未來規劃。

## 威脅模型

這個 server 可以直接檢查並修改執行中的 WPF UI 狀態，因此下列風險必須納入考量：

- 惡意或被 prompt injection 影響的 MCP client 直接送出 `tools/call`
- 未授權存取 inspector pipe
- 在 `connect` 過程中載入非預期的 inspector DLL
- 透過本機檔案洩漏金鑰或憑證
- 在 named-pipe 通道上遭受中間人攻擊或身分偽冒

MCP client 預設是不可信任的（untrusted by default）。Tool description、annotation 與 prompt 只提供操作提示；真正的安全決策會在 server-side policy gates 執行，並且早於 process discovery details、UI text、screenshot、ViewModel value 或 runtime mutation 的回傳。

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
- 只有在你明確要對某個 app 做 raw injection 時，才設定 `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS`，其值必須是以分號分隔的 exact local absolute executable path 清單；malformed entry 會以 `errorCode: InvalidPolicyConfiguration` fail closed。
- 若希望在 production 對外部 target 做診斷而不擴大 raw injection 範圍，應優先使用 `InspectorSdk.Initialize()` 的 SDK-hosted reuse 路徑。

### MCP tool 與 target policy gates

server 會在把高風險 MCP `tools/call` 派送到 tool implementation 前先做政策檢查。

| Gate | 預設 | 啟用能力 | 控制的主要風險 | 常見第一個 tool |
| --- | --- | --- | --- | --- |
| `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` | target access 必要 | 用 exact local absolute executable path 允許 `connect()` target | process identity 與 window metadata 外洩 | `connect` |
| `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS` | `false` | UI text、binding values、DP values、event payloads、scene/tree summaries、runtime state | sensitive UI 或 application data 離開 target process | `get_ui_summary` |
| `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS` | `false` | 透過 `element_screenshot` 回傳 screenshot metadata/file/base64 | pixel data 外洩 | `element_screenshot` |
| `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION` | `false` | ViewModel、command、DataContext chain，以及 ViewModel-triggered snapshot/batch operations | runtime business state 暴露或 command 誤用 | `get_viewmodel` |
| `WPFDEVTOOLS_MCP_ALLOW_COMPOSER_RUNTIME_APPROVALS` | `false` | 一次 `preview_ui_blueprint` call 使用的已審查 `runtimePackApprovalTokens` | 載入未審查的第三方 preview dependencies | `preview_ui_blueprint` |
| `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS` | `false` | 已核准的 interaction、mutation、render measurement、state snapshots、event drain、batch mutation | target app state 被改變 | `click_element`、`batch_mutate` |
| `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` | 停用 | 對 exact reviewed executable path 啟用 raw injection fallback | unexpected code loading into app process | `connect` |

請只啟用符合診斷任務的最小 gate set。例如 scene-level binding triage 通常只需要 target allowlist 與 sensitive reads，不需要 screenshots、ViewModel inspection、destructive tools 或 raw injection。

- `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 會把所有 `connect()` target 限制在 exact local absolute executable path 清單內，且會在 SDK-hosted reuse 或 raw injection 前套用；未設定會以 `SecurityError` fail closed，malformed entry 會以 `InvalidPolicyConfiguration` fail closed。
- `get_processes` 與 `connect()` auto-discovery 會先套用 target policy，再回傳 process name、window title、architecture/runtime metadata 或 candidate details。Denied targets 會 redacted，只會以 aggregate counts 呈現。
- `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true` 會 opt in runtime mutation、interaction、render-measurement 與 session state-consuming tools，例如 `set_dp_value`、`click_element`、`execute_command`、`measure_element_render_time`、`capture_state_snapshot`、`restore_state_snapshot`、`drain_events`、`batch_mutate`。
- `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` 會 opt in MCP 邊界的 `element_screenshot`。
- `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` 會 opt in target UI text、DependencyProperty 與 binding values、routed-event payloads、tree/scene summaries，以及 runtime state snapshots。這是 per-session diagnostic profile gate，會保護 `get_ui_summary`、`get_visual_tree`、`get_bindings`、`get_state_diff` 等 read-heavy tools。
- `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` 會 opt in `get_viewmodel`、`get_commands`、`get_datacontext_chain`、`modify_viewmodel` 與 `execute_command`。當 `capture_state_snapshot` 要求 `viewModelPropertyNames`、`batch_mutate` capture 或 mutate ViewModel state，或 `wait_for_dp_change_after_mutation` 的 trigger mutation 使用 ViewModel tool 時，也會套用同一 gate。
- `WPFDEVTOOLS_MCP_ALLOW_COMPOSER_RUNTIME_APPROVALS=true` 可讓一次 preview call 透過 `runtimePackApprovalTokens` 提供已審查的 content-bound token。Token 綁定精確 pack root、id、version 與 fingerprint，不會持久化，也不會取代 `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`。
- boolean gate 未設定、false 或無效時會讓受影響類別 fail closed。

### MCP JSON-RPC envelope boundary

STDIO request 的 raw MCP JSON-RPC envelope 會先由 MCP C# SDK 解析，server 之後才會收到 typed request。因此 `initialize`、`resources/read`、`tools/list` 上的 `id` 與 `method` 等 pre-dispatch envelope fields 是 SDK-owned。本專案在 SDK parsing 之後驗證 tool-call names and arguments，接著在把 request 派送到 injected or SDK-hosted Inspector host 前，驗證 Inspector IPC request ids, methods, and correlation ids。

這不代表 tool execution 缺少 input validation。專案負責的邊界從 typed MCP request filters 與 tool wrappers 開始，會檢查 oversized tool names、unsupported tools、tool arguments、process target policy、sensitive-read gates、screenshot gates、ViewModel gates 與 destructive gates。下游 named-pipe IPC 邊界也會執行 request id、method、correlation id、framing 與 authentication constraints。

Screenshot capture 另外受 resource lifecycle controls 限制。`element_screenshot` 預設只回傳 metadata。Inline `base64` 只允許小型 PNG payload；較大的 pixel capture 必須使用 `outputMode: "file"`，讓回應提供 `wpf://screenshots/{screenshotId}` resource handle，而不是本機路徑。MCP `element_screenshot` file mode 會建立 MCP server-owned retained screenshot resources：server 會發出 per-process server-issued lease root，只把該 root 傳給 Inspector，接著由 `SessionManager` 註冊回傳的 PNG、在 24 小時後到期、把每個 MCP server session 限制在最多 100 筆，並在 eviction 或 expiry 時刪除 retained PNG file 與已分離的 lease root。一般 target-owned resources 會在 target session disconnect 時清除。唯一 preview exception 是成功的 `preview_ui_blueprint` 且 `screenshotOutputMode="file"`：該筆已註冊 resource 會在 temporary process disconnect 前分離，讓 client 可用 `resources/read` 讀取 `resourceUri`；它仍受 expiry、eviction 與 server session-manager disposal 管理。失敗的 preview capture 不會分離。這些 retained resources 由 `SessionManager` 清理，not by the Inspector default screenshot cache。Inspector default screenshot cache 位於 `%LOCALAPPDATA%\WpfDevTools\tmp\screenshots`，或在設定 `WPFDEVTOOLS_SCREENSHOT_DIR` 時使用該目錄；它只適用於沒有 server-issued lease root 的 Inspector file output。`full-uninstall` 會移除這個預設目前使用者 cache；auth secrets 與 certificates 仍維持需要手動刻意清理。

IPC payload size 會在 framing layer 受限制。`MessageFraming.MaxMessageSizeBytes` 是 named-pipe UTF-8 payload 的 10 MB hard per-frame limit。請把它視為 abuse 與 memory boundary，而不是可以任意調高的 tuning knob。大型回應必須在跨越 IPC 前透過 tool-level caps、truncation metadata、compact modes 或 screenshot file-mode resource handles 等 resource handles 先縮小。若沒有先設計並測試 streaming or chunking，包含 failure recovery 與 client-facing release documentation，不應提高 frame limit。

## Safe deployment profiles

請把這些 profile 當成 production 或 shared test workstation 的部署樣板。所有 target path 都必須是 exact local absolute executable path。未列在 profile 內的 boolean gate 應保持 unset 或 `false`。前四種 profile 優先使用已設定相同 `WPFDEVTOOLS_AUTH_SECRET` 與 `WPFDEVTOOLS_CERT_DIR` 的 SDK-hosted reuse；raw injection 只應用於經過額外核准的 emergency profile。

### Read-only diagnostics

適用於 scene、tree、binding、DP 與 state read，且允許 target UI text 離開 process 的診斷情境。

設定：

- `WPFDEVTOOLS_MCP_ALLOWED_TARGETS=<exact target exe>`
- `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true`

保持 unset 或 `false`：

- `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS`
- `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION`
- `WPFDEVTOOLS_MCP_ALLOW_COMPOSER_RUNTIME_APPROVALS`
- `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS`
- `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS`

會被 blocked：

- `element_screenshot`
- `get_viewmodel` 與 `modify_viewmodel`
- `set_dp_value`、`click_element` 與 `batch_mutate`
- raw-injection fallback

仍可使用：

- `get_ui_summary` 與其他 sensitive read tools，但只能用在 allowlisted target。

### Screenshot-enabled diagnostics

適用於 metadata 與 scene summary 不足，且已核准對審查過的 target 做 pixel capture 的情境。

設定：

- 所有 Read-only diagnostics gates
- `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true`

`element_screenshot` 預設應使用 `outputMode: "metadata"` 或 `"file"`。

會被 blocked：

- ViewModel tools
- `modify_viewmodel`、`set_dp_value`、`click_element` 與 `batch_mutate`
- raw-injection fallback

### ViewModel-enabled diagnostics

適用於檢查 commands、DataContext chain 與 ViewModel state，但不修改執行中 app 的情境。

設定：

- 所有 Read-only diagnostics gates
- `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true`

除非 mutation 另外核准，否則保持 `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS` unset。

會被 blocked：

- `modify_viewmodel`
- `execute_command`
- `set_dp_value`、`click_element` 與 `batch_mutate` mutation steps

### Mutation-enabled diagnostics

只適用於已核准，且 UI 或 ViewModel 變更可 rollback 的 workflow。

設定：

- 所有 Read-only diagnostics gates
- `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`

只有在需要時才加：

- 需要 ViewModel tools、ViewModel snapshot fields、ViewModel batch steps 或 ViewModel wait-after-mutation triggers 時，設定 `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true`。
- 需要 pixel evidence 時，設定 `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true`。

會被 blocked：

- 任何 gate 未啟用的能力。
- raw-injection fallback，除非 emergency profile 也被明確核准。

### Raw-injection emergency diagnostics

只在已審查的本機 target 無法 host SDK inspector 時，作為最後手段使用。

設定：

- `WPFDEVTOOLS_MCP_ALLOWED_TARGETS=<exact target exe>`
- `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS=<same exact target exe>`
- 只加入上方 profiles 中必要的最小 `WPFDEVTOOLS_MCP_ALLOW_*` gates。

會被 blocked：

- 未 allowlist 的 targets。
- `element_screenshot`、`get_viewmodel`、`modify_viewmodel`、`set_dp_value`、`click_element` 與 `batch_mutate`，除非對應 profile gate 也明確啟用。

### Named-pipe 驗證

以 injection 為基礎的 `connect` session 預設就會使用 HMAC challenge-response 驗證。

- 這個 secret 必須是 base64 編碼，且解碼後至少要有 32 decoded bytes (256 bits)。
- 若未設定 `WPFDEVTOOLS_AUTH_SECRET`，server 會先產生一組預設 secret，之後在同一個使用者 profile 下跨 server restart 重用。
- 若需要 deterministic 的 shared secret，可設定 `WPFDEVTOOLS_AUTH_SECRET` 來覆寫 server 產生的預設 secret。
- 在 injection-based bootstrap 期間，server 會把短生命週期的 auth-secret handoff file 寫成 DPAPI-protected payload，native bootstrapper 讀取後會刪除該檔案。這可以避免 temp file 直接暴露 plaintext secret，但已經以同一 Windows 使用者執行的本機程式仍屬於 local trust boundary。
- 預設 persisted auth-secret file 是 `%APPDATA%\WpfDevTools\auth\shared-secret.bin`。
- 若要讓 `connect()` 重用 SDK-hosted Inspector，`WPFDEVTOOLS_AUTH_SECRET` 與 `WPFDEVTOOLS_CERT_DIR` 必須在兩邊一起設定，並且要在呼叫 `InspectorSdk.Initialize()` 前完成。預設 hardened 的 MCP server 不會重用 plaintext SDK host。
- 若其中任一值缺漏，或兩者都未設定，`InspectorSdk.Initialize()` 現在會直接 fail closed，不再啟動 plaintext SDK host。

### Named pipes 上的 TLS

以 injection 為基礎的 `connect` session 預設就會使用 inspector 連線的 TLS。

- secure named-pipe transport 目前固定使用 TLS 1.2，以維持 .NET 8 與 .NET Framework 4.8 runtime path 的相容性。
- Named-pipe TLS negotiation 由 `scripts/tests/Test-TlsNegotiation.ps1` 驗證，涵蓋 `net8-net8`、`net8-net48` 與 `net48-net8` runtime pair。在同一套 harness 證明所有支援 pair 都能穩定 negotiation，且 release notes 標明已驗證的 Windows/.NET matrix 前，不應在 `SecureTransportProtocols.InspectorTransport` 啟用 TLS 1.3。
- server 會在該目錄建立或重用憑證。
- 若未設定 `WPFDEVTOOLS_CERT_DIR`，server 會使用 `%APPDATA%\WpfDevTools\certs` 下的預設憑證目錄。
- 若有明確設定 `WPFDEVTOOLS_CERT_DIR`，它必須是 local absolute directory。Network paths are not allowed；UNC path 與 mapped network drive 會被拒絕。
- Persisted PFX 檔案會留在受保護的本機憑證目錄，但 runtime certificate import 使用 non-exportable private key storage；transport 不會 fallback 到 `Exportable` key import。
- client 會驗證 subject 並 pins the expected thumbprint。
- `WPFDEVTOOLS_CERT_THUMBPRINT` 可用來覆寫預期 thumbprint。
- 若 target app 會呼叫 `InspectorSdk.Initialize()`，只有在兩邊使用相同的 `WPFDEVTOOLS_AUTH_SECRET`，並且使用同一個 local absolute `WPFDEVTOOLS_CERT_DIR` 值時，`connect()` 才能重用既有的 SDK-hosted Inspector。
- 即使不是 SDK-host reuse，任何使用 default-pipe 的 `connect()` 嘗試，在 client 接受連線前也會先驗證 named-pipe server 確實屬於指定 target process，且該 host 回報的 protocol/build fingerprint 與目前 MCP server 相容。
- 在重用既有 host 前，client 也會驗證 named-pipe server 確實屬於指定 target process，且該 host 回報的 protocol/build fingerprint 與目前 MCP server 相容。

Package `uninstall` 會移除 client registration。Package `full-uninstall` 會移除 installer-owned payloads 與 generated registration artifacts，但不會刪除目前使用者的 transport state，因為同一個 server profile 可能在 package upgrade 後重用它。若要刻意移除預設 persisted auth secret 與 TLS certificate store，請執行：

```powershell
Remove-Item -LiteralPath "$env:APPDATA\WpfDevTools\auth\shared-secret.bin" -Force
Remove-Item -LiteralPath "$env:APPDATA\WpfDevTools\certs" -Recurse -Force
```

### Pipe 存取限制與 server 端保護

- Pipe ACL 只開放給目前使用者與 SYSTEM。
- 請求會序列化並受 framing 大小限制保護。
- server 會在 session 層級做 rate limiting。
- Tool policy gates 可以在送出任何 target-process request 前，先擋下 destructive tools、screenshots、sensitive reads、ViewModel inspection 與未 allowlist 的 target。

## 建議的生產環境姿態

1. 使用 `Release` build。
2. 對 inspector DLL 做 Authenticode 簽署。
3. 保留預設的 injection-based transport hardening。
4. 若需要 deterministic secret rotation 或 SDK mode coordination，設定 `WPFDEVTOOLS_AUTH_SECRET`。
5. 若需要 deterministic 的憑證儲存位置或要與 SDK mode 共用，請在兩邊設定相同的 local absolute `WPFDEVTOOLS_CERT_DIR`。
6. 視需要設定 `WPFDEVTOOLS_CERT_THUMBPRINT`。
7. 預設停用 raw injection；只有在 exact local absolute executable path 經過明確審查後，才使用 `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS`。
8. 將 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 設為 server 可連線且經審查的 exact local absolute executable path。
9. 若 session 不需要 destructive tools、screenshots、sensitive reads 或 ViewModel inspection，請用 `WPFDEVTOOLS_MCP_ALLOW_*` gates 停用。
10. 限制哪些人可以在工作站或 VM 上啟動 server。

## 重要限制

- TLS 預設使用本機自管憑證，而非企業 PKI。
- SDK-hosted Inspector 需要先設定相同的 transport configuration，`connect()` 才能重用既有 host；啟用 TLS 時也必須使用相同的 local absolute `WPFDEVTOOLS_CERT_DIR`。Network paths are not allowed。
- 目前已交付的 transport 是 STDIO 加 named-pipe inspector 通訊；HTTP transport 不在目前二進位發佈範圍內。
