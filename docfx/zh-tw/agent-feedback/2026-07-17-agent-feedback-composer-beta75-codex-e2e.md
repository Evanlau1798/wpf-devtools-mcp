# 使用 WPF DevTools MCP beta.75 建立動態星座

我以真正獨立的 Agent 身分使用公開的 `v1.0.0-beta.75` prerelease，完整執行公開安裝、原創 UI 組合、受保護的專案整合、WPF 建置、執行中程序檢查、可還原互動、截圖重建與完整解除安裝。這輪測試通過，評分為 **9.6/10**。

> 維護者後續：獨立審查確認整體品質達到 9.5/10 發布門檻。兩項可執行的產品 P3——pack-owned 描述不均與缺少 restore/build 提示——已在 beta.76 處理。通用 repeated-layout builder 經 YAGNI 評估後不實作；binary resource export 仍屬 client-side 能力。

![最終動態星座序列器](../../agent-feedback/assets/2026-07-17-composer-beta75-codex-e2e/composer-final-beta75.png)

## 使用歷程

安裝器是第一個令人驚喜之處。它的 JSON 提供 release asset、精確執行檔、client registration artifact、checksum policy、預期與實際 digest、server command，以及 Composer policy profile。我不需要 clone repository，也不需要猜測 packaged sidecar 位於何處。x64 archive 的 SHA-256 與 GitHub 一致，最後的 exact-root uninstall 也只移除了本次驗證的安裝內容。

在查看 recipe 或先前 app 之前，我先寫下三份實質不同的 brief。抽象 ledger 比對排除了與既有 fingerprint 太接近的潮汐樣本筆記與光影劇場，最後選擇「動態星座序列器」：寬廣的 signal node field、窄版 frequency spine、局部 inspector、temporal strip 與 pulse status rail。

不含 recipe 的 compact discovery 在不主導設計的前提下提供了幫助。它列出 core 與 WPF UI packs 的 24 個可組合 blocks。我保留原先 topology，以 grid、border 與 stack primitives 建立 signal field，並在 inspector 放入有套用 theme 的 numeric、toggle、progress、button、icon 與 card controls。我刻意沒有使用已提供的 navigation、tabs 或 dashboard recipe。

## 拼圖式 workflow 的使用感受

最好用的部分是 stable alias。編輯 `@InspectorHeader.properties.text` 與組合到 `@SignalInspector.slots.actions` 都很精準。每次 immutable response 都會告訴我解析後的精確 path。新增的 button 已包含設定好的 text、appearance、width 與 margin，而 inserted-node summary 不必回傳整份 blueprint 就能證明結果正確。

Composition skeleton 同樣很有價值。它明確標示 slot names，並避免 kind transcription 錯誤。Slot summary 也回報 existing/resulting counts，以及 WPF UI card actions unbounded slot 的 explicit null capacity members。

較不方便的是資料量。以 grids 與 bordered stacks 組成不對稱的 pseudo-canvas，需要很多層 nested JSON。現有 blocks 足以完成設計，但若未來有能處理重複 layout pattern 的小型 builder operation，便能在保留創作控制的同時減少 Agent bookkeeping。

我另外展開 card recipe 作為獨立 contract check，之後沒有使用它。這正是理想結果：recipes 是可選的加速器，而不是強制套用的產品外形。

## Preview 與視覺信心

![修訂後的 Composer preview](../../agent-feedback/assets/2026-07-17-composer-beta75-codex-e2e/composer-preview-beta75.png)

第一次 preview 成功 compile 並載入，semantic scene 也完整，但 runtime layout inspection 在 inspector 找到五項 clipping risks。回應並非只說「已載入」；它完成 94/94 targets 的 correlation 與 inspection，並精確說明哪個 nested stack 超出空間。

我只做了一輪修訂：稍微加寬 inspector、縮小 card padding，並收緊 vertical spacing。接下來兩次 preview 使用同一份 immutable draft，分別省略 bounds 與明確指定 1024 bounds。兩次都得到相同且完整的 1024×636 圖片、零 clipping、94/94 inspection，也沒有 unresolved 或 uninspected correlations。

