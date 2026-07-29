# WPF DevTools beta.99 E2E feedback: WPF UI Store reference

## Result and screenshot

This public prerelease validation passed at 9.6/10. The final native-MCP screenshot is [City Bloom Cabinet](assets/2026-07-26-wpfui-store-beta99-rerun-codex-e2e/final-runtime.png): a narrow navigation rail, broad search band, editorial hero, compact collection strip, product cards, and clear primary action all fit within the live 1440×900 viewport.

## What worked well

Focused runtime catalog discovery was sufficient to create a non-derivative marketplace identity while retaining the reference's visual hierarchy. Immutable draft operations, validation, dry-run apply, confirmation, and integration produced a dependable progression. The runtime tool surface was particularly useful for examining semantic layout, capturing a property snapshot, diffing a single controlled mutation, restoring it, and confirming the restored value.

## Workflow observations

The public installer delivered the expected 1.0.0-beta.99 STDIO server. Keeping its standard input alive while awaiting output yielded valid `initialize`, `tools/list`, and `resources/list` replies; the protocol list exposed 77 tools and 12 resources. Chunked screenshot resources were practical: reading advertised chunks once in ascending offset, decoding, concatenating, and checking SHA-256 gave a deterministic evidence file.

## Friction and recovery

The first composition used an unconstrained vertical stack, which left later content visually weak in the live viewport. A final pixel review caught this. Replacing the outer stack with a bounded grid and replacing an invisible card-hosted search area with a core border/stack visibly fixed the issue without source edits or package changes. Process-local draft expiry was also recoverable because Composer's response identified the correct fresh-draft path.

## Suggested improvements

- Add a Composer preview diagnostic for content that extends beyond a fixed root viewport, especially a vertical stack with several tall children.
- Include a small visual-presence assertion in preview output for named components whose layout is non-zero but whose child content may render blank.
- Document the STDIO lifecycle expectation that clients should keep stdin open while awaiting JSON-RPC responses.
- Preserve the chunk metadata and add a concise copyable extraction recipe beside screenshot resource responses.

## Closing

The beta.99 workflow was capable of a complete public-install-to-cleanup E2E run using only native WPF MCP interaction. The final result was high-fidelity at the composition level while remaining creatively distinct from the supplied reference.
