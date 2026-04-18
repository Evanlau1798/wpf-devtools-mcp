# Claude Desktop Setup

Claude Desktop uses a static JSON file, so the cleanest setup is to copy the generated JSON from the installed package output.

## 1. Install WPF DevTools

Preferred public path:

1. Review [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1) as the canonical source entrypoint.
2. Run the reviewed installer locally.

Example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Architecture x64 -Client claude-desktop -NonInteractive -Force -OutputJson
```

Package-local fallback:

1. Download the matching `release_<version>_win-<arch>.zip` from [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases).
2. Extract the package.
3. Run `run.bat`.

`run.bat` requests elevation when the current shell is not already elevated and then launches the packaged `bin/install.ps1`. Set `WPFDEVTOOLS_SKIP_ELEVATION=1` when you need to keep the install in the current unelevated shell.

After installation, the fallback executable path when no previous live install root is reused is:

```text
C:\Users\<you>\AppData\Roaming\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe
```

## 2. Generated JSON template

The installer writes `client-registration\claude-desktop.json`. Its structure is:

```json
{
  "mcpServers": {
    "wpf-devtools": {
      "type": "stdio",
      "command": "C:\\Users\\<you>\\AppData\\Roaming\\WpfDevToolsMcp\\<arch>\\current\\bin\\wpf-devtools-<arch>.exe",
      "args": []
    }
  }
}
```

Use the generated `client-registration\claude-desktop.json` artifact as the source of truth for the resolved executable path. Only adjust the copied path in your local `claude_desktop_config.json` when you intentionally switch architectures or install roots.

## 3. First prompt

```text
Use the WPF DevTools MCP server to connect to the running WPF app, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## Notes

- Start with `connect()` in the common case. Use `get_processes(windowFilter)` only when auto-discovery reports multiple candidates.
- Prefer scene-level verification before visual-tree expansion.
- After each diagnostic, interaction, or mutation, follow `navigation.recommended` first and treat `nextSteps` as the compatibility field.
- Keep mutation tools for later in the workflow.
- Reinstall or re-register after switching between `x64`, `x86`, and `arm64` targets.
