# WPF DevTools MCP Server 繁體中文文件

[English version](../index.md)

WPF DevTools MCP Server 是一個只支援 Windows 的 Model Context Protocol 伺服器，透過將 in-process inspector 注入執行中的 WPF 應用程式，讓 AI agent 能做到 UI Automation 做不到的 WPF 深度診斷與互動，例如 Binding 診斷、Dependency Property 優先順序分析、Visual Tree 檢視、MVVM 狀態分析、Routed Event 追蹤與 Layout 除錯。

## 依需求選擇入口

| 我想要... | 從這裡開始 |
| --- | --- |
| 用繁體中文閱讀完整文件 | [繁體中文文件](index.md) |
| 五分鐘內完成第一次 setup | [5 分鐘快速開始](quickstart/index.md) |
| 從 Claude Code 使用這個 server | [Claude Code 快速開始](quickstart/claude-code.md) |
| 從 OpenAI Codex 或 Codex CLI 使用這個 server | [OpenAI Codex 與 Codex CLI 快速開始](quickstart/openai-codex.md) |
| 先比較各種 AI client 的差異 | [AI Agent Client 總覽](quickstart/ai-agent-clients.md) |
| 從 Claude Desktop 使用這個 server | [Claude Desktop 快速開始](quickstart/claude-desktop.md) |
| 從 Cursor 或 VS Code 使用這個 server | [Cursor 與 VS Code 快速開始](quickstart/cursor-vscode.md) |
| 了解 AI agent 應如何安全使用工具 | [AI Agent 使用指南](guides/ai-agent-guide.md) |
| 了解生產環境安全與部署方式 | [安全模型](production/security.md) |
| 了解 runtime、bootstrapper 與 injection 限制 | [Bootstrap 與 Injection](production/bootstrap-and-injection.md) |
| 參與程式碼、測試或文件貢獻 | [貢獻指南](contributors/index.md) |

## 這個專案的特色

- **WPF 原生可見性**：可以直接檢視 `BindingOperations`、Dependency Property value source、namescope、template、routed event 與 layout 細節，這些都是 out-of-process 工具拿不到的資訊。
- **對 AI agent 友善**：tool metadata 由程式碼維護、structured content 一致、常見工作流與錯誤恢復都有文件化。
- **生產環境硬化**：目前程式碼已包含 DLL 驗證、可選 HMAC 驗證、可選 named pipe TLS、pipe ACL 與有界限的 request handling。
- **已驗證的工作流**：repository 內含 unit tests、integration tests，以及會實際執行全部工具的 live MCP smoke harness。

## 您現在可以做到的事

- 掃描執行中的 WPF process 並建立連線。
- 瀏覽 visual tree、logical tree、namescope 與 template tree。
- 診斷 binding error、檢查 binding chain，並強制更新 binding。
- 分析 dependency property 值來源、metadata、style setter 與 resource lookup。
- 執行受控互動，例如 click、scroll、keyboard、screenshot 與 drag/drop。
- 檢查 layout、clipping、routed event、MVVM command 與 performance 資訊。

## 目前的邊界

- **Transport**：目前正式支援的是 STDIO MCP transport。
- **平台**：僅支援 Windows。
- **目標 UI 技術**：僅支援 WPF。
- **Injection 模型**：native bootstrapper 搭配 managed inspector。
- **安全模型**：authentication 與 TLS 為可選；Debug 與 Release 在 DLL 驗證上有不同策略。

## 架構一覽

```text
AI Client (Claude Code / Codex / Claude Desktop / Cursor / VS Code)
  -> MCP over STDIO
MCP Server (net8.0)
  -> named pipes with JSON messages and length-prefix framing
Native bootstrapper + managed inspector
  -> WPF Dispatcher and in-process APIs
Target WPF application
```

完整資料流請參考 [架構總覽](architecture/overview.md)，設計決策請參考 [ADR 索引](architecture/adrs/index.md)。

## 建議閱讀順序

1. [5 分鐘快速開始](quickstart/index.md)
2. [AI Agent Client 總覽](quickstart/ai-agent-clients.md)
3. [AI Agent 使用指南](guides/ai-agent-guide.md)
4. [工具總覽](reference/tools/index.md)
5. [安全模型](production/security.md)
6. [Bootstrap 與 Injection](production/bootstrap-and-injection.md)
