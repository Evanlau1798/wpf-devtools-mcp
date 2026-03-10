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

## elevated target 與系統管理員權限不一致

如果目標 WPF app 是以系統管理員權限或其他 elevated 狀態啟動，而 MCP host 不是，通常仍可看到 process，但無法真正控制它。這類情況常會在 `connect`、injection 或後續工具呼叫時出現 `Access denied` 或中文系統上的「存取遭拒」。

建議處理方式：

- 以系統管理員權限重新啟動 Claude Code、Codex 或 MCP host，讓 server 與 target 位於相同完整性等級
- 改用非 elevated 的 target process 重新驗證
- 如果目標 packaging 或部署型態不適合一般 injection 路徑，改用 SDK mode

## project scope 註冊混淆

如果 Claude Code 常常重新找不到 server，建議優先使用安裝時產生的 `client-registration/claude-code.project.mcp.json`，或改用 project scope 註冊命令。project scope 可以降低 shell、repo 與本機設定狀態不一致造成的漂移。

## 不支援的封裝型態與 injection 限制

若目標使用 trimmed deployment、self-contained single-file、native AOT 等不支援的封裝型態，標準 injection 路徑可能無法使用。這時請優先考慮 SDK mode 或改用受支援的桌面封裝方式。
