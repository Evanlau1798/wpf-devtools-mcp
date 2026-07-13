# UI Composer 工具

UI Composer 工具用於本機 Composer extension pack 與 blueprint 輸入。它們不檢查正在執行的 WPF target，應在 catalog、validation、rendering、preview compile 或 apply workflow 前使用。

## Contract Compatibility

Composer 目前支援下列 v1 contracts；`schemaVersion` 缺失或不同時會 fail closed：

| Contract | Supported version | Compatibility policy |
|---|---|---|
| UI pack | `wpfdevtools.ui-pack.v1` | 原樣讀取 artifact；install/import workflow 只複製 pack files，不改寫內容。 |
| UI block | `wpfdevtools.ui-block.v1` | 只從 enabled packs 解析 pack-qualified block kinds。 |
| UI recipe | `wpfdevtools.ui-recipe.v1` | 只有 declared required packs 可用後才展開 recipes。 |
| UI blueprint | `wpfdevtools.ui-blueprint.v1` | 必須提供 `packs[]`、`primaryPack` 與 pack-qualified block kinds。 |
| Source lock | `wpfdevtools.source-lock.v1` | 保留 loaded packs 的 provenance metadata。 |
| Pack install manifest | `wpfdevtools.pack-install-manifest.v1` | 記錄 copied pack installation metadata，不改變 pack artifact。 |
| Composer project | `wpfdevtools.composer-project.v1` | 保留給 project-local Composer configuration。 |

目前 contract 仍在 pre-release。Beta 版本可以刻意修正 v1 shape 而不保留 legacy aliases；packs、validators、文件與 extension-pack creator 必須一起更新。第一個 public stable contract 發布後才開始套用 stable compatibility policy。

## 資料驅動的視覺基礎

- Native WPF layout 由明確的 `core@0.1.0` pack 提供，並使用 `role="layout-pack"`。請使用 `core.stack`、`core.grid`、`core.rowDefinition`、`core.columnDefinition`、`core.gridCell`、`core.border`、`core.text` 與 `core.template` 等 qualified kinds。
- `core.grid` 支援真實 rows、columns、grid cells、spanning、alignment 與 WPF `gridLength`。Layout 行為來自 pack data，不是 engine primitive 或 WPF UI 特例。
- Built-in WPF UI visual set 包含 `wpfui.numberBox`、`wpfui.toggleSwitch`、`wpfui.progressRing`，也提供可設定的 typography、margin、padding、alignment、width 與 window content constraints。
- Property contract 可宣告 `minimum`、`maximum`、`integer`、`thickness` 與 `gridLength` constraints。Slot `allowedKinds` 接受 exact qualified kind、`*` 或 `<pack-id>.*`；`xamlItemTemplate` 會對每個 child 套用宣告的 wrapper。
- 會輸出 pack XML namespace 的第三方 renderer 必須在 `pack.json` 宣告安全 structural preview metadata。Composer 依 metadata 產生 preview types；使用 custom namespace 卻缺少 contract 時回傳 `PreviewContractMissing`。只輸出 native controls 的第三方 renderer 不需要 stub contract。Pack 不可提供 arbitrary preview C#。
- Preview metadata 保持 pack-neutral：語意子類別若套用以原生基底為 TargetType 的樣式，應使用 `tabControl` 或 `tabItem`。`SelectedIndex` 與 `Header` 等原生繼承屬性不可重複宣告；Composer 會拒絕可能讓 base style 或 template 讀不到 rendered value 的 shadow declaration。只有整個值為 unset property token 的 renderer attribute 會被省略；明確空字串與 literal empty attribute 仍會保留。除非 blueprint 明確覆寫目前 theme，否則不要設定 `Foreground` 等可繼承 visual property。

建立原創 app 時，先以 `includeRecipes=false` 呼叫 `get_ui_block_catalog`，依 available capabilities 決定 creative brief。稍後再把 recipes 當作 optional accelerator；不要讓第一個 recipe 決定 app concept。

## Composer observability

