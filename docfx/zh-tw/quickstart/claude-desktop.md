# Claude Desktop 快速開始

Claude Desktop 使用靜態 JSON 設定檔，因此最乾淨的做法是直接複製 installer 產生的 JSON。

## 1. 安裝 WPF DevTools

> **公開端點狀態：** Public release endpoints are not yet anonymously reachable。GitHub repository、Releases、latest-release API、raw installer URL 與 installer alias 都通過匿名 smoke check 前，請使用本機產生且已驗證的 release package 或 source checkout，不要執行遠端一行安裝命令。

建議的本機 package 安裝路徑：

1. 先審查 `scripts/online-installer.ps1`，把它當成正式來源入口。
2. 使用已審查的 installer 安裝已驗證的本機 package archive。

範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -Client claude-desktop -NonInteractive -Force -OutputJson
```

package-local 回退路徑：

1. 使用本機產生的 package，或等 public endpoint smoke check 通過後，再從 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) 下載對應架構的 `release_<version>_win-<arch>.zip`、`SHA256SUMS.txt`、`release-assets.json` 與 `release-sbom.spdx.json`。
2. 解壓前，先用 `SHA256SUMS.txt`、`release-assets.json` 與 `release-sbom.spdx.json` 驗證 archive。
3. 解壓縮套件。
4. 執行 `run.bat`。

在信任解壓後的套件前，請保留與該 archive 相鄰且已驗證的 release sidecar：`SHA256SUMS.txt` 用於 checksum 驗證，`release-assets.json` 用於 canonical release metadata，`release-sbom.spdx.json` 用於 release asset SBOM。SBOM sidecar 是 asset-level release archive inventory，not a full package/dependency SBOM。Production payload signature verification 仍需要獨立的 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`；相鄰 sidecar 只能證明 archive provenance，不能取代 signer trust。`WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` 只能在 thumbprint 已 pin 之後作為 additional constraint。

`run.bat` 會在目前 shell 尚未提升權限時要求 elevation，然後啟動 packaged `bin/install.ps1`。如果你需要把安裝留在目前未提升權限的 shell 中，請設定 `WPFDEVTOOLS_SKIP_ELEVATION=1`。

安裝後，若沒有可沿用的既有 live install root，回退 executable 路徑會是：

```text
C:\Users\<you>\AppData\Roaming\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe
```

## 2. 使用 installer 產生的 JSON

installer 會輸出 `client-registration\claude-desktop.json`，格式如下：

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

請把 installer 產生的 `client-registration\claude-desktop.json` 視為解析後 executable 路徑的真源。只有在你刻意切換架構或 install root 時，才需要更新本機 `claude_desktop_config.json` 內複製出的路徑。

以下保留英文，是為了方便直接貼給 client：

使用此 prompt 前，請確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含執行中 WPF app 的 exact local absolute executable path；未設定或 malformed value 會在 `connect` attach 前 fail closed。

## 3. 第一個 prompt

以下保留英文，是為了方便直接貼給 client：

```text
Use the WPF DevTools MCP server after WPFDEVTOOLS_MCP_ALLOWED_TARGETS includes the running WPF app's exact local absolute executable path; connect to it, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## 注意事項

- `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含已審查 target 的 exact local absolute executable path 後，一般情況先從 `connect()` 開始；只有 auto-discovery 回報多個候選時，才使用 `get_processes(windowFilter)`。
- 在展開 visual tree 前，優先做 scene-level 驗證。
- 每次診斷、互動或 mutation 後，優先遵循 `navigation.recommended`，並把 `nextSteps` 視為相容欄位。
- mutation 工具請放到較後面的工作流再使用。
- 如果切換 `x64`、`x86` 或 `arm64`，請重新安裝或重新註冊。
