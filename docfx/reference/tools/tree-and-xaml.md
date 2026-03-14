# Tree and XAML Tools

## Most important tools

- `get_visual_tree`
- `get_logical_tree`
- `compare_trees`
- `find_elements`
- `serialize_to_xaml`
- `get_namescope`
- `get_template_tree`

## When to use which

- Start with `get_ui_summary` or `get_element_snapshot` before tree expansion when you only need scene context or one-element triage.
- Use **visual tree** when you need actual rendered structure.
- Use **logical tree** when you want content relationships.
- Use **template tree** when control templates generate visual children.
- Use **namescope** when you need stable named parts.
- Use **find_elements** when you need a compact lookup by type, name, automation id, or exact property value before expanding the full tree.
- Use **`serialize_to_xaml`** when you want a compact XAML-like representation of a subtree.

## Common pitfall

Do not assume a named control in XAML is present where you expect in the visual tree after templating. Always inspect the live tree.
