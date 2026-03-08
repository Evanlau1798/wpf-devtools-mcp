# 5 分鐘快速開始

這份 quickstart 以公開安裝流程為主：執行 GitHub Pages bootstrap installer、讓它下載發佈版 release package、將已安裝的執行檔註冊到 MCP client，最後再對實際執行中的 WPF 應用程式進行驗證。

## 先決條件

- Windows 10 以上。
- 有一個正在執行中的 WPF 應用程式，且與 MCP server 使用同一個使用者帳號。
- 選對與 target process 相同的架構：`x64`、`x86` 或 `arm64`。

## 先記住架構規則

只有在 server process architecture 與 bootstrapper architecture 都和 target process 相符時，`connect` 才會成功。

- `x64` target -> 安裝並使用 `x64` package。
- `x86` target -> 安裝並使用 `x86` package。
- `arm64` target -> 安裝並使用 `arm64` package。

## Step 1：執行一鍵安裝

最快方式：

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

這個指令會從 GitHub Pages 取得靜態 bootstrap script。之後該腳本會下載符合架構的 `WpfDevTools-win-<arch>.zip` release asset，並執行 package 內的 `setup.ps1`。

如果你想固定架構並指定 client，可使用：

```powershell
& ([scriptblock]::Create((irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1))) -Architecture x64 -Clients claude-code -NonInteractive -Force
```

Repository 與 Releases：

- Repository: [https://github.com/Evanlau1798/wpf-devtools-mcp](https://github.com/Evanlau1798/wpf-devtools-mcp)
- Releases: [https://github.com/Evanlau1798/wpf-devtools-mcp/releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases)

## Step 2：如果你不想使用 `irm | iex`

1. 從 Releases 下載 `WpfDevTools-win-x64.zip`、`WpfDevTools-win-x86.zip` 或 `WpfDevTools-win-arm64.zip`。
2. 解壓縮。
3. 在解壓後的資料夾執行 `setup.ps1 -Force`。

解壓後的 package 也包含 included `install.ps1`，可用於較低階的 copy/install 自動化；若只是一般使用者安裝，優先使用 `setup.ps1`。

## Step 3：確認安裝後的執行檔路徑

安裝後的預設執行檔路徑通常是：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## Step 4：註冊已安裝的執行檔

installer 會把可直接複製使用的註冊指令輸出到：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\client-registration\
```

如果你要手動註冊，請直接指向 `WpfDevTools.Mcp.Server.exe`。

## Step 5：先讓 WPF target 保持執行

server 只能檢查正在執行中的 WPF process，所以請先啟動目標應用程式，再啟動 MCP client。

## Step 6：驗證第一個 session

在 MCP client 中依序執行：

1. `get_processes`
2. `connect`
3. `ping`
4. `get_visual_tree`

健康的首次連線應該會看到：

- `get_processes` 能列出目標 WPF process。
- `connect` 成功，且沒有 architecture mismatch。
- `ping` 反應快速。
- `get_visual_tree` 能回傳 root window 與子元素。

## 給 AI client 的第一個實用提示詞

```text
List WPF processes, connect to the target app, ping it, and summarize the first two levels of the visual tree.
```

## 需要 source-based setup 嗎？

如果你是要貢獻程式碼或除錯 server 本身，請改看 contributor setup，而不是公開安裝流程。

下一步：[AI Agent Client 總覽](ai-agent-clients.md)
