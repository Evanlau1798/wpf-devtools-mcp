using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Injector;

/// <summary>
/// Pure logic for selecting the correct Inspector DLL (TFM) and
/// Bootstrapper DLL (architecture) based on target process info.
/// </summary>
public static class RuntimeSelector
{
    /// <summary>
    /// Select the Inspector DLL matching the target runtime.
    /// Prefers matching TFM, falls back to any available candidate.
    /// </summary>
    /// <param name="runtime">Target CLR runtime type</param>
    /// <param name="candidates">Available Inspector DLL paths</param>
    /// <returns>Selected path, or null if no candidates</returns>
    public static string? SelectInspectorDll(
        TargetRuntime runtime,
        IReadOnlyList<string> candidates)
    {
        if (candidates.Count == 0) return null;

        if (runtime == TargetRuntime.Unknown)
        {
            return candidates.FirstOrDefault();
        }

        var preferred = runtime == TargetRuntime.NetFramework ? "net48" : "net8.0-windows";

        return candidates.FirstOrDefault(c => c.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>
    /// Select the Bootstrapper DLL matching the target architecture.
    /// </summary>
    /// <param name="arch">Target process architecture</param>
    /// <param name="candidates">Available Bootstrapper DLL paths</param>
    /// <returns>Selected path, or null if arch unknown or no match</returns>
    public static string? SelectBootstrapperDll(
        ProcessArchitecture arch,
        IReadOnlyList<string> candidates)
    {
        if (candidates.Count == 0) return null;

        var suffix = arch switch
        {
            ProcessArchitecture.X86 => "x86",
            ProcessArchitecture.X64 => "x64",
            ProcessArchitecture.ARM64 => "arm64",
            _ => (string?)null
        };

        if (suffix == null) return null;

        return candidates.FirstOrDefault(
            c => c.IndexOf(suffix, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
