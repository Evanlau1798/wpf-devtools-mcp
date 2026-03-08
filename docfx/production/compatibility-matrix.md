# Compatibility Matrix

## Runtime compatibility

| Target application | Inspector runtime path | Notes |
| --- | --- | --- |
| .NET Framework WPF | `net48` inspector path | Requires matching bootstrapper architecture |
| .NET 6/7/8+ WPF | `net8.0-windows` inspector path | Requires matching bootstrapper architecture |

## Architecture compatibility

| Target process architecture | Recommended server/bootstrapper build | Notes |
| --- | --- | --- |
| x86 | x86 / Win32 | Required for cross-bitness safety |
| x64 | x64 | Recommended default for most modern WPF apps |
| ARM64 | ARM64 | Use only on native ARM64 targets |

## Known unsupported or constrained scenarios

| Scenario | Status | Why |
| --- | --- | --- |
| Self-contained single-file WPF apps | Not supported | Native injection path cannot rely on the expected assembly layout |
| Native AOT | Not supported | The managed runtime hosting assumptions do not apply |
| Trimmed apps | Partial / risky | Required types may be removed |
| Non-WPF desktop UI stacks | Not supported | This server is WPF-specific |

## Practical guidance

- Use `get_processes` as the runtime architecture truth source.
- Treat x86 and x64 as separate deployment targets.
- Validate bootstrapper and inspector selection before calling `connect` in automation.