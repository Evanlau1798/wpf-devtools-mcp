# Material Store 參考圖 E2E 使用心得 — v1.0.0-beta.114

## 結果

我以不可變的專案區域 `material` 0.1.2 pack 完成 public beta.114
端對端測試。流程包含公開安裝與資格確認、Composer draft、具內容綁定的
pack 核准、guarded apply、Release build／launch、MCP runtime inspection、
最終 PNG 匯出、可見狀態變更與 rollback，以及 public full-uninstall。

![最終 MCP 截圖](../../agent-feedback/assets/2026-07-29-material-store-beta114-codex-e2e/material-store-final-20260729-153908.png)

最終截圖為 1546 × 742、543,568 bytes，SHA-256
`e103f79b3ab14123014b9762566d354434f030eb93a3c9744b41c20ae439ac66`。

## 整體使用感受

Composer 作為 pack-neutral 組裝系統相當一致。Material pack 負責視覺控制，
`core` pack 提供一般 layout、文字、border 與專案自有圖片；engine 不需要知道
參考畫面是商店，也不需要知道本次主題是顯微影像。

Compact discovery 能先縮小候選範圍，focused calls 再提供精確 properties、
enum、warnings、slot contract 與 icon 候選，因此不必載入龐大的 Material icon
字彙。不可變 draft 流程也很清楚：建立、alias patch、重新驗證、修復 contrast
warning、把 pack icon compose 到已發現的 slot，最後再驗證 derived draft。

## 拼圖與 slot 體驗

看到 slot vocabulary 後，拼圖流程很方便。`@UtilityBar.slots.children` 容易理解，
回應也提供原始與結果數量；Material card 的 wildcard content slot 讓 Agent 能
組合原創圖片與文字，而不需要 Composer 內建 Material 特例。

較弱的地方不是 slot，而是 icon discovery 的連續性。Focused vocabulary query
可以解決，但若 pack metadata 能標註少量常見語意角色，例如 search、help、
account 與 navigation，可減少選擇成本，同時維持字彙由 pack 掌控。

## Preview 與實際應用程式

Preview 正確預測 header 比例、七個 chips、42/42/16 media rail、局部延續、
ranking controls 與 inventory 壓力。隔離 host 無法解析專案內三張
`pack://application` 圖片，但回應已明確警告，因此我沒有把空白 preview media
誤判為最終畫面。Apply/integration 後，三張圖片皆在 Release app 正常載入。

具內容綁定的 runtime approval 設計很好：第一次 preview 回傳綁定不可變 pack
內容的 token，第二次僅消耗一次，不需要 global trust 或可變 allowlist。

## Runtime inspection 與 recovery

Scene-first summary 從 444 個 traversed nodes 中整理出 68 個 semantic nodes，
沒有 truncation。Namescope 接著提供 `SearchPrompt`、`PromoRail`、
`FeaturedCard` 與 `RankedInventory` 等穩定 ID。

狀態流程也很完整：

1. capture `SearchPrompt.Text` 與 focus；
2. 執行序列化的可見 mutation；
3. 等待 expected value；
4. 檢查 focused DP；
5. 取得 diff；
6. restore；
7. 驗證 baseline。

Wait 在 22 ms 完成，diff 僅有一個變更，restore 也完整恢復文字與 focus。
不存在的 property 會回傳 `PropertyNotFound` 與明確 recovery hint。

## 視覺結果與參考忠實度

最終畫面保留參考圖的橫向 desktop 結構、稀疏 global navigation、七個 chips、
69.1% 寬的 media rail、兩張完整 promo 加一張局部延續、三欄排名，以及
1/4/7、2/5/8 的掃讀順序。品牌、文字、palette 與所有 media 都是原創。

最大 normalized anchor-edge 差距為 0.070，client ratio 差距為 3.85%，
完整 entity count 為參考圖的 112.5%。剩餘差異是刻意的：單色品牌 icon、
排名項目未使用獨立方形縮圖，以及 ranking 稍早開始。

## 多角度摩擦

- **Pack metadata：** runtime 顯示 `readinessValid=false`，但指定的 immutable
  semantic readiness report 為 `valid=true`。未阻擋流程，但 pack 應攜帶或
  明確連結其 readiness receipt。
- **Composer：** screenshot contract 已描述 chunking；若 file-mode response
  直接附一個可複製的 chunk URI 範例，可避免 client 猜錯格式。
- **Preview：** 可考慮在相同 project-root 與內容檢查下，選擇性 staging 已審查
  的 WPF Resource；若刻意不支援，應持續醒目標示 preview 僅供結構判斷。
- **Agent authoring：** 初始 blueprint 少了四個 braces、一次 screenshot URI
  猜錯與重複讀取、一次不應發生的 scratch filename listing，皆可恢復且不屬於
  產品缺陷。

## 建議

1. 讓 project-local pack 攜帶可驗證的 readiness receipt 連結。
2. 在 screenshot file response 加入一個完整 chunk URI 範例。
3. 由 pack 提供少量常見 icon 語意角色提示。
4. 提供受審查、受限的 project-resource preview staging。
5. Draft derivation 後回傳精簡 changed-alias 摘要。

## 結語

這個 beta 已接近可靠的公開 authoring workflow。Installation trust、contract
discovery、不可變 draft、具內容綁定的 preview approval、guarded integration
與 live rollback 形成合理的安全鏈。產品沒有掩蓋 preview 限制，而是讓最終
Release app 的 MCP 截圖成為權威證據；這讓我能給予 final visual quality
9.64、reference fidelity 9.62、overall Agent experience 9.66。
