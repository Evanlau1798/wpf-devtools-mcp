# Public-Path Runtime Security Checklist

在發布或推廣任何公開 installer 路徑、GitHub Release asset，或 hosted documentation 更新前，請使用這份 checklist。

## Release asset integrity

- 確認 release tag 指向已審查的 commit 後才開始 packaging。
- Stable release 只發布 `x64` 與 `x86` archives。
- 在具備可行的 Windows-on-ARM runtime smoke 路徑前，`arm64` archives 只能作為 prerelease-only preview assets。
- Stable release 使用 `Signed` trust mode，並在發布前設定 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`。
- 付費簽章尚不可用時，`ReleaseChecksumOnly` 只能用於 beta prerelease，且每個 archive 都必須透過 SHA256 release metadata 驗證。
- 確認 GitHub Release 已附上 `SHA256SUMS.txt`。
- 確認 `release-assets.json` 已附上，且列出每個 uploaded archive 的 size 與 SHA256 metadata。
- 確認 generated GitHub Release notes 包含 `SHA256SUMS.txt` 的內容。
- 保留 `release-sbom.spdx.json`、`package-sbom.spdx.json` 與 `release-evidence.json` 供 operators 檢查。

## Public installer path

- 確認 installer alias 解析到已審查的 `scripts/online-installer.ps1` entrypoint。
- Release validation 必須從 public URL 或 staged release metadata 安裝，不要從 source checkout 安裝。
- 確認 pinned pre-release installs 使用 `-Version <tag> -Prerelease`。
- 只有在 stable assets 已發布後，stable installs 才能省略 `-Prerelease`。
- 執行 `uninstall` 與 `full-uninstall` 檢查，並確認沒有 installer-owned payloads 殘留。

## Runtime policy gates

- 測試 `connect()` 前，先將 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 設為 exact local absolute executable paths。
- 只有在明確核准 raw injection fallback 時，才設定 `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS`。
- 除非 UI text、binding values、DP values 或 runtime state 可以離開 target process，否則保持 `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS` 未啟用。
- Screenshot、ViewModel 與 destructive gates 只在需要它們的 validation session 中啟用。
- 驗證 SDK-hosted reuse 時，保持 `WPFDEVTOOLS_AUTH_SECRET` 與 `WPFDEVTOOLS_CERT_DIR` 一致。

## Mutation and restore discipline

- 在 mutation 或 interaction 之前使用 `capture_state_snapshot`。
- 使用 focused read 或 `get_state_diff` 驗證 mutation 結果。
- 在 validation run 結束前使用 `restore_state_snapshot`。
- 當 event-buffer state 會影響 workflow 時，請 drain 或記錄殘留 events。

## Documentation and release notes

- User-facing install instructions 要保持簡短、任務導向。
- Contributor-only release 與 security procedures 放在 contributor section 或 root maintainer files。
- 不要把 E2E harness paths、temporary report names 或 local agent run notes 發布成 product guidance。
- 推廣 hosted site 前，重新 build DocFX 並執行 documentation validation。
