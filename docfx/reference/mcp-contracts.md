# MCP Contracts And Navigation

Use this page when an MCP client or agent needs the machine-readable runtime contract rather than prose examples.

## Discovery Order

1. Start the server with the target policy gates needed for the task.
2. Call MCP discovery such as `initialize`, `tools/list`, `prompts/list`, and `resources/list`.
3. Read `wpf://contracts/tools` for tool names, categories, parameters, required fields, reflection-backed parameter `constraints`, capability tags, and policy tags.
4. Read `wpf://contracts/response` for response envelopes, compatibility aliases, navigation metadata, and error recovery fields.
5. Read `wpf://capabilities` when the client needs a compact capability summary.

Do not hard-code tool arguments from old screenshots or prior runs. Runtime discovery is the source of truth.

Each manifest parameter may include a compact `constraints` object. Fields such as `minLength`, `maxLength`, `minimum`, and `maximum` mirror the validation annotations that also contribute to `inputSchemaHash`; use them when a client cannot expose the raw `tools/list` input schema.

## Prompt And Resource Names

Some clients render MCP prompts and resources with client-specific shortcuts. Treat the standard names and URIs as the portable contract:

| Surface | Portable name |
| --- | --- |
| Binding triage prompt | `debug_binding_issue` |
| Capability resource | `wpf://capabilities` |
| Tool contract resource | `wpf://contracts/tools` |
| Response contract resource | `wpf://contracts/response` |

If a client displays a shortcut such as `/mcp__wpf-devtools__debug_binding_issue`, keep the underlying prompt name in notes and automation.

## Response Fields To Prefer

Structured clients should treat `result.structuredContent` as canonical. Use `result.content[0].text` as a compact fallback for clients that cannot render structured payloads.

When present, follow `navigation.recommended` before improvising the next call. Use `navigation.alternatives` for human-guided branches, `prefetchTools` for progressive schema loading, and `contextRefs` as descriptive context only. `contextRefs` are not executable handles.

`nextSteps` remains the compatibility field for older clients and is usually derived from `navigation.recommended`.

## Navigation Opt-Out

`get_binding_errors` advertises `navigation=false` in its runtime schema. Agents may use it when the next action is already obvious and token volume matters.

Do not pass `navigation=false` to other tools unless their `tools/list` schema advertises the parameter.

## Related Pages

- [Tool Reference Overview](tools/index.md)
- [AI Agent Guide](../guides/ai-agent-guide.md)
- [Error Model](error-model.md)
- [Troubleshooting](../guides/troubleshooting.md)
