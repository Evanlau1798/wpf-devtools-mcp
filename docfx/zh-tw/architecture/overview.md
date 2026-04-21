# 架構總覽

本專案使用四層式設計：

```text
AI Client
  -> MCP over STDIO
MCP Server
  -> named pipes
Native bootstrapper + managed inspector
  -> WPF Dispatcher and in-process APIs
Target WPF application
```

## 為什麼需要這種架構

像是 binding introspection、dependency property precedence、template-aware tree analysis 這類 WPF 檢查能力，都必須在行程內執行。這也是為什麼設計上刻意採用 injected inspector，而不是只依賴 out-of-process UI automation。

## 主要元件

- **MCP Server**：工具路由、session 管理、回應整形
- **Injector**：目標行程驗證、runtime 選擇、bootstrap orchestration
- **Bootstrapper**：切進正確 managed runtime 的 native bridge
- **Inspector**：在目標行程內執行 WPF 分析與互動邏輯
- **Shared**：IPC 契約、enum、共用 helper 與安全型別

## 設計目標

- 維持 AI-Friendly 的 MCP 契約。
- 保持低延遲的本機通訊。
- 盡量降低「看似已連線、其實尚未 ready」的假成功狀態。
- 讓 runtime 與架構選擇顯式可見。
- 讓正式發佈的 injection path 預設即為 hardened，而 SDK-host reuse 需要明確協調 transport 設定。
