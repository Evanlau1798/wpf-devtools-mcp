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

- Start with `get_ui_summary` before tree expansion when you need scene context; use `get_element_snapshot(elementId)` for one-element triage after `find_elements` or another result supplies a concrete elementId.
- Use **visual tree** when you need actual rendered structure.
- Use **logical tree** when you want content relationships.
- Use **template tree** when control templates generate visual children.
- Use **namescope** when you need stable named parts.
- Use **find_elements** when you need a compact lookup by semantic `query`, type, name, automation id, or property value before expanding the full tree.
- Use **`get_windows`** when a process has dialogs or secondary windows; pass the returned window `elementId` to tree, scene, or element-scoped tools.
- Use **`serialize_to_xaml(elementId)`** only after another scene, tree, or search tool has returned a current `elementId` for the subtree you want to inspect. The result is a safe runtime XAML snapshot, not a design-time XAML export.

## Bounded output defaults

`get_visual_tree` and `get_logical_tree` apply safe defaults when callers omit caps: `maxNodes` defaults to `1000`, and `maxChildrenPerNode` defaults to `200`. Raise those values only when you truly need a larger tree and can handle a larger MCP payload.

`get_template_tree` uses the same default node and fan-out caps and accepts `maxNodes` plus `maxChildrenPerNode` when you need a smaller template payload. When a tree is capped, inspect `returnedNodeCount`, `omittedNodeCount`, `truncated`, and per-node `omittedChildCount` before deciding whether to request a narrower scope or higher caps.

Use `get_template_tree` on a loaded templated control from the current visual tree. A single `ElementNotLoaded` or `No template visual tree found` result often means the chosen candidate is inactive, virtualized, or not template-backed; retry another loaded templated control before reporting a template-tree limitation in real-project validation.

`find_elements` also applies a traversal cap before evaluating matches: `maxTraversalNodes` defaults to `1000` and is capped at `10000`. The optional `query` parameter is a bounded convenience search over common semantic fields such as element type, `FrameworkElement.Name`, AutomationId, Text, Content, and Header; use exact filters like `typeName`, `elementName`, or `automationId` when the automation path must be deterministic. When a search returns `traversalTruncated=true`, inspect `traversalNodeCount` and narrow the root or filters before raising the traversal cap.

`serialize_to_xaml` intentionally requires `elementId` and rejects selector-style arguments such as `selector`, `maxDepth`, and `maxNodes`. Use `get_ui_summary`, `get_visual_tree`, `get_logical_tree`, or `find_elements` first so the tool snapshots a specific live subtree instead of accidentally targeting a large root window.

The snapshot writer avoids WPF `XamlWriter` round-trip serialization so third-party controls and template-heavy subtrees can be inspected without invoking design-time serializers. Treat the output as a bounded runtime view of the live element tree.

## Common pitfall

Do not assume a named control in XAML is present where you expect in the visual tree after templating. Always inspect the live tree.
