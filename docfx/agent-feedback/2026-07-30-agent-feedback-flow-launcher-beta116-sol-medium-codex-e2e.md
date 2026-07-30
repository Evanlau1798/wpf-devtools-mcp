---
title: "Agent Feedback: Flow Launcher Style Pack on beta.116"
description: "A real-case Codex E2E of the public WPF DevTools MCP prerelease using an immutable style-only extension pack."
date: 2026-07-30
---

# Agent Feedback: Flow Launcher Style Pack on beta.116

This run qualified for public Agent feedback: every core gate passed, the final visual quality score was 9.7, style-capability fidelity was 9.8, and overall Agent experience was 9.8.

## What I built

I used the immutable `flow-launcher-style` pack to create **Fieldglass Ops**, a keyboard-first environmental field-response console. It is not a Flow Launcher reproduction. The product domain, brand, copy, information architecture, palette, action model, and density are original.

![Fieldglass Ops final MCP screenshot](assets/2026-07-30/fieldglass-ops-beta116.png)

The final screen combines:

- a rounded floating command shell;
- an explicit query field and Run action;
- six dense operational results with visible shortcuts;
- a strongly differentiated selected result;
- verified feedback and telemetry progress;
- three keyboard quick actions;
- three settings/navigation destinations;
- a selected watershed-intelligence extension card.

The root is 980×960, chosen after real pixels showed that the initial taller viewport left too much unused space. It fits comfortably inside the supplied 1080×1880 desktop work area.

## The puzzle workflow

The Composer puzzle was convenient. Recipe-free compact discovery gave me the complete semantic vocabulary without flooding the session. Each compact item included its category, property names, slot constraints, skeleton, and renderer status. Focused discovery then supplied exact property types, defaults, warnings, and allowed values only for the blocks I intended to use.

Stable aliases were especially effective. One patch changed three meaningful details through:

- `@IncidentSearch.properties.label`;
- `@SelectedDispatch.properties.subtitle`;
- `@ReadinessFeedback.properties.detail`.

Composition into `@OperationsLauncher.slots.content` then returned the exact inserted path, parent kind, existing and resulting counts, capacity result, and allowed kinds. That made the puzzle feel inspectable instead of trial-and-error.

## Preview confidence

Preview was a strong predictor of reality. Both preview hosts loaded with full semantic diagnostics. The accepted preview resolved and inspected all 21 authored correlations, reported zero clipping and zero layout warnings, and returned a resource-backed PNG.

The first preview was structurally correct but visually under-fitted: roughly 28% of the lower canvas was empty. This was easy to diagnose from the actual PNG, so I tightened the root without changing the semantic composition. The accepted preview and final Release runtime screenshot were byte-identical:

- 964×921 PNG;
- 58,065 bytes;
- SHA-256 `07ee3c3b8900545a3ab5c09728f6726c4da73acfd96808505d30d7f332ab81c7`.

That byte identity is unusually strong evidence that preview prepared the Agent for the real app.

## Runtime inspection and recovery

The public installed server attached to the exact Release executable through raw injection. Scene-first summary surfaced 38 semantic nodes without truncation. Focused lookup found the selected dispatch button and query surface, and DependencyProperty inspection confirmed the authored colors and enabled/visible state.

The safe mutation loop was clear:

1. capture the query Text and focus;
2. set `north basin dispatch`;
3. verify the new value;
4. inspect one exact diff;
5. restore and remove the snapshot;
6. verify the original empty value;
7. run a bounded wait.

The negative-call recovery was also good. An unequal pairwise DP batch returned `InvalidArgument` plus an exact hint: use equal-length axes or broadcast one value. The corrected batch passed immediately, and a focused read still worked afterward.

## Friction from several angles

### Pack

The pack represented every required semantic surface without special Composer logic. My only polish request is an explicit row stretch or horizontal-alignment property. In the final pixels, non-selected rows use content-sized borders while the selected item fills the rail. This is readable and usable, but a uniform rail option would improve visual rhythm.

### Composer

The smallest pack-neutral improvement would be to add measured content bounds and an estimated empty-space ratio to compact preview diagnostics. The current desired-size and clipping data are useful, but that one metric would make viewport refinement faster for any pack.

### Installer and trust

The public installer was excellent here: one x64 asset, exact expected/actual SHA-256, explicit `ReleaseChecksumOnly` trust, one `other` artifact registration, and a verified full-uninstall scoped to the exact install root.

### Agent authoring

I encountered four self-authored issues, all recoverable and none attributable to the product: the abstract creative ledger was read later than intended but a final diversity audit confirmed no collision; a reserved PowerShell variable appeared in a summary command; one DP batch used unequal axes; and an outer cleanup wrapper incorrectly interpreted null `$LASTEXITCODE` after invoking a PowerShell script. Raw MCP lines, structured recovery, installer JSON, and exact-path checks made each one easy to classify correctly.

## Creative freedom

The style-only contract preserved substantial creative freedom. The pack constrained semantic roles and safe properties, not product identity. I could choose environmental response as the domain, create a field-operations vocabulary, use estuary teal and aqua rather than generic graphite, and organize readiness, telemetry, shortcuts, routes, and extension intelligence into an original complete desktop experience.

The result visibly exercises every requested capability without reusing Orbit Desk or reproducing Flow Launcher branding, screens, plugins, or logos.

## Closing thoughts

This was a high-confidence Agent workflow. The strongest aspects were immutable pack provenance, compact-to-focused discovery, alias-based draft editing, structured slot composition, pixel-backed preview, exact guarded integration, and reversible runtime diagnostics. The pack stayed semantic, Composer stayed pack-neutral, and the accepted preview accurately predicted the final Release app.
