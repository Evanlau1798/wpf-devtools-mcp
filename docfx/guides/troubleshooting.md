# Troubleshooting

## `connect` fails immediately

Check these first:

- The target process is a running WPF application.
- The MCP server architecture matches the target process architecture.
- The native bootstrapper for that architecture was built.
- The inspector DLL path is inside a trusted root.
- In production, the inspector DLL is Authenticode-signed.

## `connect` times out

A timeout does not always mean the same thing. The failure may be in one of these stages:

- bootstrapper did not load
- runtime detection failed
- managed bridge invocation failed
- the inspector was invoked but the named pipe never became ready

If the message points to pipe readiness, treat it as a startup/readiness issue, not necessarily a transport issue.

## Architecture mismatch errors

The fix is usually to run a server/bootstrapper build that matches the target process bitness.

- x64 target -> x64 server/bootstrapper
- x86 target -> x86 server/bootstrapper
- ARM64 target -> ARM64 bootstrapper and compatible environment

Do not assume that an AnyCPU inspector removes the injector bitness requirement.

## `get_event_handlers` returns zero handlers

A zero-handler result is a valid outcome and is different from "platform unsupported". The current implementation understands the .NET 8 `EventHandlersStore` member shape and returns an empty result when no handlers are attached.

## `element_screenshot` fails or returns empty bounds

Make sure the element is actually rendered and has non-zero visual bounds. Template parts and lazily realized content may need to be brought into view first.

## `drag_and_drop` did not update the target control

Prefer text-based drag/drop scenarios first. Validate the result after the drop using a follow-up inspection call such as `get_dp_value_source` for the `Text` property.

## Authentication or TLS failures

If you enable hardening:

- verify the same `WPFDEVTOOLS_AUTH_SECRET` is available to both ends
- verify the certificate directory is readable and persistent
- verify the expected thumbprint if you pin it explicitly

## Where to look next

- [Security Model](../production/security.md)
- [Bootstrap and Injection](../production/bootstrap-and-injection.md)
- [Configuration Reference](../reference/configuration.md)
- [Error Model](../reference/error-model.md)