# Binding 與 Dependency Property 工具

## Binding 診斷

關鍵工具：

- `get_bindings`
- `get_binding_errors`
- `get_binding_value_chain`
- `get_datacontext_chain`
- `force_binding_update`

當 UI 看起來不對，但 tree 本身仍正常時，這組工具通常是最快縮小問題的方式。

## Dependency Property 分析

關鍵工具：

- `get_dp_value_source`
- `get_dp_metadata`
- `set_dp_value`
- `clear_dp_value`
- `watch_dp_changes`

它們能用來解釋 precedence、local values、styles、inheritance、triggers 與 metadata。

## Mutation 警告

`set_dp_value` 與 `clear_dp_value` 會直接修改執行中的應用程式。每次 mutation 後，都應緊接著做一次 verification 呼叫。
