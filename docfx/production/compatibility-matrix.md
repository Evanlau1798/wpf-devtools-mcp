# Compatibility Matrix

## Runtime compatibility

| Target application | Inspector runtime path | Notes |
| --- | --- | --- |
| .NET Framework WPF | `net48` inspector path | Requires matching bootstrapper architecture |
| .NET 8+ WPF | `net8.0-windows` inspector path | Requires matching bootstrapper architecture |
| .NET 6/7 WPF | No shipped raw-injection inspector path | Not currently supported by raw injection; upgrade the target app to .NET 8+ or wait for explicit SDK/inspector target expansion |

## Architecture compatibility

| Target process architecture | Recommended server/bootstrapper build | Notes |
| --- | --- | --- |
| x86 | x86 / Win32 | Required for cross-bitness safety |
| x64 | x64 | Recommended default for most modern WPF apps |
| ARM64 | ARM64 | Use only on native ARM64 targets |

Architecture matching is mandatory for raw injection/bootstrapper fallback. SDK-hosted reuse communicates over named pipes and does not require matching process bitness once the target-side host is already running.

## Known unsupported or constrained scenarios

| Scenario | Raw injection path | Overall support posture | Notes |
| --- | --- | --- | --- |
| Self-contained single-file WPF apps | Not supported | Supported through SDK-host reuse | The native injection path cannot rely on the expected assembly layout. Call `InspectorSdk.Initialize()` in the target app with matching transport settings so `connect()` can reuse the existing host. |
| Native AOT | Not supported | Not supported | WPF DevTools does not support Native AOT targets today. SDK-hosted reuse is not a Native AOT workaround because the Inspector SDK requires managed WPF runtime and assembly access. |
| Trimmed apps | Risky / partial | Prefer SDK-host reuse | Required types may be removed, making raw injection or inspector startup unreliable. |
| Non-WPF desktop UI stacks | Not supported | Not supported | This server is WPF-specific. |

## Practical guidance

- Use `get_processes` as the runtime architecture truth source.
- Treat x86 and x64 as separate deployment targets.
- When you own the target app, prefer SDK-hosted reuse; raw injection remains the fallback path for zero-instrumentation diagnostics.
- .NET 6/7 WPF targets do not have a shipped raw-injection inspector path today. Do not assume `net8.0-windows` can be injected into those processes.
- If single-file packaging blocks raw injection, start the target-side SDK host with `InspectorSdk.Initialize()` and matching transport settings so `connect()` can reuse it.
- Native AOT remains unsupported; SDK-hosted reuse is not a Native AOT workaround.
- Validate bootstrapper and inspector selection before calling `connect` in automation.
