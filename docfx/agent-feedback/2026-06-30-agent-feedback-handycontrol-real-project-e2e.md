# Agent Feedback: HandyControl Real-Project E2E

- Agent: GPT-5.5
- Date: 2026-06-30
- Scenario: GitHub pre-release validation against the HandyControl demo application
- Release tested: `v1.0.0-beta.19`

I validated WPF DevTools MCP Server against the real HandyOrg/HandyControl demo app, installed from the public online installer and GitHub prerelease assets. I launched `HandyControlDemo` and drove the server only through actual MCP STDIO JSON-RPC calls.

![HandyControl demo window](assets/handycontrol-2026-06-30/main-window.png)

The install experience was smooth for an agent. The installer resolved the win-x64 GitHub prerelease asset, wrote an artifact-only registration for `other`, and reported the installed executable path clearly. The checksum-only beta trust model was understandable because the README and release metadata both exposed SHA256 sidecars, although this still requires agents to understand that beta packages may be unsigned.

Discovery worked well. `tools/list` returned 64 tools, `wpf://contracts/tools` and `wpf://contracts/response` were readable, and the response contract made it clear that `structuredContent` is the canonical payload while `content[0].text` is only compact fallback text. I used `navigation.recommended`, `nextSteps`, `prefetchTools`, and `contextRefs` to choose safer follow-up calls instead of guessing.

Scene-first usage was effective. `get_ui_summary` quickly identified the HandyControl main window, SearchBar, named title buttons, hidden restore button, ListBox, and useful runtime element IDs. `get_element_snapshot`, `diagnose_visibility`, `get_interaction_readiness`, `get_namescope`, and `find_elements` let me target most workflows without starting from a full tree dump.

The main scene-level friction was data-templated navigation: visible list labels were easier to understand from the screenshot than from the semantic ListBoxItem content, which surfaced model type names.

Mutation and restore safety were strong. I used snapshot/diff/restore around focus, keyboard, click, routed event, DependencyProperty mutation, style override, wait-after-mutation, and `batch_mutate`. `get_state_diff` showed exact property changes, and `restore_state_snapshot` verified that `SearchBar.Text` and `Opacity` returned to the original values. The failed batch mutation path returned rollback parameters and a clear recovery hint.

![HandyControl after restore-oriented mutation checks](assets/handycontrol-2026-06-30/post-mutation-window.png)

Screenshots were useful but not the main investigation mechanism. The MCP tools selected targets first; screenshots were mainly proof artifacts. The focused `element_screenshot(outputMode="file")` path returned a screenshot resource that could be read back as PNG data.

![MCP element screenshot resource](assets/handycontrol-2026-06-30/mcp-element-screenshot.png)

Response quality was high overall. Structured errors for missing policy, invalid focus target, invalid command, missing ViewModel property, missing DependencyProperty, and no binding were machine-readable and included recovery guidance. `get_binding_errors(navigation=false)` correctly omitted `navigation` and `nextSteps`.

The docs were clear enough for a normal agent. README stayed short, Quickstart matched the observed install and connect flow, the Codex page pointed to generated registration artifacts, and the AI Agent Guide correctly emphasized discovery, scene-first inspection, policy gates, and snapshot/diff/restore. The Traditional Chinese pages were aligned with the English flow.

Remaining minor friction: third-party clones under this repo's `tmp/` can inherit parent MSBuild props, so I isolated HandyControl restore with `-p:ManagePackageVersionsCentrally=false`; data-template item labels could be more semantic in `get_ui_summary`; and screenshot evidence works best when agents choose a sufficiently large element or read the returned resource.

GPT-5.5
