# 使用 UI Composer beta.72 建立紙張水拓工作台

我以真實端對端測試 Agent 的身分使用公開的 `v1.0.0-beta.72` prerelease，從公開安裝器開始，最後得到一個已建置、執行並完成檢查的 WPF 應用程式。我沒有使用 repository build output、先前的範例 app、舊報告、桌面自動化或手寫 XAML patch。所有必要的 Composer 與 runtime gate 都通過，我對整體體驗的評分是 9.6/10。

## 我的測試歷程

公開安裝器一開始就建立了清楚的來源證據。它解析出 `release_1.0.0-beta.72_win-x64.zip`、回報精確的 GitHub Release URL，並驗證預期與實際的 SHA-256：

`e20f0a988d1c1cbe88b02bf3a67d0bd345fd9b99e1c83b29de01061e8dfe4a2b`

release 也正確將 beta 信任原則標示為 `ReleaseChecksumOnly`。這項資訊明確且可由機器讀取，我不需要猜測 artifact 是來自本機 source tree 還是公開 release。

我在精確 allowlist 的 scratch root 建立全新的 `ComposerGeneratedApp`。原生 MCP provider 找到 77 個工具，與 release contract 的預期數量一致。canonical tool manifest 也獨立確認七個 `blueprintJson` draft consumer 及其 65,536 字元限制。

## 先保留創作自由，再查看 recipes

workflow 要求我先閱讀精簡且不含 recipe 的 capability inventory，之後才能查看 recipes。這個順序確實改變了我的設計方式。我考慮了三個方向：

- 紙張水拓的耙紋路徑演練；
- 洞穴救援的繩索負載轉移演練；
- 古地圖折頁順序整理工具。

我選擇紙張水拓，因為探索到的 grid、border、card、tab、NumberBox、toggle、progress、icon 與 button controls 能自然表達這個主題，而且不會退化成 dashboard 或 navigation shell。最後的資訊架構是一座四階段蛇形水槽、側邊浴液儀表，以及下方的證據摺頁。

![最終 Composer preview](../../agent-feedback/assets/2026-07-16-composer-beta72-codex-e2e/composer-preview-final-beta72.png)

recipes 對創作的限制比我預期少。系統提供四個 recipes；我使用實際 inputs 展開 tabbed-settings recipe 作為 contract check，但不必採用它的 topology。整個 app 仍然完全依照我的 brief 編排。

## 拼圖式 workflow

「拼圖」是很貼切的比喻：每個 block 都有 identity、合法 properties 與具名 slots，Agent 必須將它們組合成有效結構。

操作直接的部分：

- 每個選取的 catalog block 都有 `compositionSkeleton`。
- 只有在需要時，exact-kind focused reads 才提供不熟悉的 property contracts。
- Slot 的 `allowedKinds`、minimum 與 nullable maximum 都很明確。
- 有 9,235 個 values 的 symbol vocabulary，可透過 bounded search 縮小成七個相關的「Water」matches。
- Stable aliases 讓我能直接指定 `@ViscosityInput.properties.value` 與 `@InstrumentCard.slots.actions`。
- Draft operation 每次都回傳新的 immutable reference。
- `insertedNodeSummary` 與 `targetSlotSummary` 提供精簡但足夠的變更證據。

仍然彆扭的部分：

- 使用多層 skeleton 後，我仍必須自行維護 element names 與 slot owners 的對應關係。
- 較長的 blueprint 即使結構有效，仍可能有空間壓力；靜態 contract 無法完全預測 template measurement。
- 完整 preview diagnostics 的資訊很豐富，也可能占用大量 Agent context。

最有價值的小幅改善，是在成功的 draft operation 後回傳 bounded node map，包含 `elementName`、kind、available slots、current counts 與可直接使用的 alias。這能減少重複 bookkeeping，又不必回傳完整 document。

## Immutable transport 與 recovery

初始 creative JSON 經精簡後是 9,497 個字元。`create_ui_blueprint_draft` 回傳 opaque reference，並刻意省略完整 document。我透過 stable alias 修改 viscosity，接著直接在 instrument card 中插入已設定的 Success button。

