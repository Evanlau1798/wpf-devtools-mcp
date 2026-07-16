# UI Composer 工具

UI Composer 工具用於本機 Composer extension pack 與 blueprint 輸入。它們不檢查正在執行的 WPF target，應在 catalog、validation、rendering、preview compile 或 apply workflow 前使用。

## Contract Compatibility

Composer 目前支援下列 v1 contracts；`schemaVersion` 缺失或不同時會 fail closed：

| Contract | Supported version | Compatibility policy |
|---|---|---|
| UI pack | `wpfdevtools.ui-pack.v1` | 原樣讀取 artifact；install/import workflow 只複製 pack files，不改寫內容。 |
| UI block | `wpfdevtools.ui-block.v1` | 只從 enabled packs 解析 pack-qualified block kinds。 |
| UI recipe | `wpfdevtools.ui-recipe.v1` | 只有 declared required packs 可用後才展開 recipes。 |
| UI blueprint | `wpfdevtools.ui-blueprint.v1` | 必須提供 `packs[]`、`primaryPack` 與 pack-qualified block kinds；可用 `resourceVariants` 選擇 pack-owned resource variants。 |
| Source lock | `wpfdevtools.source-lock.v1` | 保留 loaded packs 的 provenance metadata。 |
| Pack install manifest | `wpfdevtools.pack-install-manifest.v1` | 記錄 copied pack installation metadata，不改變 pack artifact。 |
| Composer project | `wpfdevtools.composer-project.v1` | 保留給 project-local Composer configuration。 |

目前 contract 仍在 pre-release。Beta 版本可以刻意修正 v1 shape 而不保留 legacy aliases；packs、validators、文件與 extension-pack creator 必須一起更新。第一個 public stable contract 發布後才開始套用 stable compatibility policy。

## 資料驅動的視覺基礎

- Native WPF layout 由明確的 `core@0.1.0` pack 提供，並使用 `role="layout-pack"`。請使用 `core.stack`、`core.grid`、`core.rowDefinition`、`core.columnDefinition`、`core.gridCell`、`core.border`、`core.text` 與 `core.template` 等 qualified kinds。
- `core.grid` 支援真實 rows、columns、grid cells、spanning、alignment 與 WPF `gridLength`。Layout 行為來自 pack data，不是 engine primitive 或 WPF UI 特例。
- Built-in WPF UI visual set 包含 `wpfui.numberBox`、`wpfui.toggleSwitch`、`wpfui.progressRing`，也提供可設定的 typography、margin、padding、alignment、width 與 window content constraints。
- Property contract 可宣告 `minimum`、`maximum`、`integer`、`thickness` 與 `gridLength` constraints。Slot `allowedKinds` 接受 exact qualified kind、`*` 或 `<pack-id>.*`；optional non-negative integer `minItems` 與 `maxItems` 宣告 child-count bounds，省略時代表最少零項且不設上限。`xamlItemTemplate` 會對每個 child 套用宣告的 wrapper。
- Extension block 可宣告 `authoringRoles`，slot 則可用 `childRole`、`whenProperty`、`whenValues`、必填的 `itemSpacingProperty` 與 optional `childMarginProperty` 宣告一個純資料的 `adjacencyAdvisory`。相鄰且角色相符的 children 沒有有效水平間距時，validation 會在第二個 child 的精確路徑回傳通用 `AdjacentContentWithoutSeparation` warning 與 repair paths，最多 32 筆；engine 不推測任何 pack、block kind、control library 或 property name。
- Pack 可提供具 default 與 pack-owned `appearance`（`light`、`dark` 或 `neutral`）的 named `resourceVariants`。Blueprint 依 pack id 選擇 variant，因此 Composer 不需要任何 library-specific theme logic。Block property 可將 `visualRole` 宣告為 `surface`；當 explicit surface 與 selected theme-styled subtree 衝突時，validation 會在精確 property path 回傳 `SurfaceThemeContrastRisk`。
- 會輸出 pack XML namespace 的第三方 renderer 必須在 `pack.json` 宣告安全 structural preview metadata。Composer 依 metadata 產生 preview types；使用 custom namespace 卻缺少 contract 時回傳 `PreviewContractMissing`。只輸出 native controls 的第三方 renderer 不需要 stub contract。Pack 不可提供 arbitrary preview C#。
- Preview metadata 保持 pack-neutral：語意子類別若套用以原生基底為 TargetType 的樣式，應使用 `tabControl` 或 `tabItem`。任何 selected `baseKind` 已繼承的 member 都不可重複宣告；`Window.Content`、sizing、command、items 與 tab state 等 native properties 應直接用於 renderer XAML。Composer 會拒絕可能讓 authored value 與 native visual tree、command、style 或 template 脫節的 shadow declaration。只有整個值為 unset property token 的 renderer attribute 會被省略；明確空字串與 literal empty attribute 仍會保留。除非 blueprint 明確覆寫目前 theme，否則不要設定 `Foreground` 等可繼承 visual property。
- 任何 blueprint node 都可宣告穩定的 `elementName` 與 `automationId`。Composer 會驗證安全語法及整棵樹的唯一性，再將它們保留為 renderer root 上的 WPF `x:Name` 與 `AutomationProperties.AutomationId`。重複值會以 `DuplicateElementName` 或 `DuplicateAutomationId` 失敗；若與 pack-owned root identity 衝突，也會明確失敗而不會暗中改寫 renderer contract。

