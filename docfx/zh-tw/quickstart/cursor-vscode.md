# Cursor、VS Code 與 Visual Studio 快速開始

Cursor、VS Code 與 Visual Studio 最適合直接套用 installer 產生的 JSON 設定。

## 1. 安裝 WPF DevTools

> **公開端點狀態：** Public release endpoints are not yet anonymously reachable。GitHub repository、Releases、latest-release API、raw installer URL 與 installer alias 都通過匿名 smoke check 前，請使用本機產生且已驗證的 release package 或 source checkout，不要執行遠端一行安裝命令。

建議的本機 package 安裝路徑：

1. 先審查 `scripts/online-installer.ps1`，把它當成正式來源入口。
2. 使用已審查的 installer 安裝已驗證的本機 package archive。

範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -Client vscode -NonInteractive -Force -OutputJson
```

package-local 回退路徑：

1. 使用本機產生的 package，或等 public endpoint smoke check 通過後，再從 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) 下載對應架構的 `release_<version>_win-<arch>.zip`、`SHA256SUMS.txt` 與 `release-assets.json`。
2. 解壓前，先用 `SHA256SUMS.txt` 與 `release-assets.json` 驗證 archive。
3. 解壓縮套件。
4. 執行 `run.bat`。

在信任解壓後的套件前，請保留與該 archive 相鄰且已驗證的 release sidecar：`SHA256SUMS.txt` 用於 checksum 驗證，`release-assets.json` 用於 canonical release metadata。如果解壓後的套件旁已沒有原始且已驗證的 archive 與 sidecar，請在執行 `run.bat` 前設定 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` 作為必要的 thumbprint trust root；`WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` 只能在 thumbprint 已 pin 之後作為 additional constraint。

`run.bat` 會在目前 shell 尚未提升權限時要求 elevation，然後啟動 packaged `bin/install.ps1`。如果你需要把安裝留在目前未提升權限的 shell 中，請設定 `WPFDEVTOOLS_SKIP_ELEVATION=1`。

安裝後，若沒有可沿用的既有 live install root，回退 executable 路徑會是：

```text
C:\Users\<you>\AppData\Roaming\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe
```

## 2. 使用 installer 產生的 JSON artifact

installer 會在 `client-registration\` 內輸出 editor-ready JSON。

### Cursor 全域設定

若你要註冊到使用者層級的 Cursor 設定，請使用 `client-registration\cursor.global.json`，並把 `mcpServers.wpf-devtools` 節點複製到：

```text
%USERPROFILE%\.cursor\mcp.json
```

格式如下：

```json
{
  "mcpServers": {
    "wpf-devtools": {
      "type": "stdio",
      "command": "C:\\Users\\<you>\\AppData\\Roaming\\WpfDevToolsMcp\\<arch>\\current\\bin\\wpf-devtools-<arch>.exe",
      "args": []
    }
  }
}
```

### Cursor 專案設定

若你要把 Cursor MCP 設定綁在 repo 內，請使用 `client-registration\cursor.project.json`，並把相同的 `mcpServers.wpf-devtools` 節點複製到：

```text
<repo>\.cursor\mcp.json
```

Cursor editor workflow 與 Cursor CLI 的 MCP workflow 會共用這個 `.cursor\mcp.json` / `%USERPROFILE%\.cursor\mcp.json` 設定格式。請把產生的 `client-registration` artifact 視為解析後 executable 路徑的真源，而不是只依賴上面的範例路徑。

### VS Code / Visual Studio

請使用 `client-registration\vscode.json` 或 `client-registration\visual-studio.json`，格式如下：

```json
{
  "servers": {
    "wpf-devtools": {
      "type": "stdio",
      "command": "C:\\Users\\<you>\\AppData\\Roaming\\WpfDevToolsMcp\\<arch>\\current\\bin\\wpf-devtools-<arch>.exe",
      "args": []
    }
  }
}
```

installer 預設會把 VS Code 註冊寫到 `%APPDATA%\Code\User\mcp.json`，並把 Visual Studio 註冊寫到 `%USERPROFILE%\.mcp.json`。只有在你刻意要用手動的專案層級替代方案時，才使用 `.vscode\mcp.json`。請把產生的 `client-registration` artifact 視為解析後 executable 路徑的真源，而不是只依賴上面的範例路徑。

第一次 workflow 前，請確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含已審查 target 的 exact local absolute executable path；未設定或 malformed value 會在 `connect` attach 前 fail closed。

## 3. 第一個實用流程

1. 執行 `connect()`。
2. 如果 client 有暴露 prompts 或 resources，優先使用那些 discovery surfaces，而不是 raw protocol bootstrapping。
3. 若 auto-discovery 回傳多個候選，執行 `get_processes(windowFilter)` 並重新執行 `connect(processId)`。
4. 執行 `get_ui_summary(depthMode: "semantic")`。
5. 優先遵循 `navigation.recommended`，若 client 尚未呈現 navigation，則把 `nextSteps` 當成相容欄位。
6. 只有在仍需要更深層結構時才使用 `get_visual_tree`，或在已取得具體 `elementId` 後使用 `get_element_snapshot(elementId)`。

## 注意事項

- Cursor 使用 `mcpServers`；VS Code 與 Visual Studio 使用 `servers`。
- 若切換架構，請重新註冊或刷新 editor 端設定。
- Cursor 的 global 與 project scope 可以同時存在，但每個 scope 內只建議保留一個 `wpf-devtools` entry。
- 若切換架構，請重新註冊已安裝的 executable。
- 避免讓編輯器外層 wrapper 把額外訊息寫入 `stdout`。
- 在 editor-driven workflow 中，先用 scene-level 摘要，再展開 tree。
- 如果工具結果已包含 `navigation.recommended` 或 `nextSteps`，那個執行期 guidance 會比固定手冊流程更可靠。
