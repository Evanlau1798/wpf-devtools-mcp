using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;

namespace WpfDevTools.Injector;

/// <summary>
/// Builds an InjectionRequest from WpfProcessInfo and candidate DLL paths.
/// Pure logic — does not access the file system.
/// </summary>
public static class InjectionPlanFactory
{
    /// <summary>
    /// Create an InjectionRequest by selecting the correct Inspector TFM
    /// and Bootstrapper architecture from available candidates.
    /// </summary>
    /// <returns>InjectionRequest if matching DLLs found, null otherwise</returns>
    public static InjectionRequest? CreateRequest(
        WpfProcessInfo processInfo,
        IReadOnlyList<string> inspectorCandidates,
        IReadOnlyList<string> bootstrapperCandidates)
    {
        var inspectorDll = RuntimeSelector.SelectInspectorDll(
            processInfo.Runtime, inspectorCandidates);
        if (inspectorDll == null) return null;

        var bootstrapperDll = RuntimeSelector.SelectBootstrapperDll(
            processInfo.Architecture, bootstrapperCandidates);
        if (bootstrapperDll == null) return null;

        return new InjectionRequest
        {
            ProcessId = processInfo.ProcessId,
            BootstrapperDllPath = bootstrapperDll,
            InspectorDllPath = inspectorDll,
            ExpectedPipeName = InjectionRequest.CreatePipeName(processInfo.ProcessId)
        };
    }
}