建立原創 app 時，先以 `includeRecipes=false` 呼叫 `get_ui_block_catalog`，依 available capabilities 決定 creative brief。稍後再把 recipes 當作 optional accelerator；不要讓第一個 recipe 決定 app concept。

## Composer observability

Composer tool responses 會包含 `observability` object，提供本機 structured logs、per-call metrics、top diagnostic codes 與 privacy policy summary。這些資料只會回傳在 MCP response 或 pack import plan；預設不會 export 到 remote service。Hosted environment 若要明確停用 telemetry policy，可設定 `WPFDEVTOOLS_COMPOSER_TELEMETRY_DISABLED=true`。

Observability payload 不包含 blueprint JSON、generated XAML、完整使用者檔案內容、secrets 或 absolute local paths。Logs 只保留穩定 diagnostic codes 與短 remediation text，讓 agent 可在不複製 project content 的情況下除錯 validation、render dry-run、apply、security rejection、rollback、preview compile 與 pack import paths。

## `list_ui_block_packs`

列出 built-in、project-local 與 user-global roots 中已安裝的 UI block packs。每個 entry 都包含 `kind`、`themeTokens`、`resourceVariants`、`role`、`required`、counts、provenance、readiness metadata 與可用 block kinds。`resourceVariants.defaultVariant` 及其有序 variant ids/appearances 是權威的 pack-neutral resource choices。`role` 是依 pack kind 推導的建議的 blueprint role；`required=true` 表示預設 required declaration，而 `required=false` 不代表 blueprint 可以省略其實際使用 block 的 pack。請以 top-level `allowedPackRoles` 作為 blueprint `packs[].role` 的權威 pack-neutral 值，不要從 pack id 猜測 role。

Request options:

- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

此 tool 的公開 payload 不會回傳 absolute pack root paths。請以 `structuredContent` 作為 canonical result，`content[0].text` 只作為 compact fallback。

## `import_ui_block_pack`

驗證 normalized extension-pack ZIP，並只在明確核准後安裝至 `<projectRoot>/.wpfdevtools/packs`。預設 dry-run 會回傳 pack identity、archive SHA256、destination root 與 relative file plan，而且不寫入檔案。

Request options:

- `archivePath`: 必填，經審查 normalized pack ZIP 的 absolute local path。
- `projectRoot`: 必填，absolute local WPF project root；這是唯一 write boundary。
- `dryRun`: 預設為 `true`；先審查 archive hash 與完整 file plan。
- `confirmImport`: `dryRun=false` 時必須為 `true`。
- `allowOverwrite`: 預設為 `false`；只有在審查相同 pack id/version 的 replacement 後才啟用。

Non-dry-run import 還需要 `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`、`WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true`，以及 exact `WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS` match。Importer 會拒絕 unsafe archive entries、invalid pack contracts、destination reparse points，以及 project-local registry 以外的 writes；它不會修改 project files、package references、resources、XAML、code-behind 或 ViewModels。

## `get_ui_block_catalog`

從 enabled Composer packs 回傳 block catalog entries。Agent 需要在建立 blueprint 前理解具體 block kinds、properties、slot names、`allowedKinds`、declared child-count bounds、renderer availability 或 source hint summaries 時，先呼叫 `list_ui_block_packs`，再使用此 tool。

Request options:

- `packIds`: optional pack id filter，例如 `["sample"]`。
- `category`: optional block category filter。
- `kindPrefix`: optional pack-qualified kind prefix。
- `composableOnly`: true 時只回傳具備 renderer template 的 blocks。
- `kind`: optional exact pack-qualified block kind，用於 single-block detail。
- `includeRecipes`: true 時同時回傳可供 `expand_ui_recipe` 使用的 recipe catalog entries。
- `compact`: true 時回傳精簡 discovery projection，保留 identity、pack-authored block description、category、property names 與 preview warnings、slot bounds、renderer availability、`compositionSkeleton` 及 pack-defined `authoringRoles`。省略 `maxItems` 仍表示不設上限。
- `allowedValueQuery`: optional case-insensitive substring search，用於搜尋 allowed string values。請搭配 exact `kind` 與 `compact=false`；query 最長 128 字元。

Catalog entries 只包含 source hint paths，不會把第三方 source code 複製進 tool output。

Broad discovery 請使用 `compact=true`；選定 block 後，在設定不熟悉的 properties 前，以 exact `kind` 及 `compact=false` 查詢完整契約。Full mode 仍為預設，並保留 descriptions、完整 property contracts、slots 與 source hints。

