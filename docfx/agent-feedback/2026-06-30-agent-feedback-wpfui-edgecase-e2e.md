# Agent Feedback: wpfui Edge-Case E2E

- Agent: GPT-5.5
- Date: 2026-06-30
- Scenario: GitHub pre-release validation against a custom WPF UI edge-case app
- Release tested: `v1.0.0-beta.19`

I installed WPF DevTools MCP from the public online installer with `-Prerelease`, and the installer resolved GitHub pre-release `v1.0.0-beta.19` to the win-x64 package. The installed server was used directly from the scratch install root; no source build output from this repository was used as the server under test.

For the target application, I cloned `lepoco/wpfui` under the scratch directory to record the upstream commit and MIT license, then created a separate WPF app using the `WPF-UI` NuGet package. The app intentionally resembled a Windows 11 Microsoft Store surface: left navigation, search, a hero product area, virtualized catalog cards, account settings form, dialog, flyout, hidden/inactive/off-screen targets, templated controls, routed events, ViewModel commands, validation, and safe mutation targets.

![WPF UI edge-case store window](assets/wpfui-edgecase-2026-06-30/main-window.png)

The MCP workflow felt agent-friendly. Runtime discovery returned 64 tools, plus `wpf://contracts/tools` and `wpf://contracts/response`. `connect()` auto-discovered the single allowlisted target, and `get_ui_summary(depthMode="semantic")` gave enough semantic context to avoid starting with screenshots or full tree dumps. `get_namescope`, `find_elements`, `get_element_snapshot`, `diagnose_visibility`, and `get_interaction_readiness` were the most useful targeting tools.

Mutation and recovery were practical. I used `capture_state_snapshot`, `set_dp_value`, `get_state_diff`, `restore_state_snapshot`, `batch_mutate`, `click_element`, `simulate_keyboard`, `execute_command`, and `modify_viewmodel` against the live app. A deliberately failed batch mutation returned a snapshot id and rollback guidance, and restore completed. A deliberately failed hidden-element focus attempt returned a structured `ElementNotLoaded` response rather than leaving the session ambiguous.

![MCP hero element screenshot](assets/wpfui-edgecase-2026-06-30/mcp-hero-screenshot.png)

Screenshots were useful as evidence, not as the primary navigation path. Scene-first calls identified the right elements first; screenshots then confirmed the store-style hero region and restored window state.

Subjectively, the release path was ready for an autonomous agent validation loop. The main thing an agent must do carefully is enable the correct gates for the exact workflow, especially `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` when snapshots, commands, or ViewModel mutation are in scope. The documentation and tool errors both surfaced that requirement clearly.

Strict validation result: PASS. P0, P1, P2, and P3 counts were all zero.

GPT-5.5
