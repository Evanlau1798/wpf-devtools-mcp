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
