# Flow Launcher style pack beta.116 Agent 使用心得

本文記錄同一位 Codex Agent 完成公開預發 E2E 後的主觀心得。本輪 12 項核心
qualification gate 全數通過；最終視覺品質為 9.7/10、style capability fidelity
為 9.8/10、整體 Agent 體驗為 9.8/10。

## 本次建立的產品

我使用 immutable `flow-launcher-style` pack 建立原創的環境現場應變工具
**Fieldglass Ops**。它不是 Flow Launcher 的複製品；產品領域、名稱、文案、
資訊架構、色彩、操作模型與版面密度皆由本輪自行設計。

![Fieldglass Ops 最終 MCP 截圖](../../agent-feedback/assets/2026-07-30/fieldglass-ops-beta116.png)

最終畫面包含浮動命令殼層、查詢與執行動作、六筆具快捷鍵的操作結果、明確的
選取狀態、readiness feedback、telemetry progress、三個快速動作、三個導覽
目的地，以及一個已選取的流域情報 extension surface。根視窗為 980 × 960，
實際 MCP PNG 為 964 × 921、58,065 bytes，SHA-256 為
`07ee3c3b8900545a3ab5c09728f6726c4da73acfd96808505d30d7f332ab81c7`。

## Composer 拼圖體驗

拼圖流程相當方便。Recipe-free compact discovery 先提供完整語意字彙、分類、
properties、slot 限制、composition skeleton 與 renderer 狀態；focused discovery
再只展開實際要使用的精確型別、預設值、警告與 allowed values。

Stable alias 特別實用。我能以 `@IncidentSearch`、`@SelectedDispatch` 與
`@ReadinessFeedback` 精確修改文案，再把一個 block 組合進
`@OperationsLauncher.slots.content`。回應會同時交代插入位置、父 block、
前後數量、容量與允許類型，因此不需要盲目嘗試。

## Preview 與實際 App

第一張 preview 在結構上正確，但下方約有 28% 空白。實際 PNG 讓問題很容易
辨識，因此我只收緊根視窗，不改變語意組合。接受的 preview 與 Release App
最終截圖在尺寸、bytes 與 SHA-256 上完全一致，這是 preview 能準確預測實際
結果的強證據。

公開安裝的 server 隨後連線到精確的 Release executable。Scene-first summary
回傳 38 個未截斷的 semantic nodes，focused lookup 也能直接找到選取結果與
查詢面。狀態安全流程成功完成 capture、mutation、exact diff、restore 與 bounded
wait；一次不等長的 DP batch 亦回傳可直接採用的結構化修復提示。

## 多角度摩擦與建議

### Pack

唯一的視覺打磨建議是加入明確的 row stretch 或 horizontal alignment property。
目前選取列會填滿 rail，其他列的 border 則依內容寬度排列；功能與可讀性沒有
問題，但一致的 rail option 能改善節奏。

### Composer

最小且通用的改善是讓 preview diagnostics 回傳 measured content bounds 與
estimated empty-space ratio。現有 desired size 與 clipping 資料已足夠可靠，
再加這一項即可更快調整各種 pack 的 viewport。

### Installer 與 Agent authoring

Installer 明確回報 x64 asset、expected/actual SHA-256、`ReleaseChecksumOnly`
trust、單一 `other` registration 與精確 full-uninstall。測試中出現的 creative
ledger 讀取順序、PowerShell 保留變數、DP batch 軸長及 cleanup wrapper 問題，
皆屬 Agent 自身錯誤，且都能由原始 MCP 回應與精確路徑檢查快速復原。

## 結語

這輪讓我對公開工作流有很高信心。最強的部分是 immutable pack provenance、
compact-to-focused discovery、alias-based draft editing、結構化 slot composition、
逐像素 preview、guarded integration 與可復原 runtime diagnostics。Pack 保持
語意化、Composer 保持 pack-neutral，同時仍保留足夠的創作自由。
