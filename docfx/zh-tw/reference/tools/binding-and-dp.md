# Binding 與 Dependency Property 工具

## Binding 診斷

主要工具：

- `get_bindings`
- `get_binding_errors`
- `get_binding_mismatches`
- `get_binding_value_chain`
- `get_datacontext_chain`
- `force_binding_update`

當 UI 看起來不對，但 tree 本身仍正常時，這一組工具通常是最快的診斷入口。

當 binding path 已經能解析，但值仍然看起來不合理，例如型別不相容、nullability 衝突、或 converter 造成的問題時，請優先使用 `get_binding_mismatches`。

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

## Mutation 注意事項

`set_dp_value` 與 `clear_dp_value` 會直接修改執行中的應用程式。每次 mutation 之後，都應搭配 verification 呼叫確認結果。
