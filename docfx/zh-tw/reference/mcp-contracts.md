# MCP Contracts 與 Navigation

當 MCP client 或 agent 需要 machine-readable runtime contract，而不是 prose example 時，請從這裡開始。

## Discovery 順序

1. 依任務需求啟動 server，並只開啟必要 policy gates。
2. 呼叫 MCP discovery，例如 `initialize`、`tools/list`、`prompts/list` 與 `resources/list`。
3. 讀取 `wpf://contracts/tools`，取得 tool names、categories、parameters、required fields、capability tags 與 policy tags。
4. 讀取 `wpf://contracts/response`，取得 response envelope、compatibility aliases、navigation metadata 與 error recovery fields。
5. 需要 compact capability summary 時，再讀取 `wpf://capabilities`。

不要從舊截圖或舊執行紀錄硬寫 tool arguments。Runtime discovery 才是真源。

## Prompt 與 Resource 名稱

有些 client 會把 MCP prompt 與 resource 顯示成 client-specific shortcut。可攜契約仍應使用標準名稱與 URI：

| Surface | Portable name |
| --- | --- |
| Binding triage prompt | `debug_binding_issue` |
| Capability resource | `wpf://capabilities` |
| Tool contract resource | `wpf://contracts/tools` |
| Response contract resource | `wpf://contracts/response` |

如果 client 顯示 `/mcp__wpf-devtools__debug_binding_issue` 這類 shortcut，notes 與 automation 仍應保留底層 prompt name。

## 優先解析的 Response 欄位

支援 structured payload 的 client 應把 `result.structuredContent` 視為 canonical。`result.content[0].text` 只作為無法呈現 structured payload 的 compact fallback。

當回應包含 `navigation.recommended` 時，請先照它建議的下一步前進。`navigation.alternatives` 適合人工判斷分支，`prefetchTools` 適合漸進載入 schema，`contextRefs` 只提供描述性上下文，不是 executable handle。

`nextSteps` 仍是舊版 client 的 compatibility field，通常由 `navigation.recommended` 推導。

## Navigation Opt-Out

`get_binding_errors` 的 runtime schema 已公告 `navigation=false`。當下一步已很明確且需要降低 token volume 時，agent 可以使用它。

不要對其他工具傳入 `navigation=false`，除非該工具在 `tools/list` schema 中也明確公告此參數。

## 相關頁面

- [工具總覽](tools/index.md)
- [AI Agent 使用指南](../guides/ai-agent-guide.md)
- [錯誤模型](error-model.md)
- [疑難排解](../guides/troubleshooting.md)
