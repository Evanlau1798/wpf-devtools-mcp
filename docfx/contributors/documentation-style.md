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

## Public vs internal docs

- `docfx/` is the public documentation source.
- `docs/` remains the engineering research and planning area.
- ADRs should be summarized or rewritten for public readability instead of copied wholesale when they contain internal-only detail.