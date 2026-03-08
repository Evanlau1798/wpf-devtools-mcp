# 處理程序與連線工具

## `get_processes`

這通常是第一個要呼叫的工具。它會找出候選 WPF 行程，並回報 architecture；這是選擇正確 bootstrapper build 的關鍵資訊。

## `connect`

為指定行程建立 session。

### 重要行為

- 驗證目標行程
- 解析 inspector 與 bootstrapper 候選路徑
- 強制執行 architecture compatibility 檢查
- 執行 bootstrap 與 pipe readiness 驗證
- 只有在 readiness 成功後才建立 session

## `ping`

在 `connect` 之後，以及任何你需要快速確認目前 session 是否仍存活時，都可以使用 `ping`。

## 實務順序

```text
get_processes -> connect -> ping
```

如果這個序列失敗，請先把它解決，再使用任何針對特定目標行程的工具。