大型 pack-owned vocabulary 在 exact-kind detail 中也會維持 bounded。每個 property 都會回報完整 `allowedValueCount`、目前的 `allowedValueMatchCount`、最多 12 個符合條件的 `allowedValues`，以及這批 matches 是否 truncated。選值時，以 exact `kind`、`compact=false` 與簡短的概念或 icon 名稱作為 `allowedValueQuery` 再呼叫一次。Validation 仍會針對完整 pack vocabulary 做精確比對，並只回傳 bounded、相關性高的 repair values。

Pack author 可為 blocks、properties 與 slots 提供 inert `description` text。當 structural preview 的 measurement 或 styling 可能與 final package 不同時，property 也可提供 `previewWarning`。選擇值之前應先讀取這些 pack-defined fields；它們能說明 renderer 行為，而不需在 Composer 加入 library-specific logic。Renderer identity target 預設可由 runtime element tools 檢查；只有非 element WPF object 無法被這些 tools 找到時，pack 才設定 `renderer.runtimeInspectable=false`。

Response 也會包含 `authoringGuidance`。其中 `strategy="brief-first"` 與 `creativeBriefRequired=true` 會要求 Agent 先依 discovered capabilities 自行決定 product purpose 與 information architecture。`includeRecipes` 預設為 false；之後才把 recipes 當成 optional accelerators 或 fragments 使用。

每個 catalog item 都包含依該 block 自身 contract 產生的 pack-neutral
`compositionSkeleton`。其中會提供精確 `kind`、required properties 的值，以及
declared slots 的空陣列。Agent 可直接將此 compact node 放入 blueprint，再加入
children 或 optional properties，不必手動重打 pack-specific kind 與 slot names。

## Optional blueprint draft transport

多步驟 workflow 若要避免重複傳輸及 double-serialize 同一份文件，可使用 `create_ui_blueprint_draft`。它接受一個 blueprint JSON object，回傳 opaque `draftRef`，且不會 echo 原始文件。Immutable、process-local store 最多保留 32 drafts，每份最多 65,536 字元，每筆存活 30 minutes。Reference 無法猜測、永不持久化；MCP server process 結束、到期或容量淘汰後就會失效。

使用 `patch_ui_blueprint_draft` 搭配 live reference，可建立新的 immutable derived reference。Broad object change 可傳入 JSON Merge Patch object：null 會移除 object property、nested object 會遞迴 merge、array 或 scalar 會取代 target。單一 nested edit 則傳入 exact `jsonPath` 與 native JSON `value`；若要刪除該 target，省略 value 並設定 `remove=true`。兩到 16 個相關 edits 可改傳 ordered `operations`；它們會對同一份 working copy 原子執行，並只產生一個 derived reference。每筆 atomic change 都會包含從零開始的 `operationIndex`。Source reference 永遠不變。每次成功衍生都會回傳 bounded `changeSummary`，列出 changed paths 與 compact before/after values，而不 echo 完整 blueprint。遺失、到期或遭淘汰的 reference 會回傳 `BlueprintDraftNotFound` 與 recovery guidance。

七個接受 `blueprintJson` 的 downstream tools 也接受 opaque `draftRef`：`compose_ui_blueprint`、`validate_ui_blueprint`、`render_ui_blueprint`、`preview_ui_blueprint`、`repair_ui_blueprint`、`apply_ui_blueprint` 與 `apply_ui_project_integration`。One-shot workflow 仍可直接使用 `blueprintJson`。

## `create_ui_blueprint_draft`

建立一份 bounded ephemeral draft。Response 包含 `draftRef`、`characterCount`、`expiresAt`、`immutable=true` 與精確 retention metadata，並刻意省略 stored JSON。

## `patch_ui_blueprint_draft`

以三種互斥模式之一衍生新 draft：

- Broad change：傳入 `draftRef` 與 `patchJson`，使用 JSON Merge Patch。
- Surgical change：傳入 `draftRef`、exact path（例如 `$.layout.slots.children[0].properties.text`）與 `value`；target node 若具有唯一的標準 blueprint `elementName`，可使用穩定別名 `@ElementName.properties.text`，不必重複巢狀 array path。Pack-defined property key 若不是 simple identifier，請使用 bracket-quoted segment，例如 `$.layout.properties["accent.color"]`。若要刪除 target，省略 `value` 並設定 `remove=true`。
- Atomic multi-path change：傳入 `draftRef` 與一到 16 個 ordered objects 組成的 `operations` array。每個 object 沿用 surgical mode 的 `jsonPath`/`value` 或 `remove=true` contract；path 與 stable alias 會針對同一 batch 內先前 operation 的結果解析。任一 operation 無效時，完整 batch 會以精確 `$.operations[index]` request path 失敗，不保留 partial draft。此 all-or-nothing mode 只回傳一個 immutable reference 與 ordered per-path change summaries。

