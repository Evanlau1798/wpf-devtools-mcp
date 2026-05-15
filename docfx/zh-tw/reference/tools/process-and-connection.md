# 程序與連線工具

## 最重要的工具

- `get_processes`
- `select_active_process`
- `get_active_process`
- `connect`
- `ping`

## 什麼時候用哪一個

- 一般情況先用 `connect()`。它會自動發現單一可見的 WPF 目標並直接建立連線。
- 當 hidden 或 background 的 WPF 視窗也必須參與 auto-discovery，但你不想先多做一次 process listing 時，使用 `connect(windowFilter='all')`。
- 當你預期同時有多個 WPF target，且你是有意識地要直接挑選最大 working set 候選者時，使用 `connect(selectionStrategy='largest_working_set', windowFilter='all')`，而不是先 list 再 connect。
- 當 auto-discovery 有歧義、你想先看架構/權限資訊，或你需要把背景目標也列出來時，再用 `get_processes(windowFilter)`。
- 當你已明確知道要連哪個程序時，用 `connect(processId)` 為指定程序建立 live inspector session。
- 當你已連上多個目標，且後續工具想省略 `processId` 時，使用 `select_active_process` 明確指定預設目標。
- 在省略 `processId` 前，用 `get_active_process` 確認目前 active selection 是否正確。
- 只有在需要明確健康檢查或 reconnect 驗證時，才呼叫 `ping`。

## 重要行為

- `get_processes` 會回傳 `isElevated`、`requiresElevationToConnect` 與 `canConnectFromCurrentServer`
- `connect()` 預設會對單一可見 WPF 目標做 auto-discovery；若找到多個目標，會回傳候選清單而不是隨機連線
- `connect` 會驗證目標、解析 bootstrapper 候選項，並在目前 server 權限不足時提早阻擋
- 同一個 `SessionManager` 與 `processId` 的並行 `connect` 會共享同一個 in-flight operation，而不是重複啟動 injection。單一 caller cancellation 只會停止該 caller 等待；只要還有其他 waiter，shared operation 會繼續；如果最後一個 waiter 也取消，shared operation 會被取消。完成後的 single-flight operation 會被移除；後續呼叫若已有 connected session 會回傳 `AlreadyConnected`，否則會開始新的 connect 嘗試。
- connect 成功後，優先使用 `get_ui_summary`、`get_element_snapshot` 或 `get_form_summary` 建立 scene-first 上下文，再決定是否真的需要 tree-heavy follow-up。
- `select_active_process` 只接受已成功建立 session 的程序
- `get_active_process` 會顯示目前是否已有 active selection，以及它是在何時被選擇
- `ping` 是快速存活檢查，不會取代 `connect`

## 實際工作流程

```text
connect -> get_ui_summary -> get_element_snapshot
```

```text
connect -> get_ui_summary -> get_form_summary
```

```text
connect(windowFilter='all') -> get_ui_summary -> get_element_snapshot
```

```text
connect(selectionStrategy='largest_working_set', windowFilter='all') -> get_ui_summary -> get_form_summary
```

```text
connect -> get_ui_summary -> find_elements -> get_visual_tree
```

```text
connect -> MultipleWpfProcessesFound -> get_processes(windowFilter) -> connect(processId)
```

例外情境的 discovery 路徑：

```text
get_processes(windowFilter) -> connect(processId) -> select_active_process -> get_active_process -> get_ui_summary
```

如果以上流程失敗，請先解決程序或 session 問題，再呼叫其他依賴程序狀態的工具。
