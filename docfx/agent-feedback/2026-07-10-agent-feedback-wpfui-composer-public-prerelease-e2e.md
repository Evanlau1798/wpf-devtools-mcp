# Agent Feedback: WPF UI Composer Public Pre-release E2E

The public online installer path for `v1.0.0-beta.31` was direct: it resolved the GitHub pre-release asset, installed into the scratch root, and produced an artifact-only registration for `other`.

Composer was usable through the installed MCP server over STDIO. The agent discovered the built-in WPF UI pack, selected `wpfui.shellWithNavigation`, expanded and validated the recipe, previewed it, and then applied it to a scratch `dotnet new wpf` app after the dry-run confirmation guard.

![Composer-generated WPF UI app](assets/composer-2026-07-10/composer-generated-app-mcp.png)

The generated app built and launched after applying only Composer-advertised scratch-local setup: WPF-UI package reference, scratch-local central package version, WPF UI resource dictionaries, and `FluentWindow` code-behind inheritance. Runtime inspection then worked against the launched app with scene-first summaries, focused reads, bounded trees, screenshot capture, snapshot/restore, bounded wait, and a negative recovery call.