Response 會回傳新 reference、`sourceDraftRef`、retention metadata，以及 compact `changeSummary`；其中包含 `changeCount`、bounded `changes` 與 truncation metadata。每個 change 會列出 `jsonPath`、`changeType` 及 compact `before`/`after` values；atomic batch 也會包含從零開始的 `operationIndex`。Response 不會 echo 完整 blueprint。若目標是把 catalog block 插入 slot array，應改用 `compose_ui_blueprint`。

## `compose_ui_blueprint`

將一個 pack-defined `compositionSkeleton` 插入既有 blueprint slot，並驗證結果文件。Agent 可用它逐步建立巢狀介面，不必手動重寫深層 JSON。此操作保持 pack-neutral，而且不會寫入檔案。

Request options:

- `blueprintJson`: 目前完整的 blueprint JSON 文字或 opaque `draftRef`。
- `targetPath`: 精確 slot path。Root slot 使用 `$.layout.slots.<slot>`；每個 nested slot 前提供明確 child index，例如 `$.layout.slots.content[0].slots.actions`；node 具有唯一的標準 blueprint `elementName` 時，也可使用 `@ElementName.slots.actions`。成功 response 仍會回傳解析後的精確 `insertedPath`。
- `kind`: 來自 `get_ui_block_catalog` 搭配 `composableOnly=true` 的 exact pack-qualified block kind。
- `elementName` 與 `automationId`: optional standard identities，可在插入時直接指定。既有 blueprint validation 會驗證安全語法及 blueprint-wide uniqueness；兩者皆不依賴選用的 extension pack。
- `properties`: optional JSON object，可在插入時套用 pack-defined values。Installed block contract 會驗證 property name、type、range 與 allowed values。
- `insertionIndex`: optional zero-based position；省略時 append。
- `projectRoot` 與 `localAppDataRoot`: optional pack discovery roots。

需要在插入時設定 block 時，使用 `properties` 可避免再透過很長的 nested path 追加一次 edit，同時仍以 pack 的 `compositionSkeleton` 為權威。Raw JSON input 在 `composed=true` 時會回傳新的 `blueprint`、compact `blueprintJson`、精確 `insertedPath` 與 validation result。Draft input 則回傳新的 immutable `draftRef` 並省略完整文件，source draft 保持不變。所有未完成 composition 的 outcome 都會以 MCP error result 回傳 `success=false`。Invalid draft-derived candidate 仍會保留在 `candidateDraftRef`；raw input 則維持既有 `invalidCandidate` 與 `candidateBlueprintJson` recovery shape。兩者都不會寫入 project files。Ambiguous path 與 non-composable block 只回傳可採取行動的 errors，不提供 candidate。

每個成功 response 也會回傳 bounded `insertedNodeSummary`，讓 caller 不必 render 或取回完整 draft，就能驗證同呼叫的設定。內容包含解析後的精確 JSON path、kind、optional `elementName` 與 `automationId`、property total/reported counts、truncation state，以及最多 32 個 deterministic property entries。每個 entry 包含 name、JSON value kind、最多 160 字元的 compact value 與明確的 value-truncation flag。

Target 可解析到 installed block contract 時，`targetSlotSummary` 會回傳 exact path、parent kind、slot name、`allowedKinds`、`minItems`、`maxItems`、existing/resulting counts、`remainingCapacity` 與 capacity 是否超出。無上限的 `maxItems` 與 `remainingCapacity` 會明確回傳 JSON null，而不會省略 member。Invalid candidate 也會保留同一份 summary，讓 Agent 遇到 `SlotMinimumItemsNotMet` 或 `SlotMaximumItemsExceeded` 時不必再查一次 catalog。這些是 extension-declared child-count constraints，不是 pixel-width 預測。

## `validate_ui_blueprint`

依照已安裝 Composer pack contracts 驗證 UI blueprint JSON。請在 `list_ui_block_packs` 與 `get_ui_block_catalog` 之後、rendering XAML 或 apply generated UI 之前使用。

Request options:

