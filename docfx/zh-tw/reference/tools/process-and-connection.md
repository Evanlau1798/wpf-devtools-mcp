# 程序與連線工具

## 最重要的工具

- `get_processes`
- `select_active_process`
- `get_active_process`
- `connect`
- `ping`

## 什麼時候用哪一個

- 先用 `get_processes` 找出可連線的 WPF 目標、程序架構與權限限制。
- 用 `connect` 為指定程序建立 live inspector session。
- 當你已連上多個目標，且後續工具想省略 `processId` 時，使用 `select_active_process` 明確指定預設目標。
- 在省略 `processId` 前，用 `get_active_process` 確認目前 active selection 是否正確。
- 在 `connect` 之後，或在高成本檢查前，用 `ping` 快速確認 inspector 仍可回應。

## 重要行為

- `get_processes` 會回傳 `isElevated`、`requiresElevationToConnect` 與 `canConnectFromCurrentServer`
- `connect` 會驗證目標、解析 bootstrapper 候選項，並在目前 server 權限不足時提早阻擋
- `select_active_process` 只接受已成功建立 session 的程序
- `get_active_process` 會顯示目前是否已有 active selection，以及它是在何時被選擇
- `ping` 是快速存活檢查，不會取代 `connect`

## 實際工作流程

```text
get_processes -> connect -> ping
```

```text
get_processes -> connect -> select_active_process -> get_active_process -> get_visual_tree
```

如果以上流程失敗，請先解決程序或 session 問題，再呼叫其他依賴程序狀態的工具。
