# Bootstrap and Injection

## Scope: raw injection path only

This page describes the native bootstrapper and raw injection path only.

If the target app already starts the inspector through `InspectorSdk.Initialize()`, or packaging and publish mode block raw injection, start with [Compatibility Matrix](compatibility-matrix.md) and [Security Model](security.md) first.

`connect()` always tries to reuse a compatible SDK-hosted Inspector before it falls back to bootstrapper-based injection.

## Why a bootstrapper exists

The server does not load the inspector directly into the target process. It injects a native bootstrapper first, then the bootstrapper hosts the correct managed entrypoint for the target runtime.

This design tightens the success contract and makes runtime-specific startup more explicit.

## High-level raw-injection flow

1. The MCP client calls `connect()` for the common case, or `connect(processId)` when the target has already been selected explicitly.
2. The server first tries to reuse a compatible SDK-hosted Inspector when one is already running.
3. If SDK-host reuse is unavailable, the server validates the process and candidate DLL paths.
4. The injector validates architecture compatibility.
5. The native bootstrapper is loaded into the target process.
6. The bootstrapper selects the correct managed bridge for the target runtime.
7. The inspector is invoked.
8. The server waits for the target named pipe to become ready.
9. A session is created only after readiness is confirmed.

## Success contract

A successful injection is stricter than "remote thread returned".

The remaining steps on this page describe the injection fallback path after compatible SDK-host reuse is unavailable.

The current implementation distinguishes between:

- bootstrap execution succeeded
- pipe became ready
- session creation succeeded

This matters because a partial bootstrap that never exposes a ready pipe should not be treated as a connected session.

## Architecture rule

The critical rule is not just inspector compatibility. The injector and target process must also be compatible for the remote loading sequence.

When you see an architecture mismatch, the fix is usually:

- switch to a matching server/bootstrapper build
- not "rebuild the inspector as AnyCPU"

## Debug vs release DLL validation

- **Debug**: trusted local roots can skip signature verification.
- **Release**: signature verification is enforced.
- **Untrusted paths**: rejected by path validation.

## Diagnostics you may see

Typical injection-stage failures include:

- architecture mismatch
- bootstrap failure
- pipe readiness timeout

See [Error Model](../reference/error-model.md) for how these appear at the tool response layer.
