# 文件撰寫風格

## 目標

公開文件應該具備以下特性：

- 與目前已交付的程式碼一致
- 對第一次接觸的使用者來說是任務導向的
- 對生產環境部署來說深度足夠
- 清楚說明限制與安全邊界

## 撰寫規則

- 優先使用 workflow-first 的說明，而不是冗長清單。
- 以程式碼中的 MCP tool metadata 作為 schema 真相來源。
- 不要承諾目前尚未支援的 transport 或部署模式。
- 盡早說明 architecture 與 runtime 限制。
- 讓 quickstart 維持精簡，再連到較深的頁面。

## MCP C# SDK 契約慣例

在這個 repository 中，官方 C# SDK attribute 只是公開契約的第一層。當 maintainer 新增或調整工具時，請同步維持以下 repo-specific 慣例與實際 server 行為一致：

- Tool metadata 仍然從 SDK attribute 開始，但真正的 runtime response contract 會經過 `ToolCallHelper` 正規化。
- MCP wrapper 使用 `UseStructuredContent = true`，讓 SDK 在 `tools/list` 發布 `CallToolResult` envelope 的 `outputSchema`。
- `ToolCallHelper` 仍會將 WPF-specific canonical JSON payload 放進 `StructuredContent`。Machine-readable response contract resource 會描述這些 payload 欄位。
- 當 `StructuredContent` 存在時，`Content.Text` 仍保留 compact fallback summary，供只讀文字的 client 使用。`WPFDEVTOOLS_TEXT_FALLBACK_MODE=full` 是 legacy text-only MCP client 的明確相容模式，不是預設文件基準。
- 加值的 follow-up guidance 放在共用的 `navigation` envelope；`nextSteps` 則保留給較舊 client 的 compatibility surface。
- 錯誤結果可能包含 `Annotations`，以及像 `suggestedAction`、`requiresReconnect`、`retryAfterSeconds` 這類 structured recovery 欄位。
- 公開 docfx 文件應從 code-backed contract 出發更新，不要手動維護另一套容易 drift 的 schema 敘事。

## 公開文件與內部文件的區分

- `docfx/` 是公開文件來源。
- `docs/` 保留給工程研究、規劃與 review 歷程。
- 若 ADR 含有內部細節，公開文件應做摘要或重寫，而不是整份原封不動搬過來。
