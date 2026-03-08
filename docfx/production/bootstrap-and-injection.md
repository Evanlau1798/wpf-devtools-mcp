# Bootstrap and Injection

## Why a bootstrapper exists

The server does not load the inspector directly into the target process. It injects a native bootstrapper first, then the bootstrapper hosts the correct managed entrypoint for the target runtime.

This design tightens the success contract and makes runtime-specific startup more explicit.

## High-level connect flow

1. The MCP client calls `connect(processId)`.
2. The server validates the process and candidate DLL paths.
3. The injector validates architecture compatibility.
4. The native bootstrapper is loaded into the target process.
5. The bootstrapper selects the correct managed bridge for the target runtime.
6. The inspector is invoked.
7. The server waits for the target named pipe to become ready.
8. A session is created only after readiness is confirmed.

## Success contract

A successful injection is stricter than "remote thread returned".

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