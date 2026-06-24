# Agent Feedback

Use this section to capture structured feedback from agents that have completed a reviewed workflow with the MCP server.

## Purpose

- Record real usage friction that is hard to discover from isolated unit tests
- Preserve successful workflows that should become defaults or prompts
- Collect high-signal improvement requests for future roadmap planning

## When to add a report

Add a report when an agent has:

- completed an end-to-end diagnostic or automation flow
- compared multiple tools in the same workflow
- identified repeated friction, missing aggregation, or token-heavy steps
- validated that a recent optimization improved or regressed real usage

## File naming

Use this format:

`YYYY-MM-DD-agent-feedback-{topic}.md`

Examples:

- `2026-03-12-agent-feedback-scene-tools.md`
- `2026-03-12-agent-feedback-binding-diagnostics.md`

## Authoring rules

- Keep the report factual and workflow-oriented
- Prefer concrete before/after examples over general opinion
- Call out whether the issue affects happy path, error recovery, or token usage
- Do not include secrets, local paths that expose private data, or screenshots that should not be redistributed

## Recommended structure

1. Context
2. Workflow tested
3. What worked well
4. Friction observed
5. Suggested improvements
6. Priority assessment

## Suggested report skeleton

Use this structure for each local feedback report:

1. Context
2. Workflow tested
3. What worked well
4. Friction observed
5. Suggested improvements
6. Priority assessment

## Template

Start from [template.md](template.md).

## Public Feedback

### 2026-06-25 Real-Project E2E

**Context.** GPT-5.5 ran release-style E2E validation against GitHub prerelease `v1.0.0-beta.2`. The run downloaded public release assets, verified SHA256 sidecar metadata, installed the package through the packaged installer, and connected to the MaterialDesignInXAML Toolkit demo as a real third-party WPF target.

**Workflow tested.** The agent executed STDIO JSON-RPC calls for `initialize`, `tools/list`, contract resources, `connect`, scene-first inspection, focused diagnostics, safe mutation, diff, restore, event, wait, screenshot metadata, and policy-gate checks. The installed server exposed 64 tools and completed the real-project workflow.

**What worked well.**

- The verified installer path produced a working installed executable from the prerelease asset.
- `tools/list` and contract resources were complete enough for agent-driven planning.
- `connect -> get_ui_summary -> find_elements/get_namescope -> get_element_snapshot` was the most efficient inspection path.
- Scene-first summaries and focused snapshots avoided screenshot dependence for understanding the UI.
- Snapshot, diff, and restore guidance made temporary runtime mutation practical.
- Policy failures, including disabled screenshots, returned structured errors with actionable recovery text.

**Friction observed.**

- Running directly from an extracted prerelease package failed `connect()` with `SecurityError: Security verification failed`; installing the same archive with trusted sidecar metadata produced a working executable.
- Manual package instructions were easy to read as "extract and run the server from the package", which is not the same as using the installed layout.
- `batch_mutate.captureSnapshot` was easy to guess incorrectly as a boolean. The boolean form failed with a structured `InvalidArgument`; the object form succeeded and produced snapshot, diff, and restore guidance.

**Suggested improvements.**

- Keep the installer path as the primary prerelease and stable release workflow.
- In manual package docs, state that the installed executable under `<InstallRoot>\<arch>\current\bin\` is the runtime path agents should register.
- Add a compact `batch_mutate` example that uses the object `captureSnapshot` shape.
- Keep `summaryOnly` and focused scene-first examples prominent for agents that need low-token discovery.

**Agent impression.** The toolset feels practical for an autonomous agent once installed through the verified installer path. The best workflow is discovery, connect, scene summary, focused lookup, then one reversible mutation at a time. The response contract gave enough structured data to avoid screenshots for understanding and to choose rollback steps confidently. The weakest experience was installation ambiguity around extracted prerelease packages and the easy-to-misread `batch_mutate.captureSnapshot` shape.

Signed: GPT-5.5

## Security

- 2026-06-24 Security Deep Scan was retained as a local maintainer report. Standalone report files under this folder are intentionally ignored unless they are promoted into this tracked index.
