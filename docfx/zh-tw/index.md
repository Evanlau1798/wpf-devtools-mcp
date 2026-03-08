# WPF DevTools MCP Server 文件（繁體中文）

[English version](../index.md)

WPF DevTools MCP Server 是一個僅支援 Windows 的 Model Context Protocol 伺服器。它透過注入到目標行程內的 inspector，讓 AI agent 可以直接檢視並操作執行中的 WPF 應用程式，適用於 UI Automation 不夠用的場景，例如 Binding 診斷、Dependency Property 優先順序分析、Visual Tree 檢視、MVVM 狀態檢查、Routed Event 追蹤、Layout 偵錯與受控 UI 互動。

## 選擇你的閱讀路徑

| 我想要... | 從這裡開始 |
| --- | --- |
| 五分鐘內完成安裝與第一次連線 | [五分鐘完成安裝](quickstart/index.md) |
| 在 Claude Desktop 使用這個 MCP Server | [Claude Desktop 設定](quickstart/claude-desktop.md) |
| 在 Cursor 或 VS Code 使用這個 MCP Server | [Cursor 與 VS Code 設定](quickstart/cursor-vscode.md) |
| 了解 AI agent 應如何安全地使用這些工具 | [AI Agent 使用指南](guides/ai-agent-guide.md) |
| 以生產環境等級部署並加固安全性 | [安全模型](production/security.md) |
| 搞懂 runtime、bootstrapper 與架構限制 | [Bootstrap 與 Injection](production/bootstrap-and-injection.md) |
| 參與開發、測試或文件貢獻 | [貢獻指南](contributors/index.md) |

## 這個專案的差異化能力

- **WPF 原生可見性**：可直接檢查 `BindingOperations`、Dependency Property value source、namescope、template、routed events 與 layout 細節，這些通常不是 out-of-process 工具能穩定取得的資訊。
- **AI-Friendly 契約**：工具描述、參數與回應形狀以 MCP 與實作行為為準，並明確記錄建議工作流程與常見反模式。
- **生產環境加固**：目前程式碼已包含 DLL 驗證、選用式 HMAC 驗證、選用式 named pipe TLS、pipe ACL 限制與有界請求處理。
- **可驗證的真實流程**：倉庫中包含單元測試、整合測試，以及對測試應用程式執行所有已交付工具的 live MCP smoke harness。

## 目前可以做到的事情

- 探索執行中的 WPF 行程並建立 session。
- 瀏覽 visual tree、logical tree、namescope 與 template tree。
- 診斷 binding 錯誤、檢查 binding chain、重新觸發 binding 更新。
- 分析 dependency property 的有效值來源、metadata、style setter 與資源解析鏈。
- 進行受控互動，例如點擊、滾動、鍵盤模擬、螢幕擷取與受控 drag/drop。
- 檢查 layout、clipping、routed events、MVVM 指令與效能診斷資訊。

## 目前範圍與邊界

- **Transport**：目前發佈版本使用 STDIO 作為 MCP transport。
- **平台**：僅支援 Windows。
- **目標 UI 技術**：僅支援 WPF。
- **注入模型**：native bootstrapper 加上 managed inspector。
- **安全邊界**：驗證與 TLS 為選用功能；Debug 與 Release 的 DLL 驗證規則不同。

## 架構速覽

```text
AI Client（Claude Desktop / Cursor / VS Code）
  -> MCP over STDIO
MCP Server（net8.0）
  -> named pipes with JSON messages and length-prefix framing
Native bootstrapper + managed inspector
  -> WPF Dispatcher and in-process APIs
Target WPF application
```

請參考[架構總覽](architecture/overview.md)了解完整資料流，並閱讀[ADR 索引](architecture/adrs/index.md)掌握目前設計決策。

## 建議閱讀順序

1. [五分鐘完成安裝](quickstart/index.md)
2. [AI Agent 使用指南](guides/ai-agent-guide.md)
3. [工具總覽](reference/tools/index.md)
4. [安全模型](production/security.md)
5. [Bootstrap 與 Injection](production/bootstrap-and-injection.md)

## 語言說明

- 本區為人工整理的繁體中文版，內容以目前已交付程式碼為準。
- 自動產生的 API 參考頁目前仍以英文為主，以避免型別名稱與 XML 文件產生額外歧義。
