# 架構總覽

本專案使用四層式設計：

```text
AI Client
  -> MCP over STDIO
MCP Server
  -> named pipes
Native bootstrapper + managed inspector
  -> WPF Dispatcher and in-process APIs
Target WPF application
```

## 資料流

MCP 端透過官方 C# SDK 使用 STDIO。`connect()` 附加或重用 target-side host 之後，Inspector 會透過 Named Pipes 與 MCP Server 通訊，並使用帶有 4-byte little-endian length prefix 的自訂 length-prefixed JSON request/response messages。

## 為什麼需要這種架構

像是 binding introspection、dependency property precedence、template-aware tree analysis 這類 WPF 檢查能力，都必須在行程內執行。這也是為什麼設計上刻意採用 injected inspector，而不是只依賴 out-of-process UI automation。

當你擁有 target application 時，prefer SDK-hosted reuse。raw injection remains the fallback path for zero-instrumentation diagnostics，以及無法修改的 target。

## 主要元件

- **WpfDevTools.Mcp.Server/**：STDIO transport、工具路由、session 管理、回應整形
- **WpfDevTools.Injector/**：目標行程驗證、runtime 選擇、bootstrap orchestration
- **WpfDevTools.Bootstrapper/**：切進正確 managed runtime 的 native bridge
- **WpfDevTools.Inspector/**：在目標行程內執行 WPF 分析與互動邏輯
- **WpfDevTools.Inspector.Sdk/**：由擁有 target app source code 的使用者主動啟用的 SDK-hosted Inspector 入口
- **WpfDevTools.Shared/**：IPC 契約、enum、共用 helper 與安全型別

## 實作對照表

| 層級 | 對使用者的角色 | 主要實作位置 | 補充 |
| --- | --- | --- | --- |
| MCP Server | 提供 tools、resources、prompts、安全 gates 與 response navigation | `src/WpfDevTools.Mcp.Server` | MCP client 透過 STDIO 與這一層溝通。 |
| Inspector | 在 target process 內讀取 WPF 原生 runtime state，並執行已核准的互動 | `src/WpfDevTools.Inspector` | 會跑在 target WPF Dispatcher 上，因此可以直接檢查 bindings、templates 與 DependencyProperty precedence。 |
| Injector | 選擇 target runtime path，並協調 raw injection fallback | `src/WpfDevTools.Injector` | 只有 target policy 允許已審查 executable path 後才會使用。 |
| Bootstrapper | 從 native startup 橋接到 managed Inspector runtime | `src/WpfDevTools.Bootstrapper` | Raw injection 需要時，architecture matching 很重要。 |
| SDK-hosted Inspector | 讓你擁有的 app 自行啟動 Inspector | `src/WpfDevTools.Inspector.Sdk` | 自有 production app 優先使用此路徑，因為不需要開啟 raw injection。 |
| Shared contracts | 維持 IPC contracts、common enums、framing helpers 與安全型別 | `src/WpfDevTools.Shared` | Public API docs 只從被選定的 shared/SDK projects 產生。 |

## 選擇 runtime path

| 情境 | 優先選擇 | 原因 |
| --- | --- | --- |
| 你擁有 WPF app source | SDK-hosted reuse | App 主動啟動 Inspector，不需要開啟 raw injection。 |
| 需要診斷未改動的本機 WPF app | Raw injection fallback | 適合 zero-instrumentation diagnostics，但需要 exact target allowlist 與 matching bootstrapper architecture。 |
| 需要先理解畫面或 binding context | `connect` 後跑 `get_ui_summary` | Scene summary 可在 tree-heavy inspection 前提供語意上下文。 |
| 需要理解 wire response | MCP contract resources | 使用 `wpf://contracts/tools` 與 `wpf://contracts/response`，不要靠手動記憶。 |

相關頁面：[IPC 與通訊協定](ipc.md)、[Injection 與 Runtime 選擇](injection.md)、[安全模型](../production/security.md)、[工具總覽](../reference/tools/index.md) 與 [詞彙表](../reference/glossary.md)。

## 設計目標

- 維持 AI-Friendly 的 MCP 契約。
- 保持低延遲的本機通訊。
- 盡量降低「看似已連線、其實尚未 ready」的假成功狀態。
- 讓 runtime 與架構選擇顯式可見。
- 讓正式發佈的 injection path 預設即為 hardened，而 SDK-host reuse 需要明確協調 transport 設定。