Composer tool responses 會包含 `observability` object，提供本機 structured logs、per-call metrics、top diagnostic codes 與 privacy policy summary。這些資料只會回傳在 MCP response 或 pack import plan；預設不會 export 到 remote service。Hosted environment 若要明確停用 telemetry policy，可設定 `WPFDEVTOOLS_COMPOSER_TELEMETRY_DISABLED=true`。

Observability payload 不包含 blueprint JSON、generated XAML、完整使用者檔案內容、secrets 或 absolute local paths。Logs 只保留穩定 diagnostic codes 與短 remediation text，讓 agent 可在不複製 project content 的情況下除錯 validation、render dry-run、apply、security rejection、rollback、preview compile 與 pack import paths。

## `list_ui_block_packs`

列出 built-in、project-local 與 user-global roots 中已安裝的 UI block packs。回應包含 pack id、version、scope、block count、recipe count、example count、renderer count、source repository、readiness metadata、diagnostics 與可用 block kinds。請以 top-level `allowedPackRoles` 作為 blueprint `packs[].role` 的權威 pack-neutral 值，不要從 pack id 猜測 role。

Request options:

- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

此 tool 的公開 payload 不會回傳 absolute pack root paths。請以 `structuredContent` 作為 canonical result，`content[0].text` 只作為 compact fallback。

## `get_ui_block_catalog`

從 enabled Composer packs 回傳 block catalog entries。Agent 需要在建立 blueprint 前理解具體 block kinds、properties、slot names、`allowedKinds`、renderer availability 或 source hint summaries 時，先呼叫 `list_ui_block_packs`，再使用此 tool。

Request options:

- `packIds`: optional pack id filter，例如 `["sample"]`。
- `category`: optional block category filter。
- `kindPrefix`: optional pack-qualified kind prefix。
- `composableOnly`: true 時只回傳具備 renderer template 的 blocks。
- `kind`: optional exact pack-qualified block kind，用於 single-block detail。
- `includeRecipes`: true 時同時回傳可供 `expand_ui_recipe` 使用的 recipe catalog entries。

Catalog entries 只包含 source hint paths，不會把第三方 source code 複製進 tool output。

Pack author 可為 blocks、properties 與 slots 提供 inert `description` text。當 structural preview 的 measurement 或 styling 可能與 final package 不同時，property 也可提供 `previewWarning`。選擇值之前應先讀取這些 pack-defined fields；它們能說明 renderer 行為，而不需在 Composer 加入 library-specific logic。

Response 也會包含 `authoringGuidance`。其中 `strategy="brief-first"` 與 `creativeBriefRequired=true` 會要求 Agent 先依 discovered capabilities 自行決定 product purpose 與 information architecture。`includeRecipes` 預設為 false；之後才把 recipes 當成 optional accelerators 或 fragments 使用。

每個 catalog item 都包含依該 block 自身 contract 產生的 pack-neutral
`compositionSkeleton`。其中會提供精確 `kind`、required properties 的值，以及
declared slots 的空陣列。Agent 可直接將此 compact node 放入 blueprint，再加入
children 或 optional properties，不必手動重打 pack-specific kind 與 slot names。

## `compose_ui_blueprint`

將一個 pack-defined `compositionSkeleton` 插入既有 blueprint slot，並驗證結果文件。Agent 可用它逐步建立巢狀介面，不必手動重寫深層 JSON。此操作保持 pack-neutral，而且不會寫入檔案。

Request options:

- `blueprintJson`: 目前完整的 blueprint JSON 文字。
- `targetPath`: 精確 slot path。Root slot 使用 `$.layout.slots.<slot>`；每個 nested slot 前必須提供明確 child index，例如 `$.layout.slots.content[0].slots.actions`。
- `kind`: 來自 `get_ui_block_catalog` 搭配 `composableOnly=true` 的 exact pack-qualified block kind。
- `insertionIndex`: optional zero-based position；省略時 append。
- `projectRoot` 與 `localAppDataRoot`: optional pack discovery roots。

當 `composed=true`，response 會回傳新的 `blueprint`、compact `blueprintJson`、精確 `insertedPath` 與 validation result。若 path 模糊、block 無法組合，或 pack validation 拒絕 child，`composed=false` 會省略 candidate blueprint 並回傳可採取行動的 errors。

## `validate_ui_blueprint`

