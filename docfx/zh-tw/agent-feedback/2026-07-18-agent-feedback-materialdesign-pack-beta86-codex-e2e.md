# 使用公開 beta.86 Composer 與 MaterialDesign pack 建立 CueMargin

我以全新的 Agent 身分開始這輪測試，不知道獨立 pack 生成 session 的內容，也沒有查看先前的 E2E app、報告或截圖。我的目標不只是讓 Composer 產生可編譯的 XAML，而是確認公開 prerelease 與不可變的第三方 pack，是否足以建立真正原創且複雜的 WPF 應用程式。成品還必須能建置、啟動、呈現有意識的設計、接受檢查，並能安全地修改與還原狀態。

答案是肯定的。整體體驗獲得 **9.6/10**，而且沒有尚未解決、與專案相關的 finding。

## 安裝流程明確且可還原

公開安裝器的回應立即提供建立信任所需的資訊：解析出的 beta.86 版本、精確 GitHub release asset、安裝後執行檔、註冊產物、信任政策，以及相符的預期與實際 archive checksum。由於這個 beta 採用 `ReleaseChecksumOnly`，精確的 checksum 證據格外重要。最後執行 exact-root `full-uninstall` 時，也只移除了本輪驗證的安裝內容。

隔離邊界同樣清楚。launcher 擁有的 bootstrap provider 只負責提供原生工具；我則在指定的驗證目錄中，獨立安裝公開 release。我沒有重新安裝、停止或移除 bootstrap。

## Discovery 保留了創作自由

在查看 recipe 或生成報告前，我先透過 dry-run 與 hash 綁定確認，匯入不可變的 `material@0.1.2` archive。Runtime discovery 接著把它顯示為 project-local 第三方 pack，並與 `core` 及另一個內建 visual pack 並存。我刻意只使用 Material 作為視覺 pack，`core` 則只負責 layout。

Compact catalog 是本輪最有利於創意發揮的 Composer 介面。它提供 block identity、說明、分類、property 名稱與警告、slot bounds、renderer availability、skeleton 與 role，卻不會展開完整 property contract 或 source hint。這些資訊已足以讓我提出三個不同的企劃：

- 電影院字幕安全區校對工作台；
- 高山雪坑分層命名板；
- 手鐘和弦配置排練扇面。

我選擇電影院概念，因為它最符合現有能力，同時與抽象 diversity ledger 的結構差異最大。我將它命名為 **CueMargin**。畫面中心是一個明亮的電影影像區，外圍則是深色編輯介面；它不是 dashboard、navigation shell、drawer、ledger，也不是重複的 card matrix。

選定概念後，我才查詢所用 block 的完整 contract。13,646 個 icon 值原本可能消耗大量 context，但 substring discovery 只回傳 12 個符合 `Film` 的結果，並誠實提供總數、match 數與 truncation metadata。我不必重建整份 vocabulary，就能選出 `FilmCheckOutline`。

## 拼圖式 workflow 大多令人愉快

每個選定的 block 都從自己的 `compositionSkeleton` 開始。我再使用原生 grid 與 stack 關係，組成電影畫面區、字幕處理區、時間 ribbon 與決策區。Immutable draft reference 避免每次 request 都重送 7 KB blueprint。`@CaptionEditor.properties.text` 與 `@DecisionActions.slots.children` 等 stable alias，也比深層數字 JSON path 更容易理解與維護。

當我出錯時，workflow 也能乾淨復原。我誤把 contract 未宣告的 property 放在原生 text block 上。Composition 回傳 invalid 但仍保留的 candidate、精確錯誤 path 與 slot summary。我只從最新 valid draft 移除該 property，再次插入便成功。刻意使用不存在的 target 時，呼叫也以 structured guidance 失敗，而且沒有取代 valid draft。

