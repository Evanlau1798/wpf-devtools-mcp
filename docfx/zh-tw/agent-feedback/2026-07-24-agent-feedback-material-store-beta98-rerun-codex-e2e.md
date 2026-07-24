# Material Store beta.98：同一位 Agent 的續跑心得

我將公開 `v1.0.0-beta.98` prerelease 安裝到隔離目錄，透過 Composer 匯入不可變的 Material 0.1.2 pack，並建立小型 WPF 市集 **Kiln Atlas**。這是實際走完 Composer、Release 與 MCP 的流程，不是 source-level review：我以 Release 建置產生的 `ComposerGeneratedApp`、啟動精確的 executable、透過已安裝的 server 連線，最後使用公開 full-uninstall 讓安裝目錄恢復乾淨。

![Approved responsive preview](../../agent-feedback/assets/2026-07-24-material-store-beta98-rerun-codex-e2e/kiln-atlas-preview-responsive-2026-07-24-183333.png)

![Final MCP window capture](../../agent-feedback/assets/2026-07-24-material-store-beta98-rerun-codex-e2e/kiln-atlas-final-responsive-2026-07-24-183333.png)

## 令人滿意的部分

第一個驚喜是 runtime catalog 資訊的品質。我能先從 compact 資訊開始，比較三個不同的市集概念而不借用舊 app，再只查詢 Kiln Atlas 所需的 Material blocks、slot boundaries、resource variants 與精簡 icon vocabulary。這個過程比較像排列一套經過思考的展示系統，而不是猜測原始 XAML。Material card、chips、buttons、text field、combo box、progress control 與 layout primitives 共同表達清楚的產品故事：比較三組窯燒測試磚、選擇 firing profile、留下工作室備註，並暫存一組選擇。

Composer 的 immutable drafts 特別能建立信心。第一份 helper draft 含有空白 optional identities、帶點號的名稱與數值型 thickness；structured errors 指出精確 paths，讓我能修復 candidate 而不遺失原稿。我也刻意使用隔離 drafts 進行 transport 與 validation probes。Merge Patch null、bracket-quoted dot keys、array removals、absent paths、ordered atomic operations、null-operation recovery 與特殊 keys，都產生精簡且 machine-readable 的結果。Collision regression 同樣明確：`wpfui.fluentWindow` 在預設 class-derived name 下回傳 non-blocking `GeneratedClassMemberNameCollision`，替代 XAML target 會移除警告，而 repair 也正確地不提出任何動作。

Visual loop 之所以值得信任，是因為它真的改變了設計決策。第一次較窄的 runtime image 顯示 clipping。我沒有換掉概念，而是進行最小的 responsive Composer 修訂，再執行 approved preview 與 final app capture。兩張 PNG 都只透過 `resources/read` chunks 重建，並檢查長度與 SHA-256 後分別開啟檢視。最終 642×825 圖片完整顯示三個商品、order desk、狀態與 footer，沒有空白或黑色區域。這讓 preview 成為真正的證據，而不只是裝飾性的 compiler artifact。

## Runtime 安全與復原

已安裝的 MCP server 提供俐落的 scene-first 起點：summary 找到 catalog cards、firing-profile ComboBox、notes field、reservation state 與 footer。我擷取 state，將 `SelectedIndex` 改變後，看見顯示選項從 Cone 5 變為 Cone 6，再檢查 diff 並還原。我也從新的 snapshot 追蹤並清空沒有狀態效果的 River Ash action；空白 diff 是誠實且有用的結果，而不是偽裝成 interaction 的失敗。Bounded no-change wait 回傳安全 timeout，且 `stateAfterTimeoutUnknown=false`。Legacy scalar event filter 則以明確的 array-form recovery hint 拒絕輸入。這些小型 negative cases 讓 contract 比默默接受錯誤輸入的寬鬆 API 更安全。

Contract 證據同樣實用。Text contracts 依序重建，我也用 16 個、每個 768 raw bytes 的 chunks 獨立重建 `tool-examples` binary resource。其 11,613-byte 長度與 SHA-256 在解析 JSON 前就與公開的 canonical values 相符。現有 chunk workflow 搭配小型 evidence helper 已可使用；若 client 能直接將 resource 交付成檔案，仍可進一步減少摩擦。

## 摩擦與改善方向

主要摩擦來自程序，而不是已證實的產品缺陷。第一份 authoring helper 產生無效的 optional fields；另一個 PowerShell pipeline 因 host-stream status 未流入 `ConvertTo-Json`，沒有保存其中一個 installer object。兩者都已復原，並記錄為 Agent-side evidence issues。第一次 visual pass 也揭露真實 layout 問題，但 Composer 加上 MCP pixels 讓修復相當直接。公開安裝、Release build、connection、pack import 與公開 cleanup 最終都表現得可預期。

最小且高價值的改善，是在完整 composition request 前提供精簡的 pre-compose authored-node lint，提早標示空白 optional names 與已知 property-type mismatches。第二項改善則是 client-supported resource-to-file handoff，同時保留長度與 SHA-256，讓 Agent 不必自行處理大型 MCP image resources 的 base64/chunk plumbing，仍能維持相同 integrity guarantees。

## 反思與結論

開始時我很謹慎，因為這是 prerelease，而且創作必須真正獨立於過去範例。最後我的信心大幅提升：套件能從公開管道安裝、Composer 保留可修復的 drafts、preview 找出真實 visual issue、Release binary 能透過受 policy 約束的 runtime 檢查，而且 runtime mutations 可以還原。我很喜歡 store reference 只約束產品目標，沒有壓扁創作選擇；Kiln Atlas 仍像一個小而完整的工作室工具，而不是 template。

續跑在 cleanup 前完成所有剩餘的 collision、JSON-shape、event 與 transport branches。最終體驗分數是 **9.6/10**：discovery、authoring、preview/apply/build reliability、visual evidence、runtime safety 與 recovery 都很強；僅因 Agent-side retries 與多次 continuation 節奏小幅扣分。我會信任這套 workflow 來執行 evidence-led WPF Composer 工作，同時仍希望未來補上前述兩項小型 ergonomics 改善。
