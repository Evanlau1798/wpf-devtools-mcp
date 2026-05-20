# SDK-Hosted Inspector 快速開始

當你擁有 target application source code 時，prefer SDK-hosted reuse with `WpfDevTools.Inspector.Sdk`。當你需要對無法修改的 app 做 zero-instrumentation diagnostics 時，raw injection remains the fallback path。

## Package status

The NuGet package is not yet publicly published。公開前，請從 repository 建立 local pack：

```powershell
dotnet pack src\WpfDevTools.Inspector.Sdk\WpfDevTools.Inspector.Sdk.csproj -c Release -o .\nupkg -p:GeneratePackageOnBuild=false
dotnet add <your-wpf-app.csproj> package WpfDevTools.Inspector.Sdk --source .\nupkg
```

目前 target framework 是 `net8.0-windows`。在 SDK target expansion 完成前，.NET Framework WPF app 應維持使用 raw injection path。

## 必要 transport settings

在呼叫 `InspectorSdk.Initialize()` 前，MCP server process 與 target WPF application 都必須設定：

- `WPFDEVTOOLS_AUTH_SECRET`
- `WPFDEVTOOLS_CERT_DIR`

`WPFDEVTOOLS_CERT_DIR` 必須是 absolute path，且兩邊必須相同。SDK plaintext mode 預設不支援。

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
string authSecretBase64 = "...base64-encoded-32-byte-secret...";
string certificateDirectory = @"C:\absolute\wpf-devtools-certs";

InspectorSdk.InitializeWithOptions(new InspectorSdkOptions
{
    ProcessId = Environment.ProcessId,
    AuthenticationSecretBase64 = authSecretBase64,
    CertificateDirectory = certificateDirectory
});
```

Partial explicit SDK transport configuration is rejected and not mixed with environment variables。`AuthenticationSecretBase64` 與 `CertificateDirectory` 必須一起提供，且 `CertificateDirectory` 必須是 absolute path。The MCP server must use the same secret and certificate directory；不要在 target app 內獨立產生另一組 secret。

App 執行後，從 MCP client 呼叫 `connect()`。Server 會先探測 compatible SDK-hosted Inspector，並在 security settings 相符時重用它。

## 何時 prefer SDK-hosted mode

- 你擁有 target app source code。
- 你需要 production diagnostics，而且不想擴大 raw injection policy。
- deployment policy 或 AV 工具阻擋 DLL injection。
- app 使用 single-file、Native AOT 或 trimmed publish mode，讓 raw injection 不可靠。

## 何時保留 raw injection fallback

- 你無法修改 target app。
- 你需要 one-off zero-instrumentation diagnostics。
- target 是短期內無法導入 SDK 的 legacy app。
- 你正在 debug field issue，需要既有 bootstrapper path。

## 失敗檢查

- 如果 `InspectorSdk.Initialize()` 失敗，檢查 `InspectorSdk.LastInitializationStatus`。
- 如果 `connect()` 沒有重用 SDK host，確認兩個 process 使用相同的 `WPFDEVTOOLS_AUTH_SECRET` 與 absolute `WPFDEVTOOLS_CERT_DIR`。
- 如果 packaging 是 single-file、Native AOT 或 trimmed，優先使用 SDK-hosted mode，並只把 raw injection 當成 fallback。
