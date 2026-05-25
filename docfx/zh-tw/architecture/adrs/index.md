# ADR 索引

以下 ADR 為對外公開版摘要，聚焦目前設計中仍然重要、且能幫助使用者與貢獻者理解系統行為的決策。

## 清單

1. [ADR-001: Named Pipes for IPC](adr-001-named-pipes.md)
2. [ADR-002: In-Process Injection](adr-002-in-process-injection.md)
3. [ADR-003: Length-Prefix Framing](adr-003-length-prefix-framing.md)
4. [ADR-005: Multi-Targeting Strategy](adr-005-multi-targeting.md)
5. [ADR-006: STDIO Session State](adr-006-stdio-session-state.md)

## 閱讀建議

- 想先理解整體流程：先看[架構總覽](../overview.md)
- 想搞懂通訊模型：看[IPC 與通訊協定](../ipc.md)
- 想理解注入與 runtime 選擇：看[Injection 與 Runtime 選擇](../injection.md)
- 想新增 Streamable HTTP、SSE 或其他多 client transport：先看 [ADR-006](adr-006-stdio-session-state.md)
