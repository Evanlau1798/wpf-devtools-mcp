# Building Emberline Kitchen Readiness with WPF DevTools MCP beta.116

This is subjective feedback from a sole-agent, real-case prerelease run using the immutable `networkmanager-style` 0.1.1 extension pack and WPF DevTools MCP `v1.0.0-beta.116`.

The exercise started without a reference screenshot. The pack's semantic capabilities were the brief: admin shell, navigation, commands, filters, profiles, metrics, a diagnostic table, tabs, progress, and status. That constraint was surprisingly productive. It led to **Emberline Kitchen Readiness**, an original allergy-safe community kitchen rehearsal product rather than another network console.

![Emberline Kitchen Readiness final WPF window](assets/2026-07-30/emberline-kitchen-readiness-beta116.png)

The screenshot above is the original MCP-captured PNG from the applied, Release-built app. It is 1584 x 861, 77,932 bytes, and has SHA-256 `c691de0505b3e5750b446096d484706fc7ee3a785e4b94e0b02650a31876e061`.

## What the public path felt like

The installer experience was unusually disciplined for a prerelease tool. One non-interactive command resolved beta.116, downloaded the x64 GitHub asset, verified its release checksum, installed to an isolated root, and generated only the requested `other` artifact. Direct STDIO initialization was conventional newline-delimited JSON-RPC. The server reported beta.116 and exactly 77 tools.

The strongest part of the run was the boundary between compact discovery and focused authoring. The compact catalog gave me 12 useful names and enough numeric constraints to choose a concept without opening recipe bodies or source. Focused reads then exposed the properties and slot cardinalities I actually needed. I never had to infer a contract from the library name.

## Was the Composer “puzzle” convenient?

Yes. The puzzle metaphor fits:

- packs define the available pieces;
- focused catalog entries describe their edges;
- slots state exactly what can connect;
- aliases make later changes copy-safe;
- immutable draft references keep a complex blueprint out of repeated transport;
- composition inserts one reviewed skeleton and reports the remaining capacity.

I created one real draft, patched the service-profile copy through an alias, validated it before composition, and added a seventh diagnostic row through `@ActiveChecks.slots.rows`. The source draft stayed immutable and the derived result validated cleanly.

That workflow felt more dependable than authoring raw XAML because the server continuously held the pack contract. It also stayed creatively open: nothing in the recipe or pack told me to build a kitchen product.

## Preview was evidence, not decoration

The first preview was valuable because it failed aesthetically while succeeding technically. The pack's default content stack made a clean dark screen, but four metrics and two filters pushed the diagnostic table below the first viewport. Semantic completeness alone would have missed that.

I then focused the generic `core.grid` contract, arranged metrics and filters into wide rows, and previewed again. The ledger became visible with all seven rows. A final copy adjustment removed clipped shell/profile text at the 1280 x 720 preview cap.

The real 1600 x 900 runtime exposed two more details:

1. the selected tab used a light host-native surface while the requested selected foreground was white;
2. the progress footer initially sat too early and contained too few distinct status facts.

Both were repaired through discovered blueprint properties and generic layout blocks. The final app was repeatedly validated, rendered, dry-run applied, confirmed applied, rebuilt, relaunched, and recaptured. No generated XAML was hand-edited.

## What worked especially well

### Pack-neutral composition

The style pack and core layout pack cooperated cleanly. `networkmanager-style` supplied the recognizable admin surfaces; `core.grid` and `core.border` supplied only spatial structure. Composer did not need a NETworkManager special case.

### Guarded project writes

`apply_ui_blueprint` gave a bounded file plan and a deterministic integration plan hash before writing. The confirmed apply created a backup. `apply_ui_project_integration` made `MainWindow.xaml` the actual startup Window. Later no-op integration plans remained hash-protected.

### Runtime continuity

The applied app restored and built with zero warnings and errors. The fresh installed runtime server attached to the exact executable, produced a 90-node scene summary, found named elements directly, reported zero binding errors, and captured the final pixels as a resource.

### Safe state workflow

The selected profile's Opacity was captured, changed to 0.72 through a serialized mutation-plus-wait, diffed, and restored to 1. The wait reached its expected value in 28 ms. Restore recovered the DependencyProperty and focus with no warnings.

## Friction from several angles

### Pack authoring

The selected-tab background token did not visibly control the host-native selected surface in this run. I resolved the app by selecting a dark selected foreground, but the pack would be more predictable if both selected background and foreground were visually authoritative.

The default admin-shell content slot is intentionally simple, yet a wide operational app benefits from a dense grid starter. A pack-provided skeleton could reduce the first preview iteration without constraining creativity.

### Composer product

The smallest broadly useful improvement would be a viewport-fit estimate for StackPanel-rooted compositions. A warning such as “these children are likely to place the last N blocks below a 720-DIP viewport” would preserve pack neutrality and help an Agent choose a grid before compiling a preview.

The current error shapes were good. When a recovered blueprint was accidentally pretty-printed beyond 65,536 characters, the server returned an exact limit and repair direction. Compacting the same document immediately recovered.

### Agent workflow

Pixel discipline mattered more than clever prompting. I had to keep treating screenshot URIs as midpoints: read ordered chunks, rebuild the PNG, verify byte length and SHA-256, then inspect the pixels. That cost more calls, but it prevented semantic summaries from hiding real contrast and density problems.

## Documentation impression

The installed tool descriptions were detailed enough to run the full scenario without repository source. They explained compact versus focused catalog use, draft retention, one-call runtime approvals, preview fidelity, resource-backed screenshots, target Window integration, and restore semantics.

The main opportunity is consolidation. A short “wide desktop style-pack” example combining an admin shell with generic grid layout would connect several individually clear contracts into one discoverable mental model.

## Creative freedom

The pack never forced its upstream product identity. I used its visual language for a completely different domain:

- seven kitchen service routes;
- two operational filters;
- four readiness/profile metrics;
- a two-tab service ledger;
- seven deterministic checks;
- one progress measure and two status facts.

There is no NETworkManager branding, copied screen, upstream icon, network scan, or credential behavior. The final composition still clearly proves the pack is useful for a serious dense desktop workspace.

## Closing thoughts

Beta.116 made the Composer workflow feel like assembling a typed visual system instead of generating a blob of XAML. The strongest design choice is that pack contracts, generic layout, guarded writes, runtime inspection, and pixel resources all remain separate but composable.

For this run, the public path was trustworthy, the puzzle workflow was convenient, preview materially improved the product, and runtime diagnostics closed the loop. I would keep the same architecture and invest next in layout-fit hints and selected-tab template fidelity.

Scores from the completed qualification were 9.7/10 for final visual quality, 9.8/10 for style capability fidelity, and 9.7/10 for overall Agent experience.
