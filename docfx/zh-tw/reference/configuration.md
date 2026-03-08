# 設定參考

## 已交付版本的環境變數

| 變數 | 用途 | 備註 |
| --- | --- | --- |
| `WPFDEVTOOLS_AUTH_SECRET` | 啟用 HMAC challenge-response 驗證 | 必須是 base64 編碼 |
| `WPFDEVTOOLS_CERT_DIR` | 啟用 inspector pipe 的 TLS | 請使用受保護的本機目錄 |
| `WPFDEVTOOLS_CERT_THUMBPRINT` | pin 預期憑證的 thumbprint | 在高限制部署中很有幫助 |

除了已交付程式碼明確記錄的內容外，不應假設還有其他 `WPFDEVTOOLS_*` 變數存在。

## Build 模式

### Debug

- 最適合本機開發
- trusted local roots 可略過簽章驗證
- 是未簽署本機 build 最容易啟動的模式

### Release

- 用於生產環境部署
- 強制簽章驗證
- 建議搭配程式碼簽署與明確安全設定

## 架構專屬建置

managed build 請使用 `-p:Platform=`，native bootstrapper 請使用 `msbuild /p:Platform=`。