- `blueprintJson`: required raw UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`；也可傳入 opaque `draftRef`。
- `targetPath`: optional target XAML path，用於檢查 generated class/member collision；省略時使用 `Views/<blueprint-name>.xaml`。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

完成 validation 呼叫時 response 會維持 `success=true`，並用 `valid` 表示 blueprint 是否有效。Validation issues 會包含 `jsonPath`、`code`、`message`、`repairSuggestion`，以及相關的 `allowedKinds` 或 `allowedValues`。語法有效但 field type 不相容的 JSON 會在 serializer exact path 回傳 `InvalidBlueprintShape`，附上 `observedValueKind`、copy-ready `expectedJsonShape` 與精確 replacement guidance；malformed JSON 仍會在 root 回傳 `InvalidBlueprintJson`。未知的 pack-owned resource selection 會以 `UnknownResourceVariant` 失敗；explicit surface/theme 衝突則會在 preview 或 apply 前回傳 bounded `SurfaceThemeContrastRisk` warning。Node-level identity 會在 render 前驗證安全語法、唯一性與 `GeneratedClassMemberNameCollision`。`blueprintSize` 會回傳 `currentCharacters`、`maximumCharacters`、`remainingCharacters` 與 `utilizationPercent`，讓 Agent 能在碰到 public input limit 前先精簡文件。

## `expand_ui_recipe`

將 starter recipe 展開成完整 UI blueprint，並立即執行 blueprint validation。呼叫前可先使用 `get_ui_block_catalog` 搭配 `includeRecipes=true` 探索 recipe id 與 inputs。

Request options:

- `recipeId`: required pack-qualified recipe id，例如 `sample.workspaceStarter`。
- `inputs`: optional JSON object，提供 recipe input values。省略時會使用 recipe defaults。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

Response 包含 `valid`、`recipeId`、展開後的 `blueprint` 與 nested validation result。Built-in WPF UI starter recipes 覆蓋 navigation shell、dashboard card、data grid page 與 tabbed settings patterns。

Built-in catalog 會刻意排除 `Snackbar` 與 `ContentDialog` 這類需要 host 的控制項。這些控制項需要 presenter、host 或 runtime show behavior，不能安全地表示成獨立 layout node。請以 runtime catalog discovery 為準；第三方 pack 只有在 renderer 與 behavior contract 能涵蓋這些要求時才應提供同類控制項。

One-shot raw workflow 在下一個 Composer call 前，請將 `structuredContent` 的 `blueprint` object 序列化為 JSON 文字，再以 `blueprintJson` 參數名稱傳入。重複呼叫時，可先建立一次 draft，再透過同一個 `blueprintJson` 參數傳入其 `draftRef`。不要改用名為 `blueprint` 的參數。

每個 Composer `blueprintJson` 參數最多接受 65,536 字元。請使用 compact serializer，避免 formatting whitespace 消耗此上限。PowerShell 請以 `$blueprint | ConvertTo-Json -Depth 100 -Compress` 序列化 structured blueprint object；明確指定 depth 可保留 built-in recipes 的巢狀 properties。其他 client 在序列化展開後的 object 時應停用 indentation。

## `render_ui_blueprint`

對有效的 UI blueprint 執行 dry-run XAML rendering。請在 `validate_ui_blueprint` 或 `expand_ui_recipe` 後使用，以便在任何寫檔 apply workflow 前檢查 generated XAML、required package references 與 application resource setup。

Request options:

- `blueprintJson`: required raw UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`；也可傳入 opaque `draftRef`。
- `targetPath`: optional target XAML path suggestion。Renderer 會在 file plan 回報此路徑，但不會寫入。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

完成 render 呼叫時 response 會維持 `success=true`，並用 `valid` 表示 render 是否有效。成功結果包含 `xaml`、`requiredNuGetPackages`、`requiredResources`、`packageIntegrationGuidance`，以及 `wouldWriteFiles=false` 的 `filePlan`。Package guidance 會依 target project 推導，而且不會編輯 project 或 central package files。無效結果會回傳 validation 或 render issues，包含 `jsonPath`、`code`、`message` 與 `repairSuggestion`。

## `preview_ui_blueprint`

在 temporary WPF preview project 中 compile generated UI Composer XAML。Agent 需要在 apply generated UI 到真實 project 前取得符合 CI 的 compile、host-load，或 runtime scene/layout evidence 時，請在 `render_ui_blueprint` 後使用。

Request options:

