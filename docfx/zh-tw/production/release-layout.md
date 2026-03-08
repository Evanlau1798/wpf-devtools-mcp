# Release Layout

本頁定義公開發行版與安裝後資料夾的穩定契約。

## 發行包版面

```text
WpfDevTools-win-x64/
  WpfDevTools.Mcp.Server.exe
  WpfDevTools.Mcp.Server.dll
  WpfDevTools.Injector.dll
  WpfDevTools.Shared.dll
  inspectors/
    net8.0-windows/
      WpfDevTools.Inspector.dll
    net48/
      WpfDevTools.Inspector.dll
  bootstrapper/
    x64/
      WpfDevTools.Bootstrapper.x64.dll
  install.ps1
  uninstall.ps1
  manifest.json
```

## 安裝後版面

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\
  current/
    WpfDevTools.Mcp.Server.exe
    WpfDevTools.Mcp.Server.dll
    WpfDevTools.Injector.dll
    WpfDevTools.Shared.dll
    inspectors/
    bootstrapper/
  client-registration/
    claude-code.txt
    codex-cli.txt
    claude-desktop.json
    cursor-vscode.json
  install-manifest.json
```
