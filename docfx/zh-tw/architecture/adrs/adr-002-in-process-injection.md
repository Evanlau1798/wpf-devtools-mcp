# ADR-002：採用 In-Process Injection

## 狀態

Accepted。

## 背景

WPF 中許多最有價值的診斷 API，例如 binding introspection、dependency property source 與 template-aware tree traversal，都需要在目標行程內執行。

## 決策

server 會在目標 WPF 行程中啟動 in-process inspector，而不是只依賴 out-of-process UI automation。

## 選擇原因

- 可存取 out-of-process 工具拿不到的 WPF API
- 更適合做深度診斷與 AI 驅動的分析
- 能取得完整 dispatcher 與 visual tree 脈絡

## 影響

- 需要更謹慎地處理安全性與相容性
- 必須清楚記錄 runtime 與 architecture 限制

更多內容請參考[Bootstrap 與 Injection](../../production/bootstrap-and-injection.md)。
