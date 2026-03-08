# Tree 與 XAML 工具

## 最重要的工具

- `get_visual_tree`
- `get_logical_tree`
- `compare_trees`
- `serialize_to_xaml`
- `get_namescope`
- `get_template_tree`

## 什麼時候用哪一個

- 需要實際 render 後的結構時，請用 **visual tree**。
- 想理解內容關係時，請用 **logical tree**。
- 當 control template 會產生額外 visual children 時，請用 **template tree**。
- 需要穩定 named parts 時，請用 **namescope**。
- 想取得精簡的類 XAML 子樹表示時，請用 **`serialize_to_xaml`**。

## 常見陷阱

不要假設 XAML 中有名稱的控制項，一定會在你想像的位置出現在 visual tree 裡。經過 template 套用後，請一律以 live tree 為準。
