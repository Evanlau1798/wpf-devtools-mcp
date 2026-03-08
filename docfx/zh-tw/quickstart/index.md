# 五分鐘完成安裝

這份指南以「最短成功路徑」為目標：建置 managed server、建置與目標架構相符的 native bootstrapper、啟動內建測試 WPF 應用、註冊 MCP server，最後驗證 `get_processes`、`connect` 與 `ping`。

## 先決條件

- Windows 10 以上。
- .NET SDK 8.0 以上。
- Visual Studio 2022 或 Build Tools，並安裝：
  - .NET desktop development
  - Desktop development with C++
- 目標 WPF 行程必須與 MCP server 由同一個使用者帳號啟動。

## 先記住最重要的架構規則

只有當 bootstrapper 的架構與目標行程架構一致時，`connect` 才會成功。

- 大多數現代桌面 WPF 應用請使用 **x64**。
- 如果目標行程是 32-bit，請使用 **Win32/x86**。
- **ARM64** 僅適用於原生 ARM64 的 WPF 應用。

如果你不確定，先啟動 server，呼叫 `get_processes`，以它回傳的 architecture 當成唯一真相來源。

## 步驟 1：Clone 並還原工具

```powershell
git clone <your-fork-or-repo-url>
cd wpf-devtools-mcp
dotnet tool restore
```

## 步驟 2：建置 managed 專案

典型的 x64 開發環境可直接使用：

```powershell
dotnet build WpfDevTools.sln -c Debug -p:Platform=x64
```

> 本機開發建議使用 `Debug`。Debug build 會對受信任的本機路徑放寬 DLL 簽章驗證，讓第一次啟動不需要先完成程式碼簽署。

## 步驟 3：建置 native bootstrapper

若目標是 x64：

```powershell
msbuild src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj /p:Configuration=Debug /p:Platform=x64
```

若目標是 x86：

```powershell
msbuild src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj /p:Configuration=Debug /p:Platform=Win32
```

若目標是 ARM64：

```powershell
msbuild src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj /p:Configuration=Debug /p:Platform=ARM64
```

bootstrapper 產物會被複製到 `artifacts/bootstrapper/<Configuration>/<Platform>/`，server 也會從候選搜尋路徑自動解析這些輸出。

## 步驟 4：啟動範例 WPF 應用

```powershell
dotnet run --project tests/WpfDevTools.Tests.TestApp --no-build
```

讓這個行程保持執行。

## 步驟 5：啟動 MCP server

在第二個終端機視窗中執行：

```powershell
dotnet run --project src/WpfDevTools.Mcp.Server --no-build
```

server 使用 STDIO transport，因此包裹這個行程的任何腳本都不應把額外日誌輸出到 `stdout`。

## 步驟 6：在 MCP client 中註冊 server

依照你的客戶端選擇其中一份指南：

- [Claude Desktop 設定](claude-desktop.md)
- [Cursor 與 VS Code 設定](cursor-vscode.md)

## 步驟 7：驗證第一個 session

在你的 MCP client 中依序呼叫：

1. `get_processes`
2. `connect`
3. `ping`
4. `get_visual_tree`

健康的第一次互動通常會長這樣：

- `get_processes` 能列出 `WpfDevTools.Tests.TestApp`
- `connect` 回傳成功並表示 session 已建立
- `ping` 可以快速回應
- `get_visual_tree` 回傳 root window 與其子元素

## 給 AI client 的最快可用提示詞

```text
列出 WPF processes，連線到測試應用程式，執行 ping，然後顯示 visual tree 的前兩層。
```

## 如果 `connect` 失敗

請依序檢查：

1. 目標應用程式仍在執行，而且確實是 WPF。
2. MCP server 與目標行程的架構一致。
3. 對應架構的 native bootstrapper 已經建置。
4. 本機未簽署開發使用 `Debug`，生產環境使用已簽署的 `Release`。
5. 目標行程沒有被政策、防毒軟體或權限不足所阻擋。

下一步請閱讀：[AI Agent 使用指南](../guides/ai-agent-guide.md)
