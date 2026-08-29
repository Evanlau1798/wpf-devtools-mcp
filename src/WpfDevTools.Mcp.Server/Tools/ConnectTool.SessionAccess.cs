using System.Diagnostics;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class ConnectTool
{
    private object CreateRawInjectionDeniedFailure(int processId, WpfProcessInfo processInfo)
    {
        var authorization = _rawInjectionPolicy(processInfo);
        Trace.WriteLine($"ConnectTool raw injection denied process {processId}: executable={SensitiveLogRedactor.Redact(processInfo.ExecutablePath)}");
        return new
        {
            success = false,
            error = authorization.Error,
            errorCode = authorization.ErrorCode,
            hint = authorization.Hint,
            requiresExplicitTargetOptIn = true,
            allowlistEnvVar = McpServerConfiguration.RawInjectionAllowedTargetsEnvVar
        };
    }
}