依照已安裝 Composer pack contracts 驗證 UI blueprint JSON。請在 `list_ui_block_packs` 與 `get_ui_block_catalog` 之後、rendering XAML 或 apply generated UI 之前使用。

Request options:

- `blueprintJson`: required UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

完成 validation 呼叫時 response 會維持 `success=true`，並用 `valid` 表示 blueprint 是否有效。Validation issues 會包含 `jsonPath`、`code`、`message`、`repairSuggestion`，以及相關的 `allowedKinds` 或 `allowedValues`。`blueprintSize` 會回傳 `currentCharacters`、`maximumCharacters`、`remainingCharacters` 與 `utilizationPercent`，讓 Agent 能在碰到 public input limit 前先精簡文件。

## `expand_ui_recipe`

將 starter recipe 展開成完整 UI blueprint，並立即執行 blueprint validation。呼叫前可先使用 `get_ui_block_catalog` 搭配 `includeRecipes=true` 探索 recipe id 與 inputs。

Request options:

- `recipeId`: required pack-qualified recipe id，例如 `sample.workspaceStarter`。
- `inputs`: optional JSON object，提供 recipe input values。省略時會使用 recipe defaults。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

Response 包含 `valid`、`recipeId`、展開後的 `blueprint` 與 nested validation result。Built-in WPF UI starter recipes 覆蓋 navigation shell、dashboard card、data grid page 與 tabbed settings patterns。

Built-in catalog 會刻意排除 `Snackbar` 與 `ContentDialog` 這類需要 host 的控制項。這些控制項需要 presenter、host 或 runtime show behavior，不能安全地表示成獨立 layout node。請以 runtime catalog discovery 為準；第三方 pack 只有在 renderer 與 behavior contract 能涵蓋這些要求時才應提供同類控制項。

下一個 Composer call 前，請將 `blueprint` object 序列化為 JSON 文字；該 object 來自 `structuredContent`。請以 `blueprintJson` 參數名稱傳入，不要改用名為 `blueprint` 的參數；validation、render、preview、repair 與 apply 刻意共用相同的 JSON-string document shape。

每個 Composer `blueprintJson` 參數最多接受 65,536 字元。請使用 compact serializer，避免 formatting whitespace 消耗此上限。PowerShell 請以 `$blueprint | ConvertTo-Json -Depth 100 -Compress` 序列化 structured blueprint object；明確指定 depth 可保留 built-in recipes 的巢狀 properties。其他 client 在序列化展開後的 object 時應停用 indentation。

## `render_ui_blueprint`

對有效的 UI blueprint 執行 dry-run XAML rendering。請在 `validate_ui_blueprint` 或 `expand_ui_recipe` 後使用，以便在任何寫檔 apply workflow 前檢查 generated XAML、required package references 與 application resource setup。

Request options:

- `blueprintJson`: required UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`。
- `targetPath`: optional target XAML path suggestion。Renderer 會在 file plan 回報此路徑，但不會寫入。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

完成 render 呼叫時 response 會維持 `success=true`，並用 `valid` 表示 render 是否有效。成功結果包含 `xaml`、`requiredNuGetPackages`、`requiredResources`，以及 `wouldWriteFiles=false` 的 `filePlan`。無效結果會回傳 validation 或 render issues，包含 `jsonPath`、`code`、`message` 與 `repairSuggestion`。

## `preview_ui_blueprint`

在 temporary WPF preview project 中 compile generated UI Composer XAML。Agent 需要在 apply generated UI 到真實 project 前取得符合 CI 的 compile、host-load，或 runtime scene/layout evidence 時，請在 `render_ui_blueprint` 後使用。

Request options:

- `blueprintJson`: required UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`。
- `restoreEnabled`: optional boolean，預設為 true。false 時 temporary project 會用 `--no-restore` build，以便 deterministic 驗證 missing-restore diagnostics。
- `startHost`: optional boolean，預設為 false。true 時 successful build 後會啟動 temporary preview host，並回報 generated-view load status。
- `includeRuntimeDiagnostics`: optional boolean，預設為 false。搭配 `startHost=true` 時，會對 temporary host 重用 `connect`、`get_ui_summary(depthMode="semantic")` 與 `get_layout_info`。這需要 `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true`。
- `includeScreenshotDiagnostics`: optional boolean，預設為 false。搭配 `startHost=true` 時會啟用 runtime diagnostics，且只有在 `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` 與 `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` 同時允許時才會要求 screenshot。
- `screenshotOutputMode`: optional closed value，預設為 `metadata`；需要保留 server-owned PNG 時使用 `file`，response 會回傳 `resourceUri` 與精確的 `resourceRead` request。Temporary preview host 結束後、server session 結束前，必須在相同 MCP server session 以 `resourceRead.method`（`resources/read`）和 `resourceRead.params` 讀取。其他值（包含 `base64`）會在 preview work 開始前回傳 `InvalidArgument`。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

