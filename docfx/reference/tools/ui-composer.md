# UI Composer Tools

UI Composer tools work with local Composer extension packs and blueprint inputs. They do not inspect a running WPF target and should be used before catalog, validation, rendering, or apply workflows.

## `list_ui_block_packs`

Lists installed UI block packs from built-in, project-local, and user-global roots. The response includes pack id, version, scope, block count, recipe count, example count, renderer count, source repository, readiness metadata, diagnostics, and available block kinds.

Request options:

- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The tool omits absolute pack root paths from its public payload. Use `structuredContent` as the canonical result and treat `content[0].text` as a compact fallback.
