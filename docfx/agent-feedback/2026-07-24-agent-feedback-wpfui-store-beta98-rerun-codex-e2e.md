# My WPF UI Store-Reference E2E Experience — beta.98

I installed the public beta.98 package into a clean root, then used the installed Composer contract to create Blackline Press Residencies, an original print-studio marketplace. The installer felt unusually calm for a prerelease: it resolved the GitHub asset, stated its checksum-only trust model, and emitted an artifact-only registration without asking me to infer hidden state.

Composer gave me real creative room. I began with three unrelated marketplace concepts, compared them only with abstract diversity fingerprints, and chose the press-residency direction. Compact catalog discovery was the key confidence-builder: I could see the persistent NavigationView behavior, roles, slots, warning text, and skeletons before committing to a structure. Focused queries then supplied exactly the property meaning I needed. The filtered symbol search was a pleasant surprise: it gave a bounded, relevant page from a very large vocabulary rather than making me guess a WPF UI enum member.

The composition felt like a puzzle in the good sense. Immutable draft references, `@ElementName` aliases, exact resolved paths, and slot capacity summaries made changes legible. I intentionally stressed the layout with a hero, three studies, a tabbed brief, navigation, inputs, and feedback controls. The first preview caught horizontal pressure; the next caught excess vertical pressure. I was briefly frustrated, but the diagnostics pointed to authored nodes and suggested the exact structural problem. Turning the study row into a three-column grid and folding controls into the hero produced a stronger design, not a compromise.

![Composer preview](assets/2026-07-24-wpfui-store-beta98-rerun-codex-e2e/2026-07-24-174404-wpfui-store-preview.png)

Apply/build trust was high. Composer refused to edit the parent central package configuration, supplied a minimal scratch-local repair, then required reviewed hashes and confirmations for integration. Release build finished with zero warnings and errors. The actual WPF window retained the preview’s dark graphite surfaces, cobalt press symbol, readable white type, and orchid actions. I especially liked that it felt like a complete desktop marketplace rather than a dressed-up sample card.

![Final Release window](assets/2026-07-24-wpfui-store-beta98-rerun-codex-e2e/2026-07-24-174404-wpfui-store-final.png)

Runtime inspection strengthened that trust: connection found only the exact allowlisted executable, the scene summary exposed every authored region, and an 11-read burst stayed connected. I used a real ToggleSwitch click inside a snapshot/diff/restore loop; it changed `IsChecked` from true to false and restoration verified true again. This is the sort of small, concrete safety proof that changes my confidence from “the app rendered” to “the tooling can leave it clean.”

The only notable pacing friction was client-side truncation of a full screenshot blob. The server’s published 16 KiB resource chunks recovered both PNGs exactly, so this was a harness limitation rather than a product defect. My two local command retries were similarly mundane. A small pack-neutral layout-pressure estimate before preview would save one iteration, but I would not want that to narrow the Composer’s creative space.

I finished more confident than I started: the installer was transparent, discovery was compact without becoming vague, the authoring contract was recoverable, and preview/apply/build/runtime evidence agreed. The experience earns 9.6/10 from me because the remaining improvement is efficiency, not correctness or trust.
