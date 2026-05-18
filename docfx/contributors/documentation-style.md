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
- When `StructuredContent` is present, `Content.Text` remains a compact fallback summary for clients that only read text. `WPFDEVTOOLS_TEXT_FALLBACK_MODE=full` is an explicit compatibility mode for legacy text-only MCP clients, not the default documentation baseline.
- Additive follow-up guidance lives in the shared `navigation` envelope, while `nextSteps` remains the compatibility surface for older clients.
- Error results may include `Annotations` plus structured recovery fields such as `suggestedAction`, `requiresReconnect`, and `retryAfterSeconds`.
- Update public docfx pages from the code-backed contract; do not hand-maintain a second schema narrative that drifts from the wrappers.

## Public vs internal docs

- `docfx/` is the public documentation source.
- `docs/` remains the engineering research and planning area.
- ADRs should be summarized or rewritten for public readability instead of copied wholesale when they contain internal-only detail.