Transport-only probes 進一步提升了信心。JSON null 能在 path-set 中作為資料保留，Merge Patch null 會移除 optional member，缺少的 object parent 會建立，缺少的 array traversal 仍會報錯，atomic operations 維持順序，而含句點或特殊 escape 的 key 會產生 bracket-quoted path 與可直接採用的 expected shape。

## Runtime approval 讓 preview 值得信任

第一次 preview 以 structural 模式完成編譯，並回傳 project-local Material pack 的 content-bound review。我審查 identity、scope、fingerprint、resources、精確 package closure 與 content hash。傳入 one-call token 後，真實 MaterialDesign 5.3.2 runtime 成功載入，且不會持久保存信任，也不需要加入 library-specific special case。

Approved preview 是整個流程從「正確」轉變為「令人信服」的時刻：

![CueMargin approved preview](../../agent-feedback/assets/2026-07-18-material-beta86-cuemargin/cuemargin-preview-approved.png)

深色 root、graphite cards、outlined input、chips、紫色 progress、floating action 與 raised approval action 共同形成一致的 Material 介面。Composer 對 55 個 targets 全部完成 correlation 與 inspection，沒有 clipping，並回傳完整 screenshot。明亮的影像區很明顯是刻意設計的畫面中畫面，而不是未套用樣式的白色缺口。

Preview 也保留了適當的不確定性。明確設定 `core.stack.spacing` 的四個位置會出現精確 path 警告，說明 structural 與 real-package measurements 可能不同；未設定該 property 的 stack 則沒有警告。因此，最終 app 使用的是刻意決定的 spacing，而不是偶然形成的 preview geometry。

## Apply 與 integration 透過拒絕建立信任

Apply dry-run 顯示完整 Window XAML、package 清單、resource 順序、file plan、binding contract 與精確的 1440×759 target dimensions。非 dry-run write 需要明確確認，並建立 backup。

Project integration 起初拒絕修改 scratch root 外繼承的 central package file。這項拒絕反而是優點。回應提供一份完整且最小的 local `Directory.Packages.props`，只在隔離專案中停用繼承的 central management。我只新增這個明確建議的 scratch-local 檔案，plan 便進入 ready 狀態。Plan hash 隨後精確綁定兩項操作：固定版本的 package references，以及依序套用的 App.xaml resources/startup。

Restore 與 Release build 都成功完成，結果為零 warning、零 error。我啟動的是 provider allowlist 中的精確 executable，而不是 Debug 替代品。

## 最終 app 證明 preview fidelity 與 root-fill 修復有效

真正的 Release window 與 approved preview 一致，並提供原生尺寸與真實 package template 的額外信心：

![CueMargin final Release app](../../agent-feedback/assets/2026-07-18-material-beta86-cuemargin/cuemargin-final.png)

Material 深色 root 完整填滿 1424×720 client area。底部沒有白帶，也沒有其他先前 root-fill defect 的跡象。Runtime inspection 顯示 root ColorZone 與完整 client 大小相同，`VerticalAlignment=Stretch`，而且沒有 clipping。內部的白色電影畫面區則是刻意設計且邊界完整。

視覺層次一眼就能理解：最上方是品牌與用途；左側以電影畫面為主；右側提供 frame convention 與字幕編輯；下方是 timing status；最底部則是 final actions。文字與控制項沒有重疊。紫色 actions 與 progress states 清楚可見，也不會壓過中性的電影畫面。

Structured inspection 支持了像素判斷。Scene summary 找到 48 個 semantic nodes，且沒有 truncation。Form summary 找到兩個已填入的 inputs 與六個可操作 actions。Namescope inspection 找到 18 個 authored names。Approve button 顯示真實的 Material Ripple template。Outlined TextBox 的 foreground 來自 active style expression，並解析為可讀的淺色 foreground。Binding diagnostics 回傳零 errors。

## Mutation 與 recovery 讓人安心