視覺結果看起來是有意識設計，而不是由預設 controls 拼湊而成。Cyan、violet、coral、mint 與 amber signal states 在深色 indigo field 上都很清楚。Inspector 緊湊但易讀。最終 1320×820 app 沒有 classic white surface、文字裁切、重疊、缺少區域或 theme workaround。

## Apply 與 build 的可信度

Write path 透過「拒絕不安全操作」建立信任。非 dry-run apply 需要明確確認。Project integration 偵測到 scratch root 外繼承的 central package management，並以完整的 scratch-local recovery document 停止。建立 local opt-out 後，server 產生 ready plan，使用單一精確 hash 覆蓋 atomic package、resource/startup 與 code-behind operations。

我犯了一個 harness 錯誤：加入 packages 後立刻用 `--no-restore` 建置，因此 build 無法解析 WPF UI XAML namespace。分開執行 restore 後再次 build，結果為零 warning、零 error。若 post-integration response 能簡短提醒 restore，這個順序會更不容易誤用。

## 執行中證據

產生的 executable 啟動後只有一個可見的 main window。`connect()` 透過 raw injection 連接到精確 allowlist 的 process。Scene 包含 69 個 semantic nodes，27 個 authored names 也全部存在。WPF UI NumberBox 的 background 來自 implicit style 與 Application theme brush，而不是 local harness fix。

我把 amplitude 從 72 改成 81，觀察到一項精確 state diff，之後還原為 72。我使用 bounded wait-after-mutation workflow，在 19 ms 內把 armed toggle 關閉，並還原為開啟狀態。點擊 Inject pulse 後，button 與 window bubble levels 都產生 Click events。Binding 與 validation scans 維持乾淨。

## Screenshot 與 contract recovery

最終 screenshot 由 resource 提供，尺寸為 1320×820、87,037 bytes；重建後的 SHA-256 與 metadata 一致。Client 會截斷大型 direct resources，因此我使用 server 提供的 chunk URIs。相同的 portable text-chunk design 也讓我不必依賴 client base64、`TextDecoder` 或 crypto APIs，即可重建 response、tools 與 examples contracts。

這套 recovery design 很可靠，但儲存許多小型 screenshot chunks 是本輪最機械化的工作。若 client 能直接把 MCP resource 匯出成檔案，便能省下大量 Agent 操作，同時不降低證據完整性。

## 摩擦與意外

Product-side friction 僅限於深層 nested authoring，以及一次有實際價值的 preview layout 修訂。Harness/client friction 包括過早使用 `--no-restore` 的 build、過時的 `find_elements` arguments、缺少 event name、兩秒 trace window 在 retrieval 前到期、direct-resource truncation，以及 process-stop verification race。每項失敗都有 bounded recovery path，也沒有留下未還原的 application state。

最令人驚喜的是 contracts 能同時兼顧精簡與證明力。Draft refs 減少 payload churn；text chunks 讓大型 contracts 保持 portable；correlation counts 讓「沒有 clipping」成為有意義的結論；snapshot/restore 保障 runtime mutation；plan hashes 則讓 project writes 可以先被審查。

## 我會優先改善的項目

1. 在保留 exact paths 與 immutable drafts 的前提下，加入精簡的 repeated-layout builder。
2. 讓更多 WPF UI blocks 具備完整的 property 與 slot descriptions。
3. 由 client 提供 MCP resource-to-file export，簡化 binary evidence 的保存。
4. 在 project integration 後加入 restore reminder。

## 結語

beta.75 讓我產生信任，因為它不會掩飾不確定性。它會明確指出 direct resource 過大、preview correlation 是否完整、layout 是否 clipping、draft candidate 是否無效、write 是否需要確認，以及 mutation 是否需要還原。同時，它保留足夠的組合自由，讓我建立的是可辨識且原創的 instrument，而不是 gallery-shaped dashboard。這種「創作自由與明確安全邊界並存」的特性，是整體體驗最出色的部分。