- `blueprintJson`: required raw UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`；也可傳入 opaque `draftRef`。
- `restoreEnabled`: optional boolean，預設為 true。false 時 temporary project 會用 `--no-restore` build，以便 deterministic 驗證 missing-restore diagnostics。
- `startHost`: optional boolean，預設為 false。true 時 successful build 後會啟動 temporary preview host，並回報 generated-view load status。
- `includeRuntimeDiagnostics`: optional boolean，預設為 false。搭配 `startHost=true` 時，會對 temporary host 重用 `connect`、`get_ui_summary(depthMode="semantic")`、涵蓋 generated names 及 non-generated correlation names（authored `elementName` values 與 renderer-provided root `x:Name` values）的 bounded `find_elements` lookup plan、針對這些精確目標的 batched `get_clipping_info`，以及 `get_layout_info`。這需要 `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true`。
- `includeScreenshotDiagnostics`: optional boolean，預設為 false。搭配 `startHost=true` 時會啟用 runtime diagnostics，且只有在 `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` 與 `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` 同時允許時才會要求 screenshot。
- `screenshotOutputMode`: optional closed value，預設為 `metadata`；需要保留 server-owned PNG 時使用 `file`，response 會回傳 `resourceUri` 與精確的 `resourceRead` request。Temporary preview host 結束後、server session 結束前，必須在相同 MCP server session 以 `resourceRead.method`（`resources/read`）和 `resourceRead.params` 讀取。其他值（包含 `base64`）會在 preview work 開始前回傳 `InvalidArgument`。
- `screenshotMaxWidth` 與 `screenshotMaxHeight`: optional positive bounds，預設為 1024。跨 constrained Agent image bridge 進行 visual consumption 時應保留預設值；只有 full rendered dimensions 是 archival evidence 的必要條件時，才明確傳入 null。
- `correlationLookupLimit`: runtime diagnostics 最多檢查的 exact non-generated correlation names（authored `elementName` values 與 renderer-provided root `x:Name` values）數量。預設為 32，上限為 64。只有 `unresolvedCorrelations` 回報 `reason="lookup-budget"` 時才提高；其他原因需要修復或 final-app check，不應增加 lookups。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

此 tool 只寫入隔離的 temporary preview directory，compile smoke 後會刪除。Built-in runtime packs 由 release provenance 信任。NuGet build targets、control constructors 與 resource markup 都是可執行的第三方相依套件，因此 project-local 與 user-global packs 預設維持 structural；審查後必須把 `PreviewRuntimeDependenciesNotApproved` 回傳的綁定內容的 approval token 設到 `WPFDEVTOOLS_COMPOSER_TRUSTED_RUNTIME_PACKS` 才會載入。Token 綁定 pack scope、canonical installed root、id、version 與 fingerprint，不能授權另一個 project 或修改後的 pack。Runtime dependency closure 中的每個 package 都必須列出 exact `[version]` 與 NuGet SHA-512 `contentHash`；restore 使用 preview-local NuGet cache、拒絕未宣告的 transitive packages，並在 build 前核對所有 hashes。Selected resources 會在產生 project 前執行 safety scan。已核准 packs 會使用其 NuGet packages、XML namespaces、selected resource variants 與去重且維持順序的 application dictionaries。Engine 維持 pack-neutral，不會針對 WPF UI、pack ID、block kind、control type 或 resource name 分支。

完成結果依序以 `visualFidelity="resource-backed"` 表示純 runtime output、`"hybrid-resource-backed"` 表示 runtime/stub 混合、`"structural"` 表示只有 stubs；invalid、cancelled 或 build failure 則回報 `"not-available"`。`visualValidationGuidance` 與 `visualComparisonChecklist` 仍要求在 apply、build 並 launch 後確認 final app。成功結果包含 generated `xaml`、`buildOutput` 與 `previewHost` summary。Runtime diagnostics 是 opt-in。成功的 `screenshotOutputMode="file"` resource 仍受 `SessionManager` 的 24 小時與 100-resource 上限管理，並在 expiry、eviction 或 disposal 時移除。若 client 顯示缺漏或大片暗色，但 semantic evidence 完整，先依 `screenshotVerificationGuidance` 重讀同一個 resource 並核對 SHA-256，再決定是否重跑。相同 decoded bytes 仍 sparse 時，才以 `screenshotMaxWidth=1024` 與 `screenshotMaxHeight=1024` 重跑 `preview_ui_blueprint`；原呼叫回傳時 temporary host 已結束。Verified image 與 semantic summary 尚未一致前，不可回報 product visual failure。Build failure 會在可用時對應回 blueprint root 與 renderer template path。

`propertyWarnings` array 只包含 submitted blueprint 明確使用之 properties 的 pack-defined warnings。每個 entry 都會回報精確的 `jsonPath`、`blockKind`、`propertyName` 與 `message`，讓 Agent 將 final-app validation 聚焦在受影響的 layout 或 styling decision，而不必把所有 preview limitation 視為同等相關。

`elementCorrelations` array 會將每個 runtime-inspectable renderer identity target 的 transient 或 safely preserved `x:Name`（`elementName`）對應到精確 blueprint `jsonPath` 與 `blockKind`。Generated names 會避開 active renderer templates 保留的所有 names。明確宣告 `renderer.runtimeInspectable=false` 的 block 仍會輸出 XAML，但因 element tools 無法找到其非 element root，所以不會納入 runtime correlation。Runtime diagnostics 只查詢一次 generated prefix，並依 bounded `correlationLookupLimit` 對 distinct non-generated correlation names（authored `elementName` values 與 renderer-provided root `x:Name` values）做 exact query。請保留預設值以避免不必要的 calls，只有在需要補齊已回報的 lookup-budget gaps 時才提高。Agent 可把結果與 `get_ui_summary` 配對，將 preview evidence 連回 authored node。Correlation metadata 不會寫入 blueprint 或由一般 render/apply 輸出；既有 renderer name 不會被改寫，因此 `ElementName` binding 仍可運作。

啟用 runtime diagnostics 時，`layoutRiskSummary` 會把遭裁切的 correlated elements 對應回精確 blueprint paths，不依賴 pack-specific kinds 或 slot names。Coverage 會明確回報：`correlatedTargetCount` 是 distinct renderer correlation names 數量、`resolvedTargetCount` 是與這些 names 關聯的 distinct runtime element IDs 數量、`inspectedTargetCount` 是其中由 `get_clipping_info` 成功檢查的 IDs 數量。任一 correlation 未解析或 ambiguous（同一 name 對應多個 exact records）、`find_elements` response 回報 `searchComplete=false`，或已解析 element 未受檢查時，`inspectionTruncated=true`；重複或無關 matches 無法掩蓋缺漏。若 name 缺失或 ambiguous，`unresolvedCorrelationCount` 會回報完整的 exact correlation record 數量，`unresolvedCorrelations` 則最多回傳 32 筆 `jsonPath`、`blockKind`、`elementName` 與穩定的 `reason`：`ambiguous-authored-name`、`lookup-budget`、`runtime-match-ambiguous`、`runtime-not-found` 或 `search-incomplete`。若 runtime target 已解析但未受檢查，平行的 `uninspectedCorrelationCount` 與 `uninspectedCorrelations` 會回傳其精確 path、kind、name 與 `elementId`。兩份 bounded lists 各自具有 `reportedUnresolvedCorrelationCount` 或 `reportedUninspectedCorrelationCount`，以及 `unresolvedCorrelationsTruncated` 或 `uninspectedCorrelationsTruncated` metadata，讓 Agent 可直接檢查或精簡遺漏節點；`warningsTruncated` 則獨立代表另一個最多 32 筆 warnings 的輸出上限。Summary 也會回報 clipped-element 總數、block kind、element identity、clipping source、各方向 overflow 與 runtime `suggestedFix`。請在 apply 前把這些項目當成早期結構風險修復；由於 extension-package templates 的實際量測可能不同於 preview stubs，final built application 仍須重做 clipping checks。

## `repair_ui_blueprint`

將 validation、render、compile 或 preview diagnostics 轉成 blueprint-first repair actions。當 `validate_ui_blueprint`、`render_ui_blueprint` 或 `preview_ui_blueprint` 回傳 issues 後使用。

Request options:

- `blueprintJson`: required raw UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`；也可傳入 opaque `draftRef`。
- `diagnosticsJson`: optional diagnostics JSON object 或 array，可使用 render 或 preview result 中的 diagnostics。
- `targetPath`: optional target XAML path suggestion，只用於 render diagnostics。此 tool 不會寫入。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