Extension-owned action slot 是 unbounded。response 明確包含 `maxItems:null` 與 `remainingCapacity:null`，這正是我需要的 machine-readable shape。

我刻意測試以下 failure paths：

- 不存在的 alias 回傳精確的 composition target error；
- 無效的 button appearance 回傳精確 nested property path 與 allowed values；
- 無效 candidate 仍可透過 `candidateDraftRef` 恢復；
- 含點號與 escape 的 keys 使用精確的 bracket-quoted JSON paths；
- null-as-data 在 path sets 與 atomic batches 中維持不變；
- null atomic operation 在 `$.operations[1]` 以 `BlueprintDraftOperationRequired` 失敗；
- missing array traversal 直接失敗，而不是自行創造 container shape。

這些 errors 是為 recovery 設計，而不只是拒絕 input。

## Preview 信任度

我對同一份 immutable draft preview 兩次：第一次使用預設 screenshot bounds，第二次明確設定 1024×1024 limits。兩張圖片都是 1024×801、63,302 bytes，SHA-256 也相同；更重要的是，它們呈現相同的 semantic regions。

第一張圖揭露真實問題：第三道 pigment wake 不完整，第四道完全不見。Runtime diagnostics 也獨立將九個 clipping results 對應回精確 blueprint paths。pixels 與結構證據一致，因此我能放心採取行動。

我以一次 atomic revision 增加 window 與 minimum heights。下一次 preview 是 1024×1005，並具有：

- `previewHost.status="loaded"`；
- `viewLoaded=true`；
- 64 個 correlated targets；
- 64 個 resolved targets；
- 64 個 inspected targets；
- 沒有 unresolved 或 uninspected correlations；
- 沒有 clipping。

最終 preview 也證明僅作為 definition 的 row/column blocks 能正常 render，且不會被錯誤計入 inspectable targets。

我也另外 preview 一個 root `elementName="MainWindow"` 的有效案例。它成功 compile 並載入，沒有 temporary host class collision，增加了我對 generated class isolation 的信心。

## Apply、project integration 與 build

Dry render 維持 read-only。之後的 apply 展現三個很好的 safety behaviors。

第一，scratch root 外的 inherited central package file 讓 project integration plan fail closed。response 提供完整且最小的 local override：

`<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>`

我只在 scratch project 建立這個檔案，下一份 plan 就成為 ready。

第二，未確認的 view apply 回傳 `ApplyConfirmationRequired`。

第三，未確認的 project integration 回傳 `IntegrationConfirmationRequired`。

完成 review 後，我確認 view write 與精確 integration plan hash。Composer 只加入 response 宣告的 WPF UI packages、依回傳順序排列的 dark theme resources、startup selection 與 FluentWindow code-behind base。build 以零 warnings、零 errors 通過。

## 最終視覺結果

![最終建置的 WPF 應用程式](../../agent-feedback/assets/2026-07-16-composer-beta72-codex-e2e/composer-final-app-beta72.png)

最終 MCP capture 是 1100×1080、80,531 bytes，SHA-256 為：

`9d52ac971928c8115c07a7ba663a8c1c4db6c86dde0130df262b4d73c8a902f0`

我透過伺服器所公布的 16 KiB resource chunks 重建圖片，並在隔離的 image view 中重新開啟完全相同的 PNG。

視覺上，這個 app 比較像儀器，而不是通用 enterprise shell。白色 editorial typography 位於 charcoal 背景上；四個 tinted wakes 使用 cobalt、oxide、indigo 與 saffron surfaces；紫色 toggles 與 progress rings 表達目前 bath state；粉紅 rehearsal button 與綠色 sealing button 清楚區分探索與提交 actions。下方 proof fold 在視覺上較次要，但仍容易閱讀。

所有 regions 都完整顯示。沒有 classic white WPF surface、無法閱讀的 foreground、overlap 或 clipping。preview 與最終 package styling 一致。

## Runtime 信心

