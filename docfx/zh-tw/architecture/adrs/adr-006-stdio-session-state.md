# ADR-006: STDIO 單 session 邊界

## 狀態

已接受。

## 背景

目前正式發佈的 MCP server 是由 MCP client 啟動的本機 STDIO child process。MCP 與 C# SDK 官方文件將 STDIO 描述為本機 child-process 整合模型，而 Streamable HTTP 與 legacy SSE 則是另一類 HTTP transport，較適合遠端或服務化部署。

因此，現階段實作允許部分狀態停留在 process 或 host scope：

- `ToolCallHelper` 持有 shared tool wrapper、response shaping helper、navigation planner default 與 host-keyed tool cache。
- `MetricsCollector` 儲存目前 server process 的 method metrics。
- `SessionManager` 持有 active target process、process session、cleanup timer 與 rate-limit state。
- `SessionNavigationStateStore` 在 `SessionManager` 後方追蹤 per-target snapshot 與 active trace navigation state。
- `ToolNavigationPlanner` 與 navigation catalog 目前是此 server process 內所有 tool call 共用的 guidance。
- `McpToolExecutionPolicy` 集中評估 destructive tools、screenshots 與 ViewModel inspection 的 policy profile。

這個模型只在一個 STDIO server process 對應一個 MCP client session 時成立。HTTP transport 可能讓多個 client、request 或 resumable event stream 共用同一個 process；若直接沿用這些全域狀態，可能造成 active process selection、metrics、navigation context、cached tool instance 或 policy decision 跨 client 洩漏。

## 決策

在 HTTP transport 工作明確替換或正確 scope 下列狀態以前，public server 維持 STDIO single-session semantics。

任何 Streamable HTTP 或 SSE server endpoint 發佈前，都必須通過 release gate，確認下列項目已移入 DI/request/session scope：

- tool instance cache 與任何 `ToolCallHelper` override state
- method metrics 與 rate-limit counters
- `SessionManager` 的 active process/session state
- `SessionNavigationStateStore` 的 snapshot、active trace 與 navigation state
- `ToolNavigationPlanner` 的 request context 與 navigation catalog access
- 目前集中在 `McpToolExecutionPolicy` 的 policy profile evaluation

該 gate 必須包含在同一個 server process 內執行兩個獨立 logical clients 的測試，證明 process selection、policy settings、navigation state、metrics 與 cached tool instances 不會跨 client 邊界。

## 後果

- 目前 static state 對正式發佈的 STDIO package 仍可接受。
- 未來 HTTP 工作必須先處理 session-bound service registration，不能只替換 transport。
- 任何未來 `MapMcp`、Streamable HTTP 或 SSE entrypoint 若仍沿用 STDIO-only process globals，必須在 review 階段被擋下。
- 文件、冒煙測試 與 production readiness review 必須把 session isolation 視為 release gate，而不是 transport launch 後的 cleanup task。

## 參考

- MCP server guide: https://modelcontextprotocol.io/docs/develop/build-server
- MCP C# SDK transports: https://csharp.sdk.modelcontextprotocol.io/concepts/transports/transports.html
