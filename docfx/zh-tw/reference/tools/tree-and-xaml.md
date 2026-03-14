# Tree 與 XAML 工具

## 最重要的工具

- `get_visual_tree`
- `get_logical_tree`
- `compare_trees`
- `find_elements`
- `serialize_to_xaml`
- `get_namescope`
- `get_template_tree`

## 何時該用哪個

- 若你只需要 scene 上下文或單一元素 triage，先用 `get_ui_summary` 或 `get_element_snapshot`，再決定是否展開 tree。
- 需要看實際 render 出來的結構時，用 **visual tree**
- 需要看內容關係與 XAML 語意結構時，用 **logical tree**
- 需要快速用 type、name、automation id 或屬性值找元素時，用 **find_elements**
- 需要看 template 產生出的 visual children 時，用 **template tree**
- 需要找穩定的命名部件時，用 **namescope**
- 需要取得精簡的 XAML 近似表示時，用 **serialize_to_xaml**

## 常見陷阱

不要假設 XAML 中有名字的控制項，一定會出現在你直覺以為的位置。遇到 template、re-parenting 或 secondary window 時，請優先觀察 live tree，必要時先用 `find_elements` 再縮小範圍。