runtime connection response 建議先取得 scene-first summary，這是正確的下一步。app 從 331 個 traversed elements 產生 55 個 semantic nodes，而且沒有 truncation。light summary 保留相同 scene，並省略詳細 node array。

我在讀取 trees 前先使用 exact element search。viscosity NumberBox 的白色 Foreground 與半透明 Background 來自 `Style`，並套用含 `BasedOn` 的 implicit `Wpf.Ui.Controls.NumberBox` style；它沒有 local resource workaround。

八個代表性 elements 都具有 nonzero bounds 且沒有 clipping。Mirror toggle 與兩個 primary actions 都是 interaction-ready。

在 mutation safety 方面，我 capture `MirrorToggle.IsChecked`、click 真實 control、觀察 routed `Click` 與 `False→True` state diff，然後 restore 並驗證回到 `False`。這是本輪特別令人滿意的一段：safety contract 不只是理論。

## Contract 與 client 觀察

portable contract route 是最出色的功能之一。我重建了：

- response：76,169 bytes、10 個 text chunks；
- tools：199,110 bytes、25 個 text chunks；
- tool-examples：11,613 bytes、2 個 text chunks。

client 沒有提供 `atob` 或 `crypto.subtle`，所以 retained binary-contract verification 受到限制；這不影響 authoritative text route。

client 也截斷完整 screenshot resource 的顯示內容。screenshot response 已提供 chunk URIs、lengths 與 SHA-256，因此我能在不擷取新 pixels 的情況下恢復精確 bytes。這是針對不完美 Agent bridges 的良好產品設計。

## 摩擦與意外狀況

我記錄了每個非零或放棄的路徑：

- 第一版 compact-catalog adapter 尋找 `blocks`，但實際回傳欄位是 `items`。
- 第一版 authored height 造成下方 wake content clipping。
- 一次 client-side screenshot decode 因為 `atob` 未定義而失敗。
- Web search 回傳無關 repository，raw page 也出現 cache miss；透過精確 public raw retrieval 恢復。
- process stop 後立即驗證的時間太早；一次 bounded retry 確認程序已結束。
- cleanup JSON 在 shared state 中沒有看到 installer-owned location，但精確 validation root 已不存在。
- 第一次 creative-ledger patch 的 context 已過期，因為當時已有另一筆 abstract beta.72 entry；較精確的 append 保留兩筆資料。

這些都沒有成為產品 P0–P3 finding。clipping 是一般 authoring recovery，而且 diagnostics 表現很好；其餘屬於 client、harness 或 shared environment state。

## 優先改善建議

1. 在成功推導 draft 後回傳 compact draft node/slot map。
2. 增加只包含 build/load、correlation counts、warnings 與 screenshot metadata 的精簡 preview diagnostics profile。
3. 對明顯的 fixed-height content pressure 提供 non-blocking static advisory。
4. 讓 validation harness 能提供 isolated installer-state root，使 cleanup counters 可獨立歸因。

## 結尾心得

這輪測試讓我重新理解 Composer：它不是「XAML generator」，而是「bounded UI construction protocol」。最強的部分不是單一 control，而是 discovery、immutable authoring、runtime preview、guarded project integration 與 final inspection 之間的 contracts。

整個過程中，我一直保有創作控制權。我不需要採用預設 dashboard、navigation shell、card grid 或 sample app；同時，系統也避免我猜測 roles、kind names、slot capacities、value vocabularies、project write boundaries、central package behavior 或 resource order。

最能建立信任的時刻，是第一版不完美的 preview。產品沒有提供虛假的 clean summary，而是對應出精確 clipped nodes，pixels 也證實相同問題。完成一次小幅 immutable edit 後，結構與視覺證據一起變得乾淨。最終 app 成功 build、launch、接受真實 interaction、精確 restore state，並產生可驗證 bytes 的 screenshots。

在 9.6/10 的評分下，剩餘改善空間是減少 Agent bookkeeping 與 payload weight，而不是補上缺失的核心 workflow。公開 prerelease 已經連貫、安全，而且確實能用來建立原創 WPF UI。
