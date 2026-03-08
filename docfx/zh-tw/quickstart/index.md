# 5 分鐘快速開始

這份指南走最短成功路徑：建置 managed server、建置符合目標架構的 native bootstrapper、啟動內建測試 WPF App、把 MCP server 註冊到您的 client，最後驗證 `get_processes`、`connect` 與 `ping`。

## 先決條件

- Windows 10 以上。
- .NET SDK 8.0 以上。
- Visual Studio 2022 或 Build Tools，且已安裝：
  - .NET desktop development
  - Desktop development with C++
- 一個與 MCP server 使用同一個使用者帳號執行中的 WPF 目標程式。

## 先記住 architecture 規則

只有當 bootstrapper architecture 與 target process architecture 一致時，`connect` 才會成功。

- 多數現代桌面 WPF App 請使用 **x64**。
- 如果目標程式是 32-bit，請使用 **Win32/x86**。
- 只有在目標是原生 ARM64 WPF App 時才使用 **ARM64**。

如果您不確定，請先啟動 server，呼叫 `get_processes`，以回傳的 architecture 為準。

## 步驟 1：Clone 與還原工具

```powershell
git clone <your-fork-or-repo-url>
cd wpf-devtools-mcp
dotnet tool restore
```

## 步驟 2：建置 managed 專案

典型 x64 開發環境：

```powershell
dotnet build WpfDevTools.sln -c Debug -p:Platform=x64
```

> 本機開發建議使用 `Debug` build。`Debug` 版本會對 trusted local path 放寬 DLL signature 檢查，讓首次 setup 不需要先完成 code signing。

## 步驟 3：建置 native bootstrapper

x64：

```powershell
msbuild src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj /p:Configuration=Debug /p:Platform=x64
```

x86：

```powershell
msbuild src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj /p:Configuration=Debug /p:Platform=Win32
```

ARM64：

```powershell
msbuild src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj /p:Configuration=Debug /p:Platform=ARM64
```

bootstrapper 輸出會放到 `artifacts/bootstrapper/<Configuration>/<Platform>/`，server 也會從候選路徑中自動解析它。

## 步驟 4：啟動範例 WPF App

```powershell
dotnet run --project tests/WpfDevTools.Tests.TestApp --no-build
```

請保持這個程式持續執行。

## 步驟 5：啟動 MCP server

開第二個終端機：

```powershell
dotnet run --project src/WpfDevTools.Mcp.Server --no-build
```

這個 server 使用 STDIO transport，所以不要讓外層 wrapper 對 `stdout` 輸出其他文字。

## 步驟 6：在您的 MCP client 中註冊 server

請依您使用的 client 選擇下列指南：

- [AI Agent Client 總覽](ai-agent-clients.md)
- [Claude Code 快速開始](claude-code.md)
- [OpenAI Codex 與 Codex CLI 快速開始](openai-codex.md)
- [Claude Desktop 快速開始](claude-desktop.md)
- [Cursor 與 VS Code 快速開始](cursor-vscode.md)

## 步驟 7：驗證第一個 session

在 MCP client 內依序執行：

1. `get_processes`
2. `connect`
3. `ping`
4. `get_visual_tree`

第一次健康驗證通常會看到：

- `get_processes` 列出 `WpfDevTools.Tests.TestApp`
- `connect` 成功並建立 session
- `ping` 很快回應
- `get_visual_tree` 回傳 root window 與子元素

## 最快可用的 AI prompt

```text
List WPF processes, connect to the test app, ping it, and show me the top two levels of the visual tree.
```

## 如果 `connect` 失敗

請依序檢查：

1. 目標程式仍在執行，而且確實是 WPF。
2. MCP server 與 target process 的 architecture 是否一致。
3. 對應 architecture 的 native bootstrapper 是否已建置。
4. 本機未簽章開發是否使用 `Debug` build；正式環境是否使用已簽章的 `Release` build。
5. 目標程式是否被權限、政策或防毒軟體阻擋。

下一步：閱讀 [AI Agent 使用指南](../guides/ai-agent-guide.md)
