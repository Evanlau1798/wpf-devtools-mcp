# Real-case E2E evidence

`Test-E2ERunEvidence.ps1` validates hashed, run-contained artifacts. Run `PreJudge`
before the isolated visual judge and `Final` only after report and cleanup.
The durable PreJudge receipt binds the operational evidence to the visual
contract. CLI exit zero alone is not E2E PASS.

## Interactive evidence shape

The hashed runtime-inventory artifact must have `checkpoints`, an array of
`{ "name": "<journey checkpoint>", "controls": [...] }` objects. A flat top-level
`controls` list is not a checkpoint union. Its eligible-control union must agree
with `interactive.checkpoints` in the manifest. Each inventory item's `binding`
and `interaction` reference the actual supporting artifacts, not inline claims.
For value controls the binding kind is `property`, not `value`. Keep all required
runtime flags, stable identities, eligible controls, and justified exclusions.

`controlKind` is the portable evidence role used consistently in checkpoints,
bindings and before/after states (for example Button, TextBox or ListView), not
necessarily a third-party CLR class name. Choose it from the control's actual
bound properties and behavior; retain the real runtime type and supporting MCP
results separately. Normalizing a role must not invent a binding, hide a control,
or omit a required interaction. Unknown controls do not become exempt.

## State safety evidence

`stateSafety.diffArtifactId` and `restoreArtifactId` reference JSON envelopes
whose `result` is the **unaltered actual MCP tool result**, including full
`structuredContent`. Keep original STDIO transcript artifacts as well. Do not
replace full results with compact text summaries, add tool fields, or infer
success from an Agent-written boolean.

The public `get_state_diff` contract supplies `propertyChanges` and
`viewModelChanges` arrays, not a `changeCount` field. Each recorded mutation must
have different `beforeValue` and `afterValue`. Preserve the other diff fields.

The public `restore_state_snapshot` contract supplies detailed restored/skipped
DependencyProperty and ViewModel entries and their counts, `restoredFocus`, and
`warnings`. It does **not** supply `restoredSelection` or `restoredState`.
Every restored or skipped property must be verified, with matching expected and
current values; counts must agree with arrays. A skipped read-only or derived
property is acceptable only when the final value is verified at its baseline.
Every changed property must have a corresponding verified baseline result.
Focus must restore successfully, with no unresolved restore warnings.

Attach a `readback` object **outside** `result` in the restore envelope. Capture
baseline and post-restore values through MCP, preserve the supporting raw results,
and group the measured values as below. The property names inside each group are
app-defined; the validator has no UI-library or application-specific names.

```json
{
  "result": { "isError": false, "structuredContent": "<actual complete object>" },
  "readback": {
    "matchesBaseline": true,
    "baseline": {
      "selection": { "<measured selection property>": "<baseline value>" },
      "state": { "<measured state property>": "<baseline value>" },
      "focus": { "<measured focus property>": "<baseline value>" }
    },
    "actual": {
      "selection": { "<measured selection property>": "<post-restore value>" },
      "state": { "<measured state property>": "<post-restore value>" },
      "focus": { "<measured focus property>": "<post-restore value>" }
    }
  }
}
```

The `structuredContent` placeholder above represents the full JSON object, not a
string to submit. Selection, state, and focus groups must all be nonempty objects
and match by JSON value, including property sets and value types. The validator
rechecks equality; `matchesBaseline=true` alone is insufficient. A failed restore,
unverified skip, unrelated property, empty diff, or mismatched readback blocks
PreJudge. This does not change visual scoring, repair budget, runner, binding,
report, cleanup, containment, or SHA-256 gates.

## Raw call and readback provenance

Every state diff/restore envelope and supporting readback-call artifact has
`schemaVersion: "wpfdevtools.e2e-mcp-call-evidence.v1"`, its actual `tool` and
typed JSON-RPC `requestId`, plus `rawRpc.requestsArtifactId` and
`rawRpc.responsesArtifactId`. Those IDs reference hashed newline-delimited
JSON-RPC streams. Log the exact serialized request sent to stdin and the actual
stdout response; do not reconstruct or fabricate a transcript from the envelope.
The validator requires one matching request and response ID, JSON-RPC 2.0,
`tools/call`, the correct tool name, and JSON-value equality of the complete
`result` with the original response. Unknown tool-result fields are preserved.

Also attach `readback.provenance.baseline` and `.actual`, each mirroring the
selection/state/focus measured-property maps. Each property references a canonical
call artifact and an RFC 6901 JSON pointer **relative to its `result`**:

```json
{
  "callArtifactId": "<actual supporting MCP call artifact ID>",
  "valuePointer": "/structuredContent/properties/0/value"
}
```

Choose the pointer from the actual returned shape, not this example's property
index. The verifier validates that supporting call against its raw request and
response, then independently extracts and compares the measured value. Array
indices and escaped object keys (`~0`, `~1`) are supported. All linked call and
raw-transcript artifacts are included in the immutable PreJudge digest. A
self-consistent envelope or equal invented baseline/current values alone cannot
pass. Unit-test transcripts are synthetic protocol fixtures, not real-run proof.

Keep one ordered request/response stream pair for the state workflow. Diff and
restore must reference the same snapshot ID and occur in that order. Baseline
source requests and responses precede the diff; post-restore source requests and
responses follow the restore, with fresh call identities. Ordering is measured
from JSONL positions, never request-ID numeric values. Wait for restore completion
before issuing readback calls; do not reuse baseline evidence as a fresh read.

The same `rawRpc` object also references `transportArtifactId`, an append-only
I/O event stream with one shared positive, strictly increasing `sequence` for
both directions. Each line is `{ "sequence": 1, "direction": "sent",
"message": <original JSON-RPC object> }` or direction `received`. Preserve the
complete message, including unknown fields. The validator compares those messages
to the separate raw streams and requires the actual read's sent sequence to be
strictly after the restore response's received sequence. Every source uses the
same transport stream. Independent request/response indices alone do not prove
this causal boundary. Never synthesize the combined order from old separate logs.

Use the bounded harness helper `scripts/e2e/E2EMcpTransport.ps1` with one sequential
client, a fresh `StreamWriter` using `UTF8Encoding(false)`, and one counter starting
at zero. Call `Write-E2EMcpTransportEvent` at actual send/receive boundaries with
the parsed original message, writer and `[ref]` counter. Await each relevant
response before sending the next dependent request. This helper is allowed E2E
infrastructure, not product source; it serializes each full event with
`System.Text.Json`, writes one line and flushes. Create the log once, never append
a reconstructed replacement or edit it after collection.
