# SDK-Hosted Inspector 快速開始

當你擁有 target application source code 時，請優先使用 `WpfDevTools.Inspector.Sdk` 的 SDK-hosted reuse。當你需要對無法修改的 app 做 zero-instrumentation diagnostics 時，raw injection 仍是 fallback path。

## Package status

在 SDK package 正式發布到 NuGet 前，請使用 release/build pipeline 產生的 repository-local package。不要把未來的 NuGet install command 放進 production onboarding。Local development 可先從 repository 建立 local pack：

```powershell
dotnet pack src\WpfDevTools.Inspector.Sdk\WpfDevTools.Inspector.Sdk.csproj -c Release -o .\nupkg -p:GeneratePackageOnBuild=false
dotnet add <your-wpf-app.csproj> package WpfDevTools.Inspector.Sdk --source .\nupkg
```

Local SDK package 會包含 repository-internal `WpfDevTools.Inspector` 與 `WpfDevTools.Shared` assemblies，因此 consumer 不需要 unpublished sibling packages。

如果 target app 使用 `PackageSourceMapping`，請在該 app 內加入 app-local `NuGet.config`，把 `WpfDevTools.Inspector.Sdk` 對應到本機 package source。如果該 app 使用 `Directory.Packages.props` 的 Central Package Management，請在該檔案加入或覆寫 SDK package version，而不是只在命令列傳入未追蹤的版本。這些 restore 設定應留在 target app repo，不要寫回 WPF DevTools MCP checkout。

目前 target framework 是 `net8.0-windows`。在 SDK target expansion 完成前，.NET Framework WPF app 應維持使用 raw injection path。

## 必要 transport settings

在呼叫 `InspectorSdk.Initialize()` 前，MCP server process 與 target WPF application 都必須設定：

- `WPFDEVTOOLS_AUTH_SECRET`
- `WPFDEVTOOLS_CERT_DIR`

`WPFDEVTOOLS_CERT_DIR` 必須是 local absolute directory，且兩邊必須相同。SDK plaintext mode 預設不支援。

兩個 shell 都要使用相同值。`WPFDEVTOOLS_AUTH_SECRET` 必須是 base64 encoded，且解碼後至少 32 bytes。除非 deployment policy 要求更長 material，否則使用 32 bytes 即可：

```powershell
$env:WPFDEVTOOLS_AUTH_SECRET = "base64-encoded-at-least-32-byte-secret"
$env:WPFDEVTOOLS_CERT_DIR = "C:\wpf-devtools-certs"
```

先從第一個 shell 啟動 MCP server，再從第二個 shell 啟動 WPF target app，並維持同一組兩個變數。Target process 取得這些環境變數後，才呼叫 `InspectorSdk.Initialize()`。

### Expected fail-closed cases

這些檢查會刻意 fail closed，而不是 fallback 到 plaintext SDK transport：

- target app missing `WPFDEVTOOLS_AUTH_SECRET` 時，`InspectorSdk.Initialize()` 會 fail closed，且不會啟動 plaintext SDK host。
- target app missing `WPFDEVTOOLS_CERT_DIR` 時，`InspectorSdk.Initialize()` 會 fail closed，且不會把 partial SDK transport settings 與 defaults 混用。
- target app mismatched `WPFDEVTOOLS_AUTH_SECRET` 時，會使用不同 HMAC material；`connect()` 必須拒絕 reuse，而不是接受該 host。
- target app mismatched `WPFDEVTOOLS_CERT_DIR` 時，會使用不同 TLS certificate store；`connect()` 必須因 certificate chain 與 thumbprint 不相符而拒絕 reuse。

## Application integration

```csharp
using System.Windows;
using WpfDevTools.Inspector.Sdk;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        InspectorSdk.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        InspectorSdk.Shutdown();
        base.OnExit(e);
    }
}
```

如果 target process 不想直接依賴 `WPFDEVTOOLS_*` environment variables，而是已經有自己的 diagnostics config source，可以傳入 `InspectorSdkOptions`：

```csharp
string authSecretBase64 = "...base64-encoded-at-least-32-byte-secret...";
string certificateDirectory = @"C:\absolute\wpf-devtools-certs";

InspectorSdk.InitializeWithOptions(new InspectorSdkOptions
{
    ProcessId = Environment.ProcessId,
    AuthenticationSecretBase64 = authSecretBase64,
    CertificateDirectory = certificateDirectory
});
```

Partial explicit SDK transport configuration 會被拒絕，且不會與 environment variables 混用。`AuthenticationSecretBase64` 與 `CertificateDirectory` 必須一起提供，且 `CertificateDirectory` 必須是 local absolute directory。MCP server 必須使用相同的 secret 與 certificate directory；不要在 target app 內獨立產生另一組 secret。

App 執行後，從 MCP client 呼叫 `connect()`。Server 會先探測 compatible SDK-hosted Inspector，並在 security settings 相符時重用它。

## 何時 prefer SDK-hosted mode

- 你擁有 target app source code。
- 你需要 production diagnostics，而且不想擴大 raw injection policy。
- deployment policy 或 AV 工具阻擋 DLL injection。
- app 使用 single-file publish mode，導致 raw injection 不可用。
- app 使用 trimmed publish mode，且你接受 SDK-host startup 是 preferred fallback rather than a guarantee。

目前不支援 Native AOT targets。SDK-hosted reuse 不是 Native AOT workaround。

## 何時保留 raw injection fallback

- 你無法修改 target app。
- 你需要 one-off zero-instrumentation diagnostics。
- target 是短期內無法導入 SDK 的 legacy app。
- 你正在 debug field issue，需要既有 bootstrapper path。

## 失敗檢查

- 如果 `InspectorSdk.Initialize()` 失敗，檢查 `InspectorSdk.LastInitializationStatus`。
- 如果 `connect()` 沒有重用 SDK host，確認兩個 process 使用相同的 `WPFDEVTOOLS_AUTH_SECRET` 與 `WPFDEVTOOLS_CERT_DIR` local absolute directory。
- 如果 packaging 是 single-file，優先使用 SDK-hosted mode，並只把 raw injection 當成 fallback。
- 如果 packaging 是 trimmed，優先使用 SDK-hosted mode，但需要驗證 startup，因為 trimming 可能移除必要 inspector types。
- 目前不支援 Native AOT targets；SDK-hosted reuse 不是 Native AOT workaround。
