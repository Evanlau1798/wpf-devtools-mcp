# Threat Model

本頁整理 WPF DevTools MCP 的生產環境威脅模型。內容刻意保持精簡，方便外部 reviewer 對照風險、控制措施與剩餘假設。

## Security boundary

WPF DevTools MCP 是本機除錯工具。支援的 trust boundary 是同一個 Windows 使用者執行已審查的 MCP server，並只連線到已審查的本機 WPF target。即使 MCP client 是可信任桌面程式，server 仍必須把 MCP client 視為 untrusted input，因為 prompts、tool descriptions 與 model context 可能被 prompt-injected。

真正的安全決策必須在 server-side policy gates。Client guidance、tool annotations、examples 與 prompts 只是不具強制力的 usability hints。

## Threats and Mitigations

| Threat | Risk | Mitigations |
| --- | --- | --- |
| MCP client as untrusted or prompt-injected caller | client 可能在使用者意圖之外要求 sensitive reads、screenshots、mutations 或 process discovery。 | target allowlists、sensitive-read gates、screenshot gates、destructive-tool policy gates、structured error contracts，以及 process metadata disclosure 前的 server-side validation。 |
| same-user local attacker | 以同一個 Windows 使用者執行的程式可能讀取本機檔案、environment variables、pipes 或 process state。 | local-only path validation、DPAPI-protected default secrets、persisted auth/cert files 的 protected ACLs、named-pipe authentication、TLS certificate pinning，以及 redacted default logs。 |
| malicious target process | target 可能暴露誤導性的 UI state、消耗 diagnostic payload，或啟動不相容的 SDK pipe。 | exact target allowlists、runtime compatibility checks、output caps、timeout handling、secure transport validation，以及 fail-closed SDK transport configuration。 |
| fake named-pipe / MITM server | 本機 process 可能冒充 Inspector pipe 以攔截 commands 或偽造 responses。 | process-derived pipe names、HMAC challenge-response、TLS certificate validation、host compatibility ping、PID validation，以及 adversarial fake-pipe regression tests。 |
| raw injection risk | DLL injection 可能失敗、打到錯誤 process，或跨過 architecture/security boundary。 | exact injection allowlists、`OpenProcess` 前的 architecture preflight、trusted local payload path validation、release signature/integrity checks，以及 target app 可 opt in 時優先採用 SDK-hosted mode。 |
| screenshot, ViewModel, and runtime data exfiltration | UI text、screenshots、binding values、ViewModel data 與 state snapshots 可能含有 secrets。 | `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS`、`WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS`、compact text fallback、省略 base64/log text fallback fields、local path redaction，以及 resource retention limits。 |
| supply-chain and release tampering | 被竄改的 package 或 installer 可能註冊惡意 MCP server。 | signed payload verification、package hash sidecars、canonical release metadata、SBOM sidecars、package-local integrity checks，以及 uninstall 後 residue checks。 |
| unsupported future HTTP/SSE multi-session transport | 若把 static process-wide caches 與 STDIO session assumptions 直接用於 multi-session transport，可能造成跨 client state leakage。 | 目前 production support 僅限 STDIO single-session。HTTP/SSE 在啟用前必須先把 session-specific state 移到 DI/request/session scope，並加上 isolation tests。 |

## Out of scope

- 防禦已用更高權限執行的 administrator 或 malware。
- 讓 raw injection 可安全套用在任意未審查 target。
- 支援 Native AOT 或 self-contained single-file injection。
- 把 screenshots、ViewModel values、binding values 或 state snapshots 視為非敏感資料。
- 在完成明確的 multi-session isolation work 前，把未來 HTTP/SSE transport 視為 production-ready。

## Review checklist

- 確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 與 `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` 只包含已審查的 exact local executable paths。
- 除非目前診斷 session 明確需要，否則保持 sensitive reads、screenshots 與 destructive tools 關閉。
- 可讓 app opt in 時優先採用 SDK-hosted Inspector；只有在 target 已審查且 architecture matched 時才使用 raw injection。
- 一般診斷優先使用 file 或 metadata screenshot output；只有 payload 明確很小時才使用 inline base64。
- 發佈或安裝 production package 前，驗證 release archives、checksums、signer metadata 與 SBOM sidecars。
