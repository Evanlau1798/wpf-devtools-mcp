using System.Runtime.InteropServices;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class ConnectTool
{
    private static object? CreateRawInjectionArchitectureFailure(
        int processId,
        ConnectTargetContext context)
    {
        var targetArchitecture = context.ProcessInfo.Architecture;
        var serverArchitecture = GetCurrentServerArchitecture();
        if (targetArchitecture == ProcessArchitecture.Unknown
            || serverArchitecture == ProcessArchitecture.Unknown
            || targetArchitecture == serverArchitecture)
        {
            return null;
        }

        var packageArchitecture = ToPackageArchitecture(targetArchitecture);
        return new
        {
            success = false,
            error = ProcessInjector.GetArchitectureErrorMessage(
                targetArchitecture,
                ProcessArchitecture.Unknown,
                serverArchitecture,
                "Bootstrapper DLL"),
            errorCode = InjectionError.ArchitectureMismatch.ToString(),
            targetArchitecture = targetArchitecture.ToString(),
            serverArchitecture = serverArchitecture.ToString(),
            requiredPackageArchitecture = packageArchitecture,
            hint = $"Install and run the matching package architecture ({packageArchitecture}) for this target, " +
                   "or start a compatible target-side Inspector SDK host and retry connect()."
        };
    }

    private static ProcessArchitecture GetCurrentServerArchitecture()
        => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => ProcessArchitecture.X86,
            Architecture.X64 => ProcessArchitecture.X64,
            Architecture.Arm64 => ProcessArchitecture.ARM64,
            _ => ProcessArchitecture.Unknown
        };

    private static string ToPackageArchitecture(ProcessArchitecture architecture)
        => architecture switch
        {
            ProcessArchitecture.X86 => "win-x86",
            ProcessArchitecture.X64 => "win-x64",
            ProcessArchitecture.ARM64 => "win-arm64",
            _ => "matching package architecture"
        };
}
