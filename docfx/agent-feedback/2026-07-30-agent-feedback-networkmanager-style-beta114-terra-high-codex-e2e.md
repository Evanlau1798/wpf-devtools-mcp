# WPF DevTools MCP E2E — NETworkManager style / Signal Atlas

## Executive summary

**PASS.** I installed public prerelease `v1.0.0-beta.114`, imported the supplied immutable `networkmanager-style` pack, created and applied an original WPF desktop product, built and launched the exact Release executable, inspected it through a fresh installed MCP server, exported and visually inspected both preview and final MCP PNGs, exercised safe runtime recovery, and fully uninstalled the public installation.

Signal Atlas is an original deterministic relay-observability workspace. It uses no network scanning, credentials, vendor branding, copied screens, upstream source, or external reference image. The overlay replaces the image-receipt requirement with its pack capability brief.

## Environment and public-path trust

- Work area/root target: 1920 × 1040 DIPs (1.846:1); final MCP capture: 1904 × 1001 PNG.
- Installer source: `https://installer.wpf-mcptools.evanlau1798.com/`.
- Resolved release: `v1.0.0-beta.114`, x64, GitHub release asset `release_1.0.0-beta.114_win-x64.zip`.
- Public archive SHA-256 verified by installer: `fb90aeee752d2d5b7d732505f9c4621086e996c17aa1539f7fc4bd6b91e14379`.
- Trust policy: `ReleaseChecksumOnly`; installer output and installed path are preserved in `evidence/installer-install-output.txt`.
- Direct STDIO qualification kept stdin open through `initialize`, flushed, read the correlated response, then sent `notifications/initialized`. Native `tools/list` returned **77** tools; `resources/list` returned 12 resources. Raw-line-first transcripts are in `evidence/transcripts/qualification-retry.ndjson`.

## Pack provenance, concept, and discovery

- Supplied archive: `networkmanager-style-0.1.0.zip`.
- Required and observed SHA-256: `5A5DF7F54F0661420E1AFE739BA1B3DD32F1DF59FCEE482D2B69532EECFF38A5`.
- Import was dry-run reviewed, then confirmed with the same hash. The project-local pack catalog contained core, WPF UI, and networkmanager-style.
- Compact discovery preceded focused reads of all 12 style block contracts; recipe expansion occurred only after independent concept selection and was read-only evidence.

Three candidate concepts were recorded: Signal Atlas, Route Ledger, and Edge Pulse. Signal Atlas was selected for the clearest semantic fit. Its final hierarchy is a text navigation rail, command band, scoped filter, four metric cards, tabbed six-row diagnostic table, telemetry progress, and guarded watch toggle. A core grid provides desktop density without pack-specific Composer code.

## Composer evidence

The successful final lifecycle used one installed server process from draft through patch, composition, validation, render, preview, apply, and integration:

- Created immutable draft, patched `@WorkspaceShell.properties.title`, then validated the patched source draft before composition.
- Composed `networkmanager-style.statusToggle` into `@WatchCell.slots.content`; the source draft remained immutable and the derived draft validated.
- Render dry-run succeeded. Preview host status was `loaded`, `viewLoaded=true`, and its runtime diagnostics matched all named authored elements.
- Guarded `apply_ui_blueprint` dry-run and confirmed write succeeded, followed by advertised `apply_ui_project_integration`.

One authoring recovery occurred before the successful cycle: the initial source used scalar values where the focused contracts required bindings (`selectedIndex`, progress `value`, and `isIndeterminate`) and omitted the required root `name`. I repaired the source blueprint, revalidated it, and continued; this was Agent-authoring friction, not a pack or product fault. The first preview also exposed an overly vertical content flow; focused core-grid discovery enabled a single evidence-led layout revision. A final short-title revision removed rail text clipping.

## Preview and final pixels

Preview PNG (final revision): `evidence/screenshots/signal-atlas-retitle-preview.png`, exported from same-session chunk resources, SHA-256 verified. It shows a compact 1200 × 650 host (1024 × 528 returned screenshot cap) with all four metrics, both tabs, and all six visible rows.

Final PNG: [signal-atlas-final.png](assets/2026-07-30/signal-atlas-final.png), resource URI `wpf://screenshots/shot_38e1b5ee1d2e49489eabd5024828aec1`, 1904 × 1001, 69,466 bytes, SHA-256 `65255477e46ee40e67df876e598c4bda46a35c03c840d155f43972d95f3d43b4`.

Pixel critique: the final screen has clear dark-surface hierarchy; high-contrast white labels; coherent cyan/blue/amber/coral operational states; readable column headers and six dense diagnostic rows; visible selected-state/status cues; and no observed overlap, clipped title, unreadable copy, or missing primary surface. The four-card metric band, table-led browse surface, and persistent rail give the intended desktop density. The remaining lower whitespace is deliberate breathing room at the 1040-pixel desktop height, not an empty primary region.

