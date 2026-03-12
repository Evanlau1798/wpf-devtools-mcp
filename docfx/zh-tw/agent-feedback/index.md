# Agent 使用心得

此區塊用來存放經 review 後的 Agent 使用心得，聚焦真實工作流程中的體驗、摩擦點與改進建議。

## 用途

- 記錄單元測試不容易反映的真實使用摩擦
- 保存值得成為預設 workflow 或 prompt 的成功案例
- 收斂高訊號的後續優化方向，供 roadmap 規劃使用

## 何時新增一篇心得

當 Agent 已經：

- 完成一條端對端診斷或自動化流程
- 在同一流程中比較過多個工具
- 發現反覆出現的摩擦、缺少聚合、或 token 過重步驟
- 驗證某次優化是否真的改善或回歸

## 檔名格式

請使用：

`YYYY-MM-DD-agent-feedback-{topic}.md`

例如：

- `2026-03-12-agent-feedback-scene-tools.md`
- `2026-03-12-agent-feedback-binding-diagnostics.md`

## 撰寫原則

- 以事實與 workflow 為主，不寫空泛心得
- 優先提供具體的 before/after 範例
- 明確標示問題是影響 happy path、error recovery，還是 token 使用量
- 不要放入機密資訊、敏感本機路徑或不應公開散佈的截圖

## 建議結構

1. 背景
2. 測試流程
3. 哪些做得好
4. 觀察到的摩擦
5. 建議改進
6. 優先級判斷

## 建議骨架

每份本地使用心得建議至少包含：

1. 背景
2. 測試流程
3. 哪些做得好
4. 觀察到的摩擦
5. 建議改進
6. 優先級判斷

## 範本

可從 [template.md](template.md) 開始填寫。