此 tool 只會寫入隔離的 temporary preview directory，compile smoke 後會刪除。Preview project 會依每個 pack 的安全 metadata 產生 structural stubs，因此 engine 不會硬編碼 WPF UI，也不會執行 pack code。每個已完成的 preview result 都會回報 `visualFidelity="structural-stub"` 並提供 `visualValidationGuidance`：preview screenshot 只適合當作結構證據，不可用來核准最終 styling。`visualComparisonChecklist` 會精簡並列 preview 與 final app 在 window chrome、icons、control templates、layout/spacing 的已知差異，以及每一項必要的 final-app 檢查。請 apply blueprint、build 並 launch 真實 WPF application，再檢查該 application 完成最終視覺驗證。成功結果包含 `buildSucceeded=true`、generated `xaml`、captured `buildOutput` 與 `previewHost` summary。Runtime diagnostics 是 opt-in，會回傳在 `previewHost.runtimeDiagnostics`。成功的 `screenshotOutputMode="file"` resource 會與短生命週期 preview process 分離，仍受 `SessionManager` 的 24 小時與 100-resource 上限管理，並在 expiry、eviction 或 server session-manager disposal 時移除。Build failure 會回傳 diagnostics，能在可用時對應回 blueprint root 與 renderer template path。

`propertyWarnings` array 只包含 submitted blueprint 明確使用之 properties 的 pack-defined warnings。每個 entry 都會回報精確的 `jsonPath`、`blockKind`、`propertyName` 與 `message`，讓 Agent 將 final-app validation 聚焦在受影響的 layout 或 styling decision，而不必把所有 structural-preview limitation 視為同等相關。

`elementCorrelations` array 會將每個 renderer root 的 transient `x:Name`（`elementName`）對應到精確 blueprint `jsonPath` 與 `blockKind`。Agent 可把該 name 與 `get_ui_summary` 或 focused runtime result 配對，將 preview evidence 連回 authored node。Correlation name 只存在 temporary preview XAML，不會寫入或儲存在 blueprint，也不會由一般 render 或 apply workflow 輸出。

## `repair_ui_blueprint`

將 validation、render、compile 或 preview diagnostics 轉成 blueprint-first repair actions。當 `validate_ui_blueprint`、`render_ui_blueprint` 或 `preview_ui_blueprint` 回傳 issues 後使用。

Request options:

- `blueprintJson`: required UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`。
- `diagnosticsJson`: optional diagnostics JSON object 或 array，可使用 render 或 preview result 中的 diagnostics。
- `targetPath`: optional target XAML path suggestion，只用於 render diagnostics。此 tool 不會寫入。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

Response 包含 `repairable`、`generatedXamlPatch=false`、`actionCount` 與 `actions`。只有內容等價的重複 guidance 才會合併成一個 action；僅有相同 `issueCode` 與 `jsonPath` 不會合併不同 message、suggestion、value 或 renderer path。對於合併後的等價 guidance，`source` 保留第一個觀察來源，ordered `sources[]` 列出所有 validation、renderer 或 diagnostic contributors。Actions 會標示 repair 應落在 blueprint 或 pack renderer template contract。此 tool 不會直接 patch generated XAML。

## `apply_ui_blueprint`

為 UI blueprint 產生 guarded apply plan。預設為 dry-run，讓 Agent 能在任何寫檔前檢查 generated view file path、required resources、package plan 與 binding contract stub。

Request options:

- `blueprintJson`: required UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`。
- `projectRoot`: required local WPF project root，用於 path planning 與 write allowlist checks。
- `targetPath`: optional target XAML file path，必須位於 `projectRoot` 內。
- `dryRun`: optional boolean，預設為 true。
- `confirmApply`: optional boolean，non-dry-run 寫入前必須在檢查 dry-run plan 後設為 true。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

