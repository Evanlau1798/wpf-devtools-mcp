# Tree and XAML Tools

## Most important tools

- `get_visual_tree`
- `get_logical_tree`
- `compare_trees`
- `find_elements`
- `serialize_to_xaml`
- `get_namescope`
- `get_template_tree`
- `get_windows`

## When to use which

- Start with `get_ui_summary` or `get_element_snapshot` before tree expansion when you only need scene context or one-element triage.
- Use **visual tree** when you need actual rendered structure.
- Use **logical tree** when you want content relationships.
- Use **template tree** when control templates generate visual children.
- Use **namescope** when you need stable named parts.
- Use **find_elements** when you need a compact lookup by type, name, automation id, or exact property value before expanding the full tree.
- Use **`get_windows`** when a process has dialogs or secondary windows; pass the returned window `elementId` to tree, scene, or element-scoped tools.
- Use **`serialize_to_xaml`** when you want a compact XAML-like representation of a subtree.

## Bounded output defaults

`get_visual_tree` and `get_logical_tree` apply safe defaults when callers omit caps: `maxNodes` defaults to `1000`, and `maxChildrenPerNode` defaults to `200`. Raise those values only when you truly need a larger tree and can handle a larger MCP payload.

`get_template_tree` uses the same default node and fan-out caps. When a tree is capped, inspect `returnedNodeCount`, `omittedNodeCount`, `truncated`, and per-node `omittedChildCount` before deciding whether to request a narrower scope or higher caps.

## Common pitfall

Do not assume a named control in XAML is present where you expect in the visual tree after templating. Always inspect the live tree.
