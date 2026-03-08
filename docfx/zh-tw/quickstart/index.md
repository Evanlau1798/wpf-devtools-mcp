# 5 分鐘快速開始

這份指南改用公開發行版的安裝路徑：先安裝已發佈的 server，再把安裝後的可執行檔註冊到 MCP client，最後對執行中的 WPF 程式做驗證。

## 先決條件

- Windows 10 以上。
- 一個正在執行、且與 server 使用同一帳號的 WPF 目標程式。
- 已確認目標程式的架構：`x64`、`x86` 或 `arm64`。

## 先記住 architecture 規則

只有當 server process architecture 與 bootstrapper architecture 都和 target process 相同時，`connect` 才會成功。

- `x64` 目標 -> 安裝 `x64` 套件。
- `x86` 目標 -> 安裝 `x86` 套件。
- `arm64` 目標 -> 安裝 `arm64` 套件。

## 步驟 1：從發行版安裝

較安全的預設方式：

```powershell
$InstallScript = Join-Path $env:TEMP 'install-wpf-devtools.ps1'
Invoke-WebRequest -Uri 'https://github.com/<OWNER>/<REPO>/releases/latest/download/install.ps1' -OutFile $InstallScript
& $InstallScript -Architecture x64
```

只追求速度的便利模式：

```powershell
irm https://github.com/<OWNER>/<REPO>/releases/latest/download/install.ps1 | iex
```

安裝完成後，預設可執行檔路徑通常是：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## 步驟 2：註冊安裝後的可執行檔

安裝程式會把可直接複製的設定輸出到：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\client-registration\
```

如果要手動設定，請直接讓 client 啟動 `WpfDevTools.Mcp.Server.exe`。

## 步驟 3：啟動或保持 WPF 目標程式執行中

這個 server 只能檢查活的 WPF process，所以請先啟動目標程式。

## 步驟 4：驗證第一個 session

在 MCP client 中依序執行：

1. `get_processes`
2. `connect`
3. `ping`
4. `get_visual_tree`

健康的第一輪驗證通常會看到：

- `get_processes` 列出目標 WPF process
- `connect` 成功，且沒有 architecture mismatch
- `ping` 很快回應
- `get_visual_tree` 回傳 root window 與子元素

下一步：閱讀 [AI Agent Client 總覽](ai-agent-clients.md)
