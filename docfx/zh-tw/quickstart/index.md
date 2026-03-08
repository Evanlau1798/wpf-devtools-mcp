# 5 分鐘快速安裝

這份 quickstart 以公開安裝流程為主：下載發佈版 release package、執行內附的 `install.ps1`、將已安裝的執行檔註冊到 MCP client，最後再對實際執行中的 WPF 應用程式進行驗證。

## 先備條件

- Windows 10 以上。
- 一個正在執行中的 WPF 應用程式，且與 MCP server 使用相同使用者帳號。
- 選擇與目標 process 相同位元架構的套件：`x64`、`x86` 或 `arm64`。

## 先記住 architecture 規則

只有在 server process architecture 與 bootstrapper architecture 都和 target process 相符時，`connect` 才會成功。

- `x64` target -> 安裝並執行 `x64` package。
- `x86` target -> 安裝並執行 `x86` package。
- `arm64` target -> 安裝並執行 `arm64` package。

## Step 1：下載正式發佈的 release package

先到 Releases 頁面下載與目標應用程式架構一致的套件：

- Repository: [https://github.com/Evanlau1798/wpf-devtools-mcp](https://github.com/Evanlau1798/wpf-devtools-mcp)
- Releases: [https://github.com/Evanlau1798/wpf-devtools-mcp/releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases)

預期的套件資料夾名稱如下：

- `WpfDevTools-win-x64`
- `WpfDevTools-win-x86`
- `WpfDevTools-win-arm64`

下載後請先解壓縮到本機資料夾，再執行安裝。

## Step 2：執行 package-local installer

進入解壓後的 package 資料夾，直接執行內附的 `install.ps1`。

```powershell
Set-Location C:\path\to\WpfDevTools-win-x64
.\install.ps1 -Force
```

這個 package-local flow 會直接使用同一資料夾中的 `manifest.json`、`WpfDevTools.Mcp.Server.exe` 與完整 release 內容，因此在一般公開安裝情境下，不需要額外傳入 `-PackagePath`。

安裝完成後，預設執行檔路徑通常會是：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## Step 3：註冊已安裝的執行檔

安裝器會自動產生可直接複製貼上的註冊命令，位置如下：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\client-registration\
```

如果你想手動註冊，請直接指向 `WpfDevTools.Mcp.Server.exe`。

## Step 4：啟動或保持你的 WPF 目標應用程式

這個 server 只能檢查正在執行中的 WPF process，因此請先啟動你的 app，再啟動或註冊 MCP client。

## Step 5：驗證第一個 session

在 MCP client 中依序執行：

1. `get_processes`
2. `connect`
3. `ping`
4. `get_visual_tree`

健康的首次驗證訊號如下：

- `get_processes` 能列出目標 WPF process。
- `connect` 成功，且沒有 architecture mismatch。
- `ping` 能快速回應。
- `get_visual_tree` 能回傳 root window 與子元素。

## 給 AI client 的最快實用提示詞

```text
List WPF processes, connect to the target app, ping it, and summarize the first two levels of the visual tree.
```

## 想使用 source-based setup？

如果你正在貢獻此 repository，或需要直接除錯 server 本體，請改看 contributor setup，而不是公開安裝流程。

下一步：[AI Agent Client 指南](ai-agent-clients.md)
