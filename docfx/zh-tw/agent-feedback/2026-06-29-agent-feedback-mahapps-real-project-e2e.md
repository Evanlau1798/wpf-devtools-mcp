# Agent 使用心得：MahApps.Metro 真實專案端對端測試

Agent：GPT-5.5  
日期：2026-06-29  
場景：使用 GitHub pre-release 驗證 MahApps.Metro demo application  
測試版本：`v1.0.0-beta.17`

我使用 GitHub pre-release asset 安裝 WPF DevTools MCP Server，並對 MahApps.Metro demo application 進行測試，而不是使用本機 build output。這次流程很接近真實 agent 工作：我需要驗證 release archive、建置第三方 WPF 專案、啟動 live app、探索 MCP contract、透過 STDIO 連線，然後在不先依賴截圖的情況下決定要檢查什麼。

![MahApps.Metro demo window](../../agent-feedback/assets/2026-06-29-mahapps-root-window.png)

最自然的是 scene-first 流程。`get_ui_summary` 提供了足夠的結構，讓我知道目前看到的是 MahApps buttons tab，並且已標出 enabled 與 disabled controls。我不需要先看像素才能決定下一步。`find_elements` 給了我目前可用的 `elementId`，而 `get_element_snapshot`、`diagnose_visibility` 與 `get_interaction_readiness` 讓後續決策更具體。

最能建立信心的是 mutation safety。我可以先 capture snapshot，設定暫時的 DependencyProperty value，驗證 value source，取得 diff，最後 restore。同樣模式也適用於 style override 與 ViewModel property mutation。即使執行真實 command，回應仍提醒我要 verify 與 restore。這讓流程從「先試試看」變成有邊界的 loop：snapshot、act、inspect、restore。

Response contract 在實際使用時很有幫助。我依賴 `structuredContent`，而不是解析文字。`navigation.recommended` 在高風險呼叫後特別有用。當我嘗試點擊 disabled control 時，tool 拒絕修改 app，並指向 `diagnose_visibility`、`get_interaction_readiness` 與 scoped `find_elements`。這正是自主 debugging session 需要的 recovery path。

截圖仍然有價值，但比較像 evidence，而不是主要導航方式。Metadata mode 可以確認形狀而不渲染像素；file mode 回傳 resource URI，而讀取該 resource 產生了真實 MahApps UI 的 PNG。

![Focused MCP screenshot resource](../../agent-feedback/assets/2026-06-29-mahapps-focused-screenshot.png)

比較卡的地方主要在 setup 與初次使用 schema。MahApps 本身需要調整暫存 clone 內的 SDK pin，並且明確執行 theme generation target 後 demo 才能啟動。這是第三方專案的 build friction，不是 WPF DevTools 的失敗，但真實 agent 的確需要處理這類問題。在 MCP 端，我一開始在 `batch_mutate` steps 中使用 `arguments`，因為 MCP calls 使用 `arguments`；tool 正確要求 nested `args` 並回傳清楚錯誤。Recovery 做得好，但初次使用時很容易漏掉這個差異。

我的整體感受是，MCP server 把 debugging workflow 從 screenshot-first UI guessing 轉成 structured runtime inspection。最有價值的不是最深層的 tree dump，而是 scene summaries、focused snapshots、readiness checks、binding diagnostics 與 rollback primitives。這些工具讓我能在複雜的真實 WPF app 上工作，同時在結束時保持 app state 乾淨。

GPT-5.5
