# Tree 與 XAML 工具

## 最重要的工具

- `get_visual_tree`
- `get_logical_tree`
- `compare_trees`
- `find_elements`
- `serialize_to_xaml`
- `get_namescope`
- `get_template_tree`
- `get_windows`

## 何時該用哪個

- 若你只需要 scene 上下文，先用 `get_ui_summary`；單一元素 triage 則在 `find_elements` 或其他回應提供具體 `elementId` 後，使用 `get_element_snapshot(elementId)`。
- 需要看實際 render 出來的結構時，用 **visual tree**
- 需要看內容關係與 XAML 語意結構時，用 **logical tree**
- 需要快速用 semantic `query`、type、name、automation id 或屬性值找元素時，用 **find_elements**
- 需要看 template 產生出的 visual children 時，用 **template tree**
- 需要找穩定的命名部件時，用 **namescope**
- 需要檢查 dialog 或 secondary window 時，用 **`get_windows`**；將回傳的 window `elementId` 傳給 tree、scene 或其他 element-scoped 工具。
- 只有在 scene、tree 或 search 工具已回傳目前 session 的 `elementId` 後，才使用 **serialize_to_xaml(elementId)** 取得該子樹的 XAML 近似表示。

## 預設輸出上限

`get_visual_tree` 與 `get_logical_tree` 在呼叫端省略 caps 時會套用安全預設：`maxNodes` 預設為 `1000`，`maxChildrenPerNode` 預設為 `200`。只有在確定需要更大的樹，且可以處理更大的 MCP payload 時，才提高這些值。

`get_template_tree` 使用同樣的預設 node 與 fan-out caps；需要更小的 template payload 時，也可以傳入 `maxNodes` 與 `maxChildrenPerNode`。若回傳被截斷，先檢查 `returnedNodeCount`、`omittedNodeCount`、`truncated` 以及節點上的 `omittedChildCount`，再決定要縮小範圍或提高 caps。

`get_template_tree` 應用在目前 visual tree 中已載入、且具備 template 的控制項。若單一候選回 `ElementNotLoaded` 或 `No template visual tree found`，通常代表該候選在 inactive、virtualized 或不是 template-backed；real-project validation 時，請先換另一個已載入的 templated control 重試，再判定 template-tree workflow 有限制。

`find_elements` 也會在評估 match 前套用 traversal cap：`maxTraversalNodes` 預設為 `1000`，最高接受 `10000`。可選的 `query` 是有界線的便利搜尋，會比對 element type、`FrameworkElement.Name`、AutomationId、Text、Content、Header 等常見語意欄位；需要穩定自動化路徑時，仍優先使用 `typeName`、`elementName` 或 `automationId` 等精確 filters。若搜尋回傳 `traversalTruncated=true`，先檢查 `traversalNodeCount`，並優先縮小 root 或 filters，再考慮提高 traversal cap。

`serialize_to_xaml` 會要求 `elementId`，並拒絕 `selector`、`maxDepth`、`maxNodes` 這類 selector-style 參數。請先用 `get_ui_summary`、`get_visual_tree`、`get_logical_tree` 或 `find_elements` 取得具體元素，避免意外序列化大型 root window。

## 常見陷阱

不要假設 XAML 中有名字的控制項，一定會出現在你直覺以為的位置。遇到 template、re-parenting 或 secondary window 時，請優先觀察 live tree，必要時先用 `find_elements` 再縮小範圍。