Response 包含 `repairable`、`generatedXamlPatch=false`、`actionCount` 與 `actions`。只有內容等價的重複 guidance 才會合併成一個 action；僅有相同 `issueCode` 與 `jsonPath` 不會合併不同 message、suggestion、value 或 renderer path。對於合併後的等價 guidance，`source` 保留第一個觀察來源，ordered `sources[]` 列出所有 validation、renderer 或 diagnostic contributors。Actions 會標示 repair 應落在 blueprint 或 pack renderer template contract。此 tool 不會直接 patch generated XAML。

## `apply_ui_blueprint`

為 UI blueprint 產生 guarded apply plan。預設為 dry-run，讓 Agent 能在任何寫檔前檢查 generated view file path、required resources、package plan、authored binding requirements 與 deterministic `projectIntegrationPlan`。

Request options:

- `blueprintJson`: required raw UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`；也可傳入 opaque `draftRef`。
- `projectRoot`: required local WPF project root，用於 path planning 與 write allowlist checks。
- `targetPath`: optional target XAML file path，必須位於 `projectRoot` 內。
- `dryRun`: optional boolean，預設為 true。
- `confirmApply`: optional boolean，non-dry-run 寫入前必須在檢查 dry-run plan 後設為 true。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

非 dry-run 寫入需要 `confirmApply=true`、`WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`、`WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true`，且 `WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS` 必須 exact match。成功的 confirmed response 會保留依照寫入前 target 狀態執行的 file plan：新寫入檔案仍為 `action="create"`，既有檔案仍為 `action="update"` 並回報 backup path。此 tool 會拒絕 `projectRoot` 外的路徑、更新既有 view 前建立 backup、加入 `WPFDEVTOOLS_BLUEPRINT_SOURCE` header、保留 `WPFDEVTOOLS_SAFE_SLOT` manual-edit markers，且不會執行 NuGet restore。

Dry-run 的 `projectIntegrationPlan` 保持 pack-neutral。Operations 會列出 package references、application resources、startup selection 與 pack-declared code-behind base types 的 exact target paths、semantic purposes、current-file preconditions 與 proposed SHA-256。為避免 ungated dry-run 洩漏既有 project-file 內容，response 不會回傳完整 proposed content；plan hash 會把 reviewed semantic operations 綁定到 exact proposed content 與目前 file state。

## `apply_ui_project_integration`

只套用最新 `apply_ui_blueprint` dry-run 回傳的 `projectIntegrationPlan`。呼叫時必須傳入相同的 raw `blueprintJson` 或 opaque `draftRef`、`projectRoot`、`targetPath` 與 pack discovery scope，再加上 `reviewedPlanHash` 及 `confirmIntegration=true`。

此 destructive tool 需要 `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`、`WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true`，且 `WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS` 必須 exact match。它會在寫入前立即重新產生 plan；pack、blueprint、target 或 project file 只要有任何變更，就會回傳 `IntegrationPlanChanged`，且不會寫入。

只允許 plan-generated package-reference、central-package-version、application-XAML 與 code-behind-base-type operations。Package IDs、XAML namespaces、resources 與 base types 全部來自 selected packs，engine 不包含 control-library branch。既有檔案會使用 atomic replacement，每個 returned change 都會記錄 `backupPath` 與 `rollbackAction`。後續 operation 失敗時會 rollback 先前變更，response 也會回報 rollback 是否完成。

## 從 apply 到可執行應用程式

`apply_ui_blueprint` 只會寫入經審查的 view XAML；它不會暗中修改 project file、application resources、code-behind、ViewModel 或 startup flow。請把 response 內的 plans 當成 authoritative integration checklist：

1. 先執行 dry apply，檢查 `filePlan`、`requiredNuGetPackages`、`packageIntegrationGuidance`、`resourcePlan`、`viewModelBindingContract`、`behaviorIntegrationContract` 與 `projectIntegrationPlan`。
2. 只有在 project-root gates 已精確限制到目標專案後，才執行 confirmed apply。
3. `projectIntegrationPlan.ready=true` 時，只有在檢查所有 operations 後，才以其 exact `reviewedPlanHash` 呼叫 `apply_ui_project_integration`。Stale hash 會以 `IntegrationPlanChanged` 失敗；成功的 changes 會包含 `backupPath` 與 rollback evidence。Applied plan 有變更 package references 時，response 也會回傳 `packageRestoreRequired=true` 與精簡的 `buildGuidance` 提醒。
4. Machine-applicable plan 尚未 ready 時，才依 `packageIntegrationGuidance` 手動處理每個 pack-declared package。偵測是 static XML best-effort；每個結果都會回報 `inspectionConfidence`、`inspectionReason`、`inspectedFiles` 與 `inspectionLimitations`，並明確說明未 evaluate MSBuild imports 與 conditions。`mode="project"` 時，把各 `projectPackageReference` 加入回報的 project file。因 `ManagePackageVersionsCentrally=true` 而得到 `mode="central"` 時，把 versionless `projectPackageReference` 加入 project，並把對應 `centralPackageVersion` 加入 `Directory.Packages.props`。若 central file 繼承自 `projectRoot` 外且此 project 應保持隔離，請在 project 內建立 `Directory.Packages.props`，並使用這份完整最小 XML：`<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>`。接著重跑 dry-run plan；不可修改 inherited file。`mode="unknown"` 時 package snippets 為 null；請先檢查 project，不可自行推測 integration shape。
5. 只有在 ready reviewed integration plan 未涵蓋時，才依每個 pack 的需求手動把 `resourcePlan` entries 加入 application resource location。該 plan 已反映 blueprint 的 `resourceVariants` selections 或各 pack default。請以回傳的 pack data 為準，不要假設特定 library namespace 或 dictionary。
6. 若 `filePlan` 包含 `role="code-behind-integration"` 且 reviewed integration plan 尚未 ready，請依 action 與 pack renderer 驗證過的 `codeBehindBaseType`，讓 generated XAML `x:Class` 與 code-behind 繼承相同 type。

7. 將 `viewModelBindingContract.bindingRequirements.status="required"` 視為 implementation gate。Requirements 會從 pack-declared binding properties 與所有 authored WPF binding expressions 泛化擷取，依 normalized binding path 去重，同時保留每個 exact blueprint JSON usage path。請實作所有 resolved paths 並調查每個 `path-unresolved` entry；Composer 會明確回報 `composerWritesViewModelSource=false`。
8. 將 `behaviorIntegrationContract.status="required"` 視為 release gate。每個 interaction 都包含 `bindingStatus`、raw `commandBinding` 與 nullable parsed `commandPath`。即使 complex valid WPF binding 的 path 無法解析，它仍是 required interaction，必須在 final view 完成解析。Navigation command 會收到 `commandParameter`，且必須更新 selected application state 與 destination content；action command 必須產生可觀察的應用程式行為，並提供合適的 `CanExecute` policy。這些是 application contracts，不是自動生成的 business logic。
9. 分開執行 restore、build 與實際 application launch：

   ```powershell
   dotnet restore .\YourApp.csproj
   dotnet build .\YourApp.csproj --no-restore
   dotnet run --project .\YourApp.csproj --no-build
   ```

10. 驗證實際執行中的 app，而不只檢查 structural preview。使用 `connect`、`get_ui_summary`、focused element reads 與 `element_screenshot(outputMode="file")`。逐一觸發 `behaviorIntegrationContract` 中的 interaction，確認 state 或 visible content 發生變化。任何 diagnostic mutation 都應搭配 `capture_state_snapshot`、`get_state_diff` 與 `restore_state_snapshot`。

不要因為 generated application 可以 compile，或 button 顯示為 click-ready 就核准結果。Command-bound control 必須完成 DataContext command，且在 launched application 中驗證可觀察結果後，才算完整。
