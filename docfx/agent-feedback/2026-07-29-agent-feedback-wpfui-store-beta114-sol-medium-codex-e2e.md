# WPF DevTools MCP beta.114 — WPF UI Store-reference Agent feedback

Run: `wpfui-store`, Codex `gpt-5.6-sol` / medium, 2026-07-29.

![Final Aurora Field Exchange application](assets/2026-07-29-wpfui-store-beta114-sol-medium-codex-e2e/20260729-161606-wpfui-store-final.png)

## Closing result

This public prerelease journey passed end to end. I independently installed the public x64 release, qualified all 77 installed MCP tools over STDIO, discovered only the installed `wpfui` and `core` contracts, composed and applied an original desktop marketplace, built and launched Release, used MCP-only runtime inspection and screenshots, repaired a pixel-visible hero defect through Composer, completed state/wait/recovery gates, and performed public full-uninstall.

The final screenshot is 1152 × 768, 796050 bytes, SHA-256 `a99cc4e84b03b904f519e0183b0c6208d09e8d84d7c9e622a384fc4aff5f99b3`.

## Usage impressions

The pack-neutral workflow now feels unusually concrete. `list_ui_block_packs` established roles and variants without revealing local pack paths. A single compact catalog call exposed 28 composable kinds, including meaningful authoring roles for `core.image` and `wpfui.editorialCard`. Exact-kind focused calls then supplied the property descriptions, enum values, singleton bounds, and skeletons needed to author confidently.

The strongest contract detail was the NavigationView pane guidance: `LeftMinimal` explicitly says to combine it with `isPaneOpen=false` for a narrow icon rail. That removed a potentially expensive preview-guess loop and matched the measured reference rail.

Media guidance was similarly actionable. Both image-capable kinds described the safe project-owned URI forms, rejected external/file-system/traversal sources, and explained that integration would declare WPF Resources. The final project contained 18 distinct original image assets, and the reviewed integration plan added exactly 18 Resource items.

## Puzzle workflow assessment

The block/slot workflow was convenient:

- aliases were copy-ready and immutable;
- `@HeroFeature.properties.*` made a meaningful revision compact;
- composition reported the resolved JSON path, inserted node summary, existing/resulting counts, and capacity;
- validation accepted opaque draft refs, avoiding repeated 20 KB blueprint transport;
- the guarded apply separated view write from package/resource/startup/base-type integration;
- integration required the exact reviewed plan hash and returned rollback paths.

The draft lifecycle depended on keeping one server alive, but the retention metadata made that constraint explicit. One installed server process successfully carried the run from initialization through discovery, drafting, previews, apply, two runtime connections, and the final repair.

## Preview versus final application

Preview accurately prepared layout, theme, control templates, hierarchy, density, and clipping decisions. It also explicitly warned that isolated preview might not resolve target-project image resources. That warning was honest: preview showed fallback/blank media, while the applied application loaded all 18 images.

A useful future improvement would be a bounded, reviewable project-resource staging plan for preview. With the exact allowlisted project root and the existing URI validator, Composer could show which Resource images it proposes to copy into the isolated preview. This would improve media-led design iteration without introducing a WPF UI special case or global trust bypass.

The first applied final screenshot exposed a different issue: the `editorialCard` hero copy/actions were semantically visible but absent from pixels after project resources loaded. I did not accept semantic visibility as proof. A Composer-only repair replaced that hero with `wpfui.card + core.grid + core.image + explicit text/action stack`. Re-apply, build, relaunch, and MCP recapture closed the defect; the final Save action reports `clip=none`, `visibleRatio=1`.

## Friction from several angles

### Composer/product

No unresolved product-targeted P0/P1/P2/P3 remained. The project-resource preview limitation was disclosed before apply and closed by final-app evidence.

### Built-in pack

The focused WPF UI pack contract was sufficient to build a complex desktop experience without engine special cases. A future pack improvement could add an example or skeleton showing an image plus explicit text overlay/adjacent content using only generic grid/card primitives; this would complement `editorialCard` without changing Composer.

### Harness/client

A synchronous shell-launched STDIO controller was initially terminated by a one-second tool timeout. Running the controller as a hidden background process solved it. The controller kept stdin open, flushed initialize, read the correlated response before initialized, persisted every stdout line before parsing, and drained stderr with `ReadToEndAsync()`.

Large screenshots required disciplined chunk assembly. The final pre-repair image used 50 chunks; the repaired image used 49. Every chunk was read once in ascending offset order, decoded, concatenated, and verified against advertised byte length and SHA-256.

### Agent authoring

Several mistakes were recoverable and correctly classified: one wrong receipt subpath, an accidental block-kind-as-tool call, a placeholder draftRef queued too early, and a nested mutation processId rejected by schema. None became a product finding; each structured response and correction was retained.

## Creative freedom and reference grounding

The visual reference constrained architecture, not identity. Aurora Field Exchange uses an original outdoor micro-expedition domain, original copy, original generated imagery, and a light mineral/teal/amber visual identity. It still preserves:

- a shallow top search band;
- a compact persistent icon rail;
- a five-zone image-led hero;
- a seven-image overlapping promo rail;
- a heading/action transition;
- a dense six-card shelf continuing below the fold.

Measured final fidelity:

- aspect-ratio delta: 0.0781%;
- maximum anchor-edge delta: 0.063;
- hero entity ratio: 83.3%;
- promo and product ratios: 100%.

This was enough structure to keep the result visibly reference-informed while leaving domain, media, vocabulary, palette, controls, and repair strategy genuinely creative.

## Smallest pack-neutral Composer improvement

Add `projectResourceStagingPlan` to `preview_ui_blueprint`:

1. enumerate only blueprint-referenced, project-owned resource URIs;
2. resolve them beneath the exact allowlisted project root;
3. return source-relative paths, byte lengths, SHA-256 values, and bounded copy actions;
4. require a call-scoped reviewed plan hash before staging;
5. preserve current external/path/traversal rejection and avoid persistent trust.

This would improve all media-capable packs without adding pack-specific Composer logic.

## Final thoughts

The combination of compact/focused discovery, opaque immutable drafts, exact aliases, slot-aware composition, runtime-backed preview, hashed guarded apply, and deep WPF runtime evidence is coherent. The most important positive behavior was recovery quality: authoring mistakes returned specific structured guidance, while pixel-visible defects could be repaired through the same Composer model rather than by escaping to handwritten XAML.
