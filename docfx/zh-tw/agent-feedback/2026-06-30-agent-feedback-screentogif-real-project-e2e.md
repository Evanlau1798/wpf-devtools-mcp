# Agent 使用心得：ScreenToGif 真實專案端對端測試

- Agent：GPT-5.5 evidence run
- 日期：2026-06-30
- 場景：使用 GitHub pre-release 驗證 NickeManarin/ScreenToGif
- 測試版本：`v1.0.0-beta.19`

> 來源說明：前台 GPT-5.5 xhigh Codex CLI runs 已完成安裝、ScreenToGif build、launch 與 MCP discovery 證據，但在產生最終文字報告前停住。此頁是根據同一個 prerelease 安裝、同一個 ScreenToGif process，以及後續有界 runtime transcript 整理出的 reviewed synthesis。

我使用 public online installer 與 GitHub prerelease assets 安裝 WPF DevTools MCP Server，並對 ScreenToGif 這個真實 WPF 應用程式進行驗證。ScreenToGif 有在地化啟動畫面與自訂 startup actions，比一般 demo app 更接近真實 agent 會遇到的應用。

![ScreenToGif startup window](../../agent-feedback/assets/screentogif-2026-06-30/screentogif-startup.png)

安裝路徑清楚。Installer 正確解析 `v1.0.0-beta.19`，選擇 win-x64 GitHub release asset，產生 `other` 的 artifact-only registration，並回報已安裝 executable path。Executable 目前未簽章，這符合現階段 checksum-only prerelease policy。

主要 setup 摩擦來自目標專案環境。ScreenToGif 目標框架是 `net9.0-windows7.0`，而本 repo 根目錄有自己的 `global.json`。測試時使用 scratch `subst` drive 隔離 ScreenToGif，避免 target build 繼承 parent repo 的 SDK policy，並改用已安裝的 .NET 10 SDK。

隔離後，ScreenToGif 可以 restore、build、launch，且 process 維持 responding。`tools/list` 回傳 exactly 64 tools，contract resources 可以讀取，`connect` 也成功 attach 到正在執行的 ScreenToGif。Scene-first calls 很有幫助：`get_ui_summary`、`find_elements`、`get_element_snapshot`、`diagnose_visibility`，以及 layout/style/template tools，讓我不用先 dump 完整 tree，也能理解 startup buttons。

State safety flow 表現良好。Runtime 使用 snapshot、DependencyProperty title mutation、state diff、restore、batch mutation、wait-after-mutation 與 explicit cleanup。Mutation checks 後 app title 已還原；這對 real-project E2E 很重要，因為測試不應讓目標應用停留在被修改過的狀態。

Screenshot resource flow 也可用。下方截圖由 `element_screenshot(outputMode="file")` 回傳，並透過 screenshot resource readback 取得。

![MCP element screenshot resource](../../agent-feedback/assets/screentogif-2026-06-30/mcp-element-screenshot.png)

Structured recovery guidance 易於理解。刻意查找不存在 element 時回傳 `ElementNotFound` 與後續建議；對沒有 binding 的 property 執行 force binding update 時回傳 `InvalidArgument`，並提示先呼叫 `get_bindings`。這些錯誤是可讀且可恢復的，而不是死路。

本輪沒有產生 GIF artifact。驅動 ScreenToGif recorder/export path 需要更侵入性的 desktop capture 與額外 UI state transitions；以 release validation 角度，本輪較安全且更有價值的訊號，是 MCP server 可以透過 public prerelease 安裝、attach 真實 app、讀取 WPF runtime state、取得 screenshot resource，並在 mutation 後還原狀態。

結論：MCP server PASS。沒有發現 P0-P2 MCP product findings。剩餘摩擦屬於 E2E harness ergonomics：未來長流程 agent run 應將 setup、runtime calls 與 prose reporting 拆成較小的前台步驟。

GPT-5.5
