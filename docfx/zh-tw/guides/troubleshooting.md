# 疑難排解

## `connect` 立即失敗

請先檢查：

- 目標 process 是正在執行的 WPF 應用程式
- server process architecture 與 target process architecture 相同
- 安裝後的版面中存在對應的 native bootstrapper

## architecture mismatch

最常見的修正方式，是切換成與目標 process bitness 一致的安裝包。

- x64 target -> x64 package
- x86 target -> x86 package
- arm64 target -> arm64 package

## missing runtime

如果 server 啟動後立刻結束，請確認對應發行包所需的 .NET runtime 已安裝。

## bootstrapper resolution

如果 `connect` 在找到 process 後仍失敗，請確認安裝資料夾中 `bootstrapper` 與 `inspectors` sidecar 仍與 `WpfDevTools.Mcp.Server.exe` 位於預期位置。
