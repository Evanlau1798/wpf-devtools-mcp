# 安全模型

本頁記錄的是目前已交付程式碼中實際存在的安全控制，而不是未來規劃。

## 威脅模型

這個 server 可以直接檢查並修改執行中的 WPF UI 狀態，因此下列風險必須納入考量：

- 未授權存取 inspector pipe
- 在 `connect` 過程中載入非預期的 inspector DLL
- 透過本機檔案洩漏金鑰或憑證
- 在 named-pipe 通道上遭受中間人攻擊或身分偽冒

## 已實作的控制措施

### DLL 驗證

`connect` 在載入 inspector DLL 前會先驗證它。

- **Debug build**：受信任的本機路徑可略過簽章驗證，方便本機開發。
- **Release build**：強制進行簽章驗證。
- **Path validation**：目前發佈版本只接受 trusted roots 內的路徑。

### Named-pipe 驗證

以 injection 為基礎的 `connect` session 預設就會使用 HMAC challenge-response 驗證。

- 這個 secret 必須是 base64 編碼。
- 若未設定 `WPFDEVTOOLS_AUTH_SECRET`，server 會先產生一組預設 secret，之後在同一個使用者 profile 下跨 server restart 重用。
- 若需要 deterministic 的 shared secret，可設定 `WPFDEVTOOLS_AUTH_SECRET` 來覆寫 server 產生的預設 secret。
- 若要讓 `connect()` 重用 SDK-hosted Inspector，`WPFDEVTOOLS_AUTH_SECRET` 與 `WPFDEVTOOLS_CERT_DIR` 必須在兩邊一起設定。預設 hardened 的 MCP server 不會重用 plaintext SDK host。
- 若 target app 會呼叫 `InspectorSdk.Initialize()`，只要 `WPFDEVTOOLS_AUTH_SECRET` 相同，`connect()` 就可以重用既有的 SDK-hosted Inspector。

### Named pipes 上的 TLS

以 injection 為基礎的 `connect` session 預設就會使用 inspector 連線的 TLS。

- server 會在該目錄建立或重用憑證。
- 若未設定 `WPFDEVTOOLS_CERT_DIR`，server 會使用 `%APPDATA%\WpfDevTools\certs` 下的預設憑證目錄。
- 若有明確設定 `WPFDEVTOOLS_CERT_DIR`，它必須是 absolute path。
- client 會驗證 subject，且可以 pin 預期的 thumbprint。
- `WPFDEVTOOLS_CERT_THUMBPRINT` 可用來覆寫預期 thumbprint。
- 若 target app 會呼叫 `InspectorSdk.Initialize()`，只要兩邊使用同一個 absolute `WPFDEVTOOLS_CERT_DIR` 值，`connect()` 就可以重用既有的 SDK-hosted Inspector。
- 在重用既有 host 前，client 也會驗證 named-pipe server 確實屬於指定 target process，且該 host 回報的 protocol/build fingerprint 與目前 MCP server 相容。

### Pipe 存取限制與 server 端保護

- Pipe ACL 只開放給目前使用者與 SYSTEM。
- 請求會序列化並受 framing 大小限制保護。
- server 會在 session 層級做 rate limiting。

## 建議的生產環境姿態

1. 使用 `Release` build。
2. 對 inspector DLL 做 Authenticode 簽署。
3. 保留預設的 injection-based transport hardening。
4. 若需要 deterministic secret rotation 或 SDK mode coordination，設定 `WPFDEVTOOLS_AUTH_SECRET`。
5. 若需要 deterministic 的憑證儲存位置或要與 SDK mode 共用，請在兩邊設定相同的 absolute `WPFDEVTOOLS_CERT_DIR`。
6. 視需要設定 `WPFDEVTOOLS_CERT_THUMBPRINT`。
7. 限制哪些人可以在工作站或 VM 上啟動 server。

## 重要限制

- TLS 預設使用本機自管憑證，而非企業 PKI。
- SDK-hosted Inspector 需要先設定相同的 transport configuration，`connect()` 才能重用既有 host；啟用 TLS 時也必須使用相同的 absolute `WPFDEVTOOLS_CERT_DIR`。
- 目前已交付的 transport 是 STDIO 加 named-pipe inspector 通訊；HTTP transport 不在目前二進位發佈範圍內。