## Build, launch, and runtime inspection

- `dotnet restore` succeeded.
- Release build succeeded with **0 warnings, 0 errors**.
- Exact executable launched from `composer-generated-app/bin/Release/net8.0-windows/ComposerGeneratedApp.exe`.
- A fresh policy-scoped server connected to the generated app. Scene-first sequence succeeded: `get_ui_summary` (72 semantic nodes) → exact `find_elements` for WatchToggle → `get_element_snapshot` → `diagnose_visibility`; focused `Title` DP inspection also succeeded.

Safe state flow: captured a Title snapshot, set it to `Signal Atlas · validation pulse`, verified the DP, drained events, obtained a state diff, restored the snapshot, and verified restoration. A safe nonexistent-element call returned structured `ElementNotFound` recovery guidance. Evidence is in `evidence/transcripts/runtime-retitle-final.ndjson` and `evidence/runtime-final-summary.txt`.

## Qualification gates and findings

All 12 gates are checked with paths in `evidence/qualification-checklist.md`.

No unresolved P0/P1/P2/P3 product, pack, installer, or environment finding remains.

- **Agent authoring / resolved:** scalar-versus-binding property mistake in the first draft; fixed after contract validation.
- **Agent authoring / resolved:** first preview was vertically sparse; core grid revision brought metrics and diagnostics onto the first desktop screen.
- **Agent authoring / resolved:** long rail title clipped in final inspection; final Composer revision shortened it to `Signal Atlas`.
- **Harness friction / resolved:** PowerShell rejects `-File` for a `.html` installer evidence copy; the same downloaded content was copied to a `.ps1` evidence file and succeeded.

## QA and scoring

| Dimension | Score | Evidence-based assessment |
|---|---:|---|
| Installation and public-path trust | 9.8 | Public installer, GitHub asset, checksum, 77 tools, and full-uninstall all verified. |
| Pack-neutral discovery and Composer usability | 9.7 | Compact-to-focused contracts directly supported authoring; core grid integrated without special Composer behavior. |
| Preview/apply/build confidence | 9.7 | Hash-verified preview, guarded apply/integration, then clean Release build. |
| Runtime inspection and recovery | 9.8 | Connection, scene-first diagnostics, DP read, negative call, snapshot/mutate/diff/restore, and event drain passed. |
| Final visual quality | 9.7 | Dense, readable, polished desktop composition with explicit state colors and no observed defects. |
| Reference-informed visual fidelity | 9.7 | N/A image reference; overlay capability brief was met with the required wide desktop hierarchy and all requested surfaces. |
| Attention/context efficiency | 9.6 | Focused contracts and one substantive layout revision avoided broad source/library exploration; early draft correction cost one retry. |
| Overall Agent experience | 9.7 | The public flow completed with clear recovery routes and no unresolved friction. |

1. **Was the puzzle-like block/slot workflow convenient?** Yes. Slots made the admin shell, core grid, tab content, table rows, and toggle placement explicit and safe.
2. **What required the most guessing?** Binding-typed properties initially looked scalar-capable from their visual meaning. Focused contracts surfaced the correction promptly.
3. **Did compact-to-focused discovery preserve enough semantics and save context?** Yes. Compact discovery identified roles; focused contracts supplied exact slots, types, and bounds.
4. **Did preview prepare the Agent for the real app?** Yes. It exposed the vertical-flow issue before apply; the final runtime capture confirmed the grid repair at full target size.
5. **How should the selected pack improve without Composer special cases?** Include a compact visual-layout hint indicating that admin-shell content is a vertical host unless a core layout container is supplied.
6. **Smallest pack-neutral Composer improvement?** Flag likely first-screen vertical overflow when a stack-like parent receives multiple large content blocks, and suggest discovered core grid/stack containers.
7. **What friction occurred?** Resolved Agent draft type errors, one PowerShell suffix issue, and an evidence-led layout/title refinement; no unresolved product or pack fault.
8. **Was creative freedom preserved?** Yes. Semantic capabilities supported an original professional observability product with fictional deterministic data.
9. **How closely did the final app preserve required broad traits without copying?** It satisfies the overlay’s wide desktop hierarchy, density, navigation, command/filter, metrics, diagnostics, tabs, progress, and status requirements while using original brand, labels, values, and topology.

## Cleanup

The generated app and each independently started MCP server were stopped. Public `full-uninstall` removed the exact validation install root; installer output reports `removedInstallation=true`, and `evidence/cleanup-summary.txt` verifies the install root is absent and the run-owned app stopped. One abstract Signal Atlas fingerprint was appended to the creative diversity ledger.

OVERALL RESULT: PASS
Manual approval complete
