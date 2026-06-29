# Agent 使用心得：wpfui Edge-Case E2E

- Agent：GPT-5.5
- 日期：2026-06-30
- 場景：使用 GitHub pre-release 驗證自訂 WPF UI edge-case app
- 測試版本：`v1.0.0-beta.19`

我使用 public online installer 搭配 `-Prerelease` 安裝 WPF DevTools MCP，installer 正確解析 GitHub pre-release `v1.0.0-beta.19` 的 win-x64 package。測試全程使用 scratch install root 內的已安裝 server，沒有使用本 repository 的 source build output 作為受測 server。

目標應用方面，我先在 scratch directory clone `lepoco/wpfui`，記錄 upstream commit 與 MIT license，接著另外建立一個使用 `WPF-UI` NuGet package 的 WPF app。這個 app 刻意接近 Windows 11 Microsoft Store 的介面：左側 navigation、search、hero product area、virtualized catalog cards、account settings form、dialog、flyout、hidden/inactive/off-screen targets、templated controls、routed events、ViewModel commands、validation，以及可安全 mutation 的 targets。

![WPF UI edge-case store window](../../agent-feedback/assets/wpfui-edgecase-2026-06-30/main-window.png)

MCP workflow 對 agent 來說很友善。Runtime discovery 回傳 64 tools，並且可以讀取 `wpf://contracts/tools` 與 `wpf://contracts/response`。`connect()` 自動找到唯一 allowlisted target，而 `get_ui_summary(depthMode="semantic")` 提供足夠的 semantic context，因此不需要一開始就依賴 screenshots 或完整 tree dump。`get_namescope`、`find_elements`、`get_element_snapshot`、`diagnose_visibility` 與 `get_interaction_readiness` 是最有用的定位工具。

Mutation 與 recovery 實務上可行。我在 live app 上使用 `capture_state_snapshot`、`set_dp_value`、`get_state_diff`、`restore_state_snapshot`、`batch_mutate`、`click_element`、`simulate_keyboard`、`execute_command` 與 `modify_viewmodel`。刻意失敗的 batch mutation 回傳 snapshot id 與 rollback guidance，後續 restore 也成功完成。刻意對 hidden element 執行 focus 時，server 回傳 structured `ElementNotLoaded`，而不是讓 session 狀態變得模糊。

![MCP hero element screenshot](../../agent-feedback/assets/wpfui-edgecase-2026-06-30/mcp-hero-screenshot.png)

Screenshots 適合作為證據，而不是主要導航方式。Scene-first calls 先協助找到正確 elements；screenshots 則用來確認 store-style hero region 與 restore 後的 window state。

主觀上，這條 release path 已經適合 autonomous agent validation loop。Agent 主要需要謹慎啟用符合工作流的 gates，特別是 snapshots、commands 或 ViewModel mutation 需要 `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true`。文件與 tool errors 都清楚揭露這個需求。

嚴格驗證結果：PASS。P0、P1、P2 與 P3 數量皆為 0。

GPT-5.5
