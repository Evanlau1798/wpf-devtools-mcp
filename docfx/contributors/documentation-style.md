# Documentation Style

## Goals

Public documentation should be:

- accurate to the shipping codebase
- task-oriented for first-time users
- deep enough for production deployment
- explicit about limitations and security boundaries

## Writing rules

- Prefer workflow-first explanations over long inventories.
- Use the MCP tool metadata in code as the schema source of truth.
- Do not promise unsupported transports or deployment modes.
- Call out architecture and runtime constraints early.
- Keep quickstarts short, then link to deeper pages.

## MCP C# SDK contract conventions

For this repository, the official C# SDK attributes are only the first layer of the public contract. When maintainers add or update tools, keep these repo-specific conventions aligned with the shipping server behavior:

- Tool metadata still starts in the SDK attributes, but the runtime response contract is normalized through `ToolCallHelper`.
- MCP wrappers use `UseStructuredContent = true`, and the server patches `tools/list` `outputSchema` to describe the common `result.structuredContent` payload fields rather than the `CallToolResult` envelope.
- `ToolCallHelper` still populates `StructuredContent` with the canonical WPF-specific JSON payload. The machine-readable response contract resource documents detailed per-tool payload fields.
- When `StructuredContent` is present, `Content.Text` remains a compact fallback summary for clients that only read text. `WPFDEVTOOLS_TEXT_FALLBACK_MODE=full` is an explicit compatibility mode for legacy text-only MCP clients, but it still omits large or sensitive text fields such as base64 screenshots and log dumps.
- Additive follow-up guidance lives in the shared `navigation` envelope, while `nextSteps` remains the compatibility surface for older clients.
- Error results may include `Annotations` plus structured recovery fields such as `suggestedAction`, `requiresReconnect`, and `retryAfterSeconds`.
- Update public docfx pages from the code-backed contract; do not hand-maintain a second schema narrative that drifts from the wrappers.

## Public vs internal docs

- `docfx/` is the public documentation source.
- `docs/` remains the engineering research and planning area.
- ADRs should be summarized or rewritten for public readability instead of copied wholesale when they contain internal-only detail.

## Reader-path checklist

Before opening a documentation PR, check the reader path:

- Can a first-time user finish the happy path without reading release engineering details?
- Does the page tell agents which tool to call first, and when not to call it?
- Are security gates explained before the first workflow that needs them?
- Does the page link to a deeper reference instead of repeating long contract text?
- If a new term appears more than once, should it be added to [Glossary](../reference/glossary.md)?

## Traditional Chinese terminology policy

Use Traditional Chinese as the base language, then keep stable English technical terms where the code, environment variable, or MCP contract uses English.

Recommended pattern:

1. First use: write Chinese plus English in parentheses, for example `目標應用程式（target）` or `預設拒絕（fail closed）`.
2. Later uses: keep the shorter chosen term consistently.
3. Environment variables, tool names, resource URIs, and JSON fields stay exactly as implemented.
4. Avoid mixing several translations for the same concept in one page.

Preferred terms:

| English | Traditional Chinese style |
| --- | --- |
| Windows-only | 僅支援 Windows |
| Model Context Protocol server | Model Context Protocol（MCP）伺服器 |
| target | 目標應用程式（target）, then `target` or `目標` consistently |
| fail closed | 預設拒絕（fail closed） |
| policy gate | policy gate or 安全 gate; choose one per page |
| allowlist | allowlist; explain as exact path allowlist on first use |
| mutation | mutation; first explain as runtime state change |
| structuredContent | keep as `structuredContent` |
