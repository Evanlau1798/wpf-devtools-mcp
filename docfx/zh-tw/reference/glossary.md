# 詞彙表

當你在文件中先看到術語、但還沒讀完架構說明時，可以先查這一頁。這裡使用實務導向的短解釋；更完整的契約細節請看各 reference 頁。

## 核心詞彙

| 詞彙 | 意思 |
| --- | --- |
| MCP Server | 透過 STDIO 暴露 WPF 診斷工具的 Model Context Protocol 伺服器行程。 |
| MCP client | 呼叫 MCP tools、讀取 resources、使用 prompts 的應用程式或 Agent。從安全模型來看，client 預設不被信任。 |
| 目標應用程式（target） | 被檢查的 WPF executable。每個 target 都需要 exact local absolute executable path。 |
| Inspector | 在 target app process 內執行的 WPF 元件，會透過 WPF Dispatcher 讀取 WPF 原生狀態。 |
| SDK-hosted Inspector | 由你擁有的應用程式透過 `InspectorSdk.Initialize()` 啟動的 Inspector。生產環境自有 app 優先使用此路徑。 |
| Raw injection | Server 將 Inspector 注入已審查 target process 的 fallback path。預設會被擋下，必須明確允許。 |
| Bootstrapper | Raw injection 時用來載入正確 managed Inspector runtime 的 native bridge。 |
| Named Pipe | MCP Server 與 Inspector host 之間的本機 IPC channel。Request 使用 length-prefixed JSON framing。 |
| HMAC secret | Inspector connection 使用 challenge-response authentication 時的 shared secret。 |
| Named pipes 上的 TLS | Inspector 通訊預設使用的安全 transport layer。 |

## 安全詞彙

| 詞彙 | 意思 |
| --- | --- |
| Allowlist | Server 可檢查或注入的 exact local absolute executable path 清單。Path fragment 或 relative path 不足以通過。 |
| 預設拒絕（fail closed） | 安全預設：policy value 缺漏、false、格式錯誤或語意不明時，server 會擋下操作。 |
| Policy gate | Server-side capability check，會在 process details、UI text、screenshot、ViewModel value 或 mutation 回傳前執行。 |
| Sensitive reads | 可能暴露 UI text、binding values、DependencyProperty values、event payloads、tree/scene summaries 或 runtime state 的讀取操作。 |
| Destructive tools | 會互動或修改執行中 app 的工具，例如 click、ViewModel change、DependencyProperty change、render measurement、snapshot、batch mutation。 |
| ViewModel inspection | 讀取或使用 ViewModel state、commands、DataContext chain 或 ViewModel mutation helpers。 |
| Screenshot gate | `element_screenshot` 回傳 pixel data 或 screenshot resource 前必須啟用的 policy gate。 |

## 回應與工作流程詞彙

| 詞彙 | 意思 |
| --- | --- |
| `structuredContent` | 新版工具回傳的 canonical machine-readable WPF payload。文字輸出只是 compact fallback。 |
| `navigation.recommended` | 工具回應提供的下一步建議。應優先採用，再考慮自行規劃 workflow。 |
| `nextSteps` | 提供給舊版 client 的 compatibility follow-up guidance。新版 client 應優先使用 `navigation` envelope。 |
| `prefetchTools` | 給 schema-aware client 的 advisory tool names，可提前載入可能用到的工具 schema。 |
| `contextRefs` | 回應中的 descriptive JSON reference，不是 executable handle。 |
| Scene summary | `get_ui_summary` 回傳的 compact semantic overview，通常應在大量 tree inspection 前先使用。 |
| Element snapshot | 已取得具體 `elementId` 後，用 `get_element_snapshot(elementId)` 取得的單一元素診斷。 |
| Snapshot | 已核准 mutation 前後用來比較或還原的 runtime state capture。 |
| Rollback | 還原 snapshot，避免診斷 mutation 讓 app 留在被改過的狀態。 |
| `diagnosticKind` | 回應提示欄位，用來說明回傳的是哪一類診斷結果或失敗。 |

## 常用入口

- 新手安裝：[5 分鐘快速開始](../quickstart/index.md)
- Agent workflow：[AI Agent 使用指南](../guides/ai-agent-guide.md)
- 穩定 recipe：[常見工作流程](../guides/common-workflows.md)
- Runtime contracts：[MCP Contracts 與 Navigation](mcp-contracts.md)
- 安全 gates：[安全模型](../production/security.md)
- 錯誤：[錯誤模型](error-model.md)
