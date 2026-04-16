# Binding 與 Dependency Property 工具

## Binding 診斷

主要工具：

- `get_affected_elements`
- `get_bindings`
- `get_binding_errors`
- `get_binding_mismatches`
- `get_binding_value_chain`
- `get_datacontext_chain`
- `force_binding_update`

當 UI 看起來不對，但 tree 本身仍正常時，這一組工具通常是最快的診斷入口。

如果你已經知道可能出問題的 binding path 或 `DataContext` property，且只想先做低成本候選掃描，請先使用 `get_affected_elements`，再決定是否要做大範圍 recursive binding inspection。

`get_binding_errors` 預設採用 `compact=true`，會從主要 `errors` array 中移除冗長的逐筆訊息文字。只有在你真的需要完整 message payload 進行人工除錯時，才改傳 `compact=false`。

如果你已經知道下一步是什麼，具備額外 optional args 傳遞能力的 client 可在 `get_binding_errors` 呼叫上傳入 `navigation=false`，即可省略該次回應的 `nextSteps` 與 `navigation`；schema-driven client 可以在這個工具上依賴這個 opt-out，因為它今天已經公告在 tool schema 中，且不應假設其他 diagnostic 工具也已公開支援它。

當 binding path 已經能解析，但值仍然看起來不合理，例如型別不相容、nullability 衝突、或 converter 造成的問題時，請優先使用 `get_binding_mismatches`。

`get_affected_elements` 的設計刻意保守。下列情況會被分流到 `unsupportedElements`，並附帶 `unsupportedReason`，而不是混進支援的候選集合：

- 使用 `ElementName`、`RelativeSource` 或 explicit `Source`
- 沒有可用的 `DataContext` 鏈
- 無法證明目前仍依賴指定 path 的 element

## Dependency Property 分析

主要工具：

- `get_dp_value_source`
- `get_dp_metadata`
- `set_dp_value`
- `clear_dp_value`
- `watch_dp_changes`
- `wait_for_dp_change`

這一組工具用來解釋 precedence、local values、styles、inheritance、triggers 與 metadata。

在 STDIO transport 下，若 `watch_dp_changes` 只能完成註冊而無法推送即時事件，請改用 `wait_for_dp_change`。它提供 polling-based、可設定 timeout 的等待流程，更適合 agent workflow。

如果你需要在 mutation、interaction 或 watcher 註冊後，明確讀出 buffered `DpChange`、`BindingError` 或 validation event，請使用 `drain_events`。

## Mutation 注意事項

`set_dp_value` 與 `clear_dp_value` 會直接修改執行中的應用程式。每次 mutation 之後，都應搭配 verification 呼叫確認結果，例如 `get_state_diff`、`get_dp_value_source` 或 `drain_events`。
