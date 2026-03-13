# 程序與連線工具

## 最重要的工具

- `get_processes`
- `select_active_process`
- `get_active_process`
- `connect`
- `ping`

## 什麼時候用哪一個

- 一般情況先用 `connect()`。它會自動發現單一可見的 WPF 目標並直接建立連線。
- 當 auto-discovery 有歧義、你想先看架構/權限資訊，或你需要把背景目標也列出來時，再用 `get_processes(windowFilter)`。
- 當你已明確知道要連哪個程序時，用 `connect(processId)` 為指定程序建立 live inspector session。
- 當你已連上多個目標，且後續工具想省略 `processId` 時，使用 `select_active_process` 明確指定預設目標。
- 在省略 `processId` 前，用 `get_active_process` 確認目前 active selection 是否正確。
- 只有在需要明確健康檢查或 reconnect 驗證時，才呼叫 `ping`。

## 重要行為

- `get_processes` 會回傳 `isElevated`、`requiresElevationToConnect` 與 `canConnectFromCurrentServer`
- `connect()` 預設會對單一可見 WPF 目標做 auto-discovery；若找到多個目標，會回傳候選清單而不是隨機連線
- `connect` 會驗證目標、解析 bootstrapper 候選項，並在目前 server 權限不足時提早阻擋
- `select_active_process` 只接受已成功建立 session 的程序
- `get_active_process` 會顯示目前是否已有 active selection，以及它是在何時被選擇
- `ping` 是快速存活檢查，不會取代 `connect`

## 實際工作流程

```text
connect -> get_visual_tree
```

```text
connect -> get_ui_summary -> get_element_snapshot
```

```text
connect -> MultipleWpfProcessesFound -> get_processes(windowFilter) -> connect(processId)
```

例外情境的 discovery 路徑：

```text
get_processes(windowFilter) -> connect(processId) -> select_active_process -> get_active_process -> get_visual_tree
```

如果以上流程失敗，請先解決程序或 session 問題，再呼叫其他依賴程序狀態的工具。
