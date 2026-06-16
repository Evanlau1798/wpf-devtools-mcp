# WPF DevTools MCP Server

需要英文文件？請回到 [English documentation](../index.md)。

WPF DevTools MCP Server 是 Windows-only 的 Model Context Protocol server，用於檢查與操作正在執行的 WPF 應用程式。它提供 visual tree、logical tree、binding、DependencyProperty、routed event、layout、screenshot、MVVM state 與受控 runtime mutation 的 WPF 原生診斷能力。

## 安裝

正式發行版 installer：

```powershell
irm https://installer.wpf-mcptools.evanlau1798.com | iex
```

Installer 會解析所選正式發行版的已審查 online installer，驗證版本化 package metadata，並安裝封裝後的 executable。若要手動安裝，請下載 `release_<version>_win-<arch>.zip`、驗證相鄰 sidecar、解壓 package，並從解壓後資料夾執行 `run.bat`。

手動 production review 前，請把這些檔案與 archive 放在同一層：`SHA256SUMS.txt`、`release-assets.json`、`release-sbom.spdx.json`、`package-sbom.spdx.json` 與 `release-evidence.json`。

`release-sbom.spdx.json` 描述發行版 asset/archive inventory。`package-sbom.spdx.json` 描述 package、相依性、script、assembly 與 payload 內容。兩者都不能取代 signer trust；production payload 簽章驗證仍需要 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`。

## 選擇文件路徑

| 需求 | 起點 |
| --- | --- |
| 快速安裝與第一次連線 | [5 分鐘快速開始](quickstart/index.md) |
| 註冊 MCP client | [AI Agent Client](quickstart/ai-agent-clients.md) |
| 在自己的 app 中 host Inspector | [SDK-Hosted Inspector](quickstart/sdk-hosted-inspector.md) |
| 檢查安全 gate | [安全模型](production/security.md) |
| 部署已審查 package | [部署指南](production/deployment.md) |
| 理解發行版資產 | [發行版配置](production/release-layout.md) |
| 瀏覽工具 | [工具總覽](reference/tools/index.md) |

## 可以做什麼

- 探索並連線到已審查的 WPF target。
- 先使用 `get_ui_summary` 與 `get_form_summary` 取得 scene-level summary。
- 診斷 binding failure、DependencyProperty precedence、template、routed event、layout state 與 focus state。
- 只有在 mutation gate 明確啟用時，才使用 snapshot、diff 與 rollback-oriented workflow。
- 只有在 ViewModel inspection gate 明確啟用時，才檢查 ViewModel state。

## 範圍與邊界

- Transport：STDIO MCP server。
- Platform：只支援 Windows。
- Target UI stack：只支援 WPF。
- Persistence：runtime mutation 不會寫回 XAML。
- Security：process details、UI text、screenshot、ViewModel value 或 mutation operation 回傳前都會先通過 policy gate。
- Transport hardening：injection-based sessions 預設使用持久化的本機 HMAC secret 與 named-pipe TLS。