非 dry-run 寫入需要 `confirmApply=true`、`WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`、`WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true`，且 `WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS` 必須 exact match。成功的 confirmed response 會保留依照寫入前 target 狀態執行的 file plan：新寫入檔案仍為 `action="create"`，既有檔案仍為 `action="update"` 並回報 backup path。此 tool 會拒絕 `projectRoot` 外的路徑、更新既有 view 前建立 backup、加入 `WPFDEVTOOLS_BLUEPRINT_SOURCE` header、保留 `WPFDEVTOOLS_SAFE_SLOT` manual-edit markers，且不會執行 NuGet restore。

## 從 apply 到可執行應用程式

`apply_ui_blueprint` 只會寫入經審查的 view XAML；它不會暗中修改 project file、application resources、code-behind、ViewModel 或 startup flow。請把 response 內的 plans 當成 authoritative integration checklist：

1. 先執行 dry apply，檢查 `filePlan`、`requiredNuGetPackages`、`resourcePlan`、`viewModelBindingContract` 與 `behaviorIntegrationContract`。
2. 只有在 project-root gates 已精確限制到目標專案後，才執行 confirmed apply。
3. 加入 `requiredNuGetPackages` 列出的所有 package。未使用 central package management 的專案可使用：

   ```xml
   <PackageReference Include="WPF-UI" Version="4.3.0" />
   ```

   若 `ManagePackageVersionsCentrally=true`，請移除 `PackageReference` 上的 `Version`，並在 `Directory.Packages.props` 設定：

   ```xml
   <ItemGroup>
     <PackageVersion Include="WPF-UI" Version="4.3.0" />
   </ItemGroup>
   ```

4. 在 `App.xaml` 宣告 WPF UI namespace，並加入 response 所列的 dictionaries：

   ```xml
   <Application xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml" ...>
     <Application.Resources>
       <ResourceDictionary>
         <ResourceDictionary.MergedDictionaries>
           <ui:ThemesDictionary Theme="Dark" />
           <ui:ControlsDictionary />
         </ResourceDictionary.MergedDictionaries>
       </ResourceDictionary>
     </Application.Resources>
   </Application>
   ```

5. 若 `filePlan` 包含 `role="code-behind-integration"`，generated XAML 的 `x:Class` 與 code-behind 必須使用相同 base type：

   ```csharp
   public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
   {
       public MainWindow() => InitializeComponent();
   }
   ```

6. 將 `behaviorIntegrationContract.status="required"` 視為 release gate。必須在 view DataContext 實作每個 `commandPath`。Navigation command 會收到 `commandParameter`，且必須更新 selected application state 與 destination content；action command 必須產生可觀察的應用程式行為，並提供合適的 `CanExecute` policy。Built-in navigation recipe 預設使用 `NavigateCommand` 與 `PrimaryActionCommand`。這些是 application contracts，不是自動生成的 business logic。
7. 分開執行 restore、build 與實際 application launch：

   ```powershell
   dotnet restore .\YourApp.csproj
   dotnet build .\YourApp.csproj --no-restore
   dotnet run --project .\YourApp.csproj --no-build
   ```

8. 驗證實際執行中的 app，而不只檢查 structural preview。使用 `connect`、`get_ui_summary`、focused element reads 與 `element_screenshot(outputMode="file")`。逐一觸發 `behaviorIntegrationContract` 中的 interaction，確認 state 或 visible content 發生變化。任何 diagnostic mutation 都應搭配 `capture_state_snapshot`、`get_state_diff` 與 `restore_state_snapshot`。

不要因為 generated application 可以 compile，或 button 顯示為 click-ready 就核准結果。Command-bound control 必須完成 DataContext command，且在 launched application 中驗證可觀察結果後，才算完整。
