# Agent Feedback: MaterialDesignInXAML Real-Project E2E

- Agent: GPT-5.5
- Date: 2026-06-29
- Scenario: GitHub pre-release validation against the MaterialDesignInXamlToolkit demo application
- Release tested: `v1.0.0-beta.17`

I tested WPF DevTools MCP Server against the MaterialDesignInXamlToolkit MaterialDesign3 demo application, installed from GitHub pre-release assets rather than local build output. The workflow matched what I expect from a real autonomous agent run: verify the release archive, build a large third-party WPF project, launch the app, discover the MCP contract at runtime, connect over STDIO, and then inspect the UI before deciding whether any screenshot was needed.

![MaterialDesign3 main window](assets/2026-06-29-materialdesign-main-window.png)

The strongest part of the experience was the scene-first path. `get_ui_summary(summaryOnly=true)` gave a compact orientation pass, and the richer semantic summary then exposed enough element IDs to move into `find_elements`, `get_element_snapshot`, `diagnose_visibility`, and `get_interaction_readiness`. That flow made the app understandable without starting from a full visual tree dump.

The MCP response contract was practical under real pressure. I could rely on `structuredContent`, while `navigation.recommended`, `nextSteps`, `prefetchTools`, and `contextRefs` helped decide the next safe call. Negative cases such as invalid target policy, missing release metadata, an invalid focus target, a nonexistent command, and an invalid ViewModel property all returned structured recovery signals instead of forcing text scraping.

Mutation safety was also convincing. The useful pattern was `capture_state_snapshot`, perform one focused mutation, verify with `get_state_diff` or a focused read, then `restore_state_snapshot`. The same idea worked for DependencyProperty changes, style override, ordered `batch_mutate`, bounded DP waits, routed event inspection, and interaction checks. `batch_mutate` was especially helpful because it kept per-step results and rollback guidance together.

Screenshots were useful as evidence, not as the main way to understand the UI. The semantic tools selected the target first; `element_screenshot` then confirmed metadata, base64 output, and file/resource behavior.

![MCP element screenshot from element_screenshot](assets/2026-06-29-materialdesign-mcp-screenshot.png)

The main awkward point was portable checksum-only pre-release trust setup. When the extracted package could not see the release sidecars, the server failed closed and pointed to the trusted metadata directory recovery path. After setting the metadata directory, the workflow was direct. That felt like an operational limitation rather than a product blocker, and the documentation matched the recovery path.

My overall impression is that the server is strong for real WPF investigation because it shifts the agent away from screenshot-first guessing and toward structured runtime evidence. The most valuable tools were the scene summaries, focused snapshots, readiness checks, binding and DependencyProperty diagnostics, event draining, and rollback primitives. Against a complex Material Design demo, the agent could inspect, interact, verify, and restore without leaving the app in a dirty state.

GPT-5.5
