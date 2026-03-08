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

設定 `WPFDEVTOOLS_AUTH_SECRET` 可啟用 HMAC challenge-response 驗證。

- 這個 secret 必須是 base64 編碼。
- 若環境變數不存在，驗證功能會關閉。

### Named pipes 上的 TLS

設定 `WPFDEVTOOLS_CERT_DIR` 可啟用 inspector 連線的 TLS。

- server 會在該目錄建立或重用憑證。
- client 會驗證 subject，且可以 pin 預期的 thumbprint。
- `WPFDEVTOOLS_CERT_THUMBPRINT` 可用來覆寫預期 thumbprint。

### Pipe 存取限制與 server 端保護

- Pipe ACL 只開放給目前使用者與 SYSTEM。
- 請求會序列化並受 framing 大小限制保護。
- server 會在 session 層級做 rate limiting。

## 建議的生產環境姿態

1. 使用 `Release` build。
2. 對 inspector DLL 做 Authenticode 簽署。
3. 設定 `WPFDEVTOOLS_AUTH_SECRET`。
4. 設定 `WPFDEVTOOLS_CERT_DIR`。
5. 視需要設定 `WPFDEVTOOLS_CERT_THUMBPRINT`。
6. 限制哪些人可以在工作站或 VM 上啟動 server。

## 重要限制

- 驗證與 TLS 都是 opt-in。
- TLS 預設使用本機自管憑證，而非企業 PKI。
- 目前已交付的 transport 是 STDIO 加 named-pipe inspector 通訊；HTTP transport 不在目前二進位發佈範圍內。