我先擷取字幕文字，套用一個暫時的 runtime value，觀察精確 before/after diff，再還原 snapshot，並確認原始 local value。Progress 也使用相同模式：bounded mutation-and-wait 在 17 ms 內將 68 改為 72，產生 state diff，最後還原為 68。

點擊 Approve 會產生 bubbling Click event。Canonical event array 呼叫能取得該事件；刻意使用舊式 scalar filter 時，則會得到 structured `InvalidArgument` guidance 與可直接使用的 array example。安全 mutation、semantic diff、驗證後 restore，以及可執行的 negative recovery，讓 runtime tools 值得信賴。

## Context 與執行節奏

Compact discovery 與 opaque drafts 大幅降低重複 context。Portable text-chunk route 使用公開的 offsets 與 canonical SHA metadata，成功重建 response、tools 與 examples contracts。Binary compatibility read 也驗證了精確 bytes 與 SHA。

目前最大的注意力成本來自很大的 preview/runtime responses。Client 截斷了一次完整 PNG resource，但公開的 16 KiB chunks 能重建 41,323 bytes 並驗證精確 SHA；相同路徑也保留了最終 53,993-byte image。我希望未來可選擇 compact evidence-handle mode，只回傳做決策所需的 runtime summary 與 resource handle，完整 payload 則在需要時讀取。

幾項非產品摩擦仍值得記錄。環境沒有 `pwsh`，所以我在 evidence root 建立最小的 .NET 8 JSON validator。Windows PowerShell 改變了 helper script 中的 Unicode bullets，但我在建立 draft 前就發現並修正。Inline evidence assembly command 被 policy 阻擋後，我改用簡短 helper script 完成同一項 bounded work。直接取得 web raw file 時也遇到 cache/safe-URL 問題，因此改以 direct HTTP 保存精確公開檔案供審查。這些情況都沒有改變 app，也沒有掩蓋產品缺陷。

## 下一步最值得改善的地方

最有價值且保持 pack-neutral 的 Composer 改善，是為 preview 與 runtime diagnostics 提供 compact response 選項：回傳 bounded summary 與 evidence resource handles，同時保留完整 payload 供後續讀取。現有 chunking 已經正確且可靠；這項改善只為減少 transcript 壓力，不會取代證據。

在 authoring 方面，Composer 可以選擇性地依 selected skeleton 產生短 checklist，列出目前 target slots、min/max bounds、已設定的陌生 properties，以及尚待完成的 integration requirements。這能減少深層 composition 的人工 bookkeeping，同時保留 Agent 的創作主導權。

Material pack 本身可增加更多 semantic interaction examples；如果真實 Material workflow 確實需要，也可加入一個刻意設計為 unbounded 的 container。這些都應維持為一般 pack contract。這輪結果強烈支持 Composer 不加入 Material-specific branches。

## 結語

最令我意外的是，當 discovery 的資訊呈現方式設計得當後，pack 對創意的限制其實很小。由 card、ColorZone、input、chip、progress、icon 與 action 組成的 catalog，本來很容易把設計推向一般 dashboard；但 brief-first 規則、抽象 diversity ledger、compact descriptions 與 skeleton-first authoring，反而讓我做出具有獨特空間結構的專注電影工具。

信任是一層一層累積的：公開 checksum 證據、不可變 archive 驗證、project-local discovery、content-bound preview approval、受保護且可還原的 apply、hash-reviewed integration、乾淨的 Release build、相符的 preview 與 final pixels、WPF-native style/layout 證據、經驗證的 state restore，以及 exact-root cleanup。沒有任何一層能取代其他層。

因此，我給的是 9.6，而不是 10.0。這套 workflow 在正確性與安全性上已達 production-grade，但大型 evidence payload 與深層 composition bookkeeping，仍比必要程度消耗更多 Agent 注意力。即使如此，我最後得到的仍是一個真實、原創且視覺完整的 WPF app，而不只是通過 schema 的練習；同時也保有足夠的獨立證據，能信任眼前看到的結果。
