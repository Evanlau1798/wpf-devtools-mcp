using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Injector.Injection;

/// <summary>
/// Reserved negative exit codes used by the injector transport itself.
/// These codes are distinct from bootstrapper / managed bridge exit codes.
/// </summary>
internal static class InjectionMechanismFailure
{
    internal const int LoadBootstrapperFailed = -101;
    internal const int ResolveRemoteModuleFailed = -102;
    internal const int ResolveBootstrapExportFailed = -103;
    internal const int AllocateBootstrapParametersFailed = -104;
    internal const int WriteBootstrapParametersFailed = -105;
    internal const int StartBootstrapExportFailed = -106;
    internal const int InvokeBootstrapExportTimedOut = -107;

    internal sealed class InterpretationResult
    {
        internal required BootstrapStage Stage { get; init; }
        internal required string Message { get; init; }
    }

    internal static bool TryInterpret(int exitCode, out InterpretationResult? result)
    {
        result = exitCode switch
        {
            LoadBootstrapperFailed => new InterpretationResult
            {
                Stage = BootstrapStage.LoadLibrary,
                Message = "LoadLibraryW failed to load the native bootstrapper DLL into the target process."
            },
            ResolveRemoteModuleFailed => new InterpretationResult
            {
                Stage = BootstrapStage.LoadLibrary,
                Message = "The bootstrapper DLL loaded, but its remote module base could not be resolved."
            },
            ResolveBootstrapExportFailed => new InterpretationResult
            {
                Stage = BootstrapStage.ManagedEntrypoint,
                Message = "The native bootstrapper export 'BootstrapInspector' could not be resolved."
            },
            AllocateBootstrapParametersFailed => new InterpretationResult
            {
                Stage = BootstrapStage.ManagedEntrypoint,
                Message = "Failed to allocate remote memory for bootstrap parameters."
            },
            WriteBootstrapParametersFailed => new InterpretationResult
            {
                Stage = BootstrapStage.ManagedEntrypoint,
                Message = "Failed to write bootstrap parameters into the target process."
            },
            StartBootstrapExportFailed => new InterpretationResult
            {
                Stage = BootstrapStage.ManagedEntrypoint,
                Message = "Failed to start the bootstrap export thread in the target process."
            },
            InvokeBootstrapExportTimedOut => new InterpretationResult
            {
                Stage = BootstrapStage.ManagedEntrypoint,
                Message = "The bootstrap export thread did not complete before the timeout expired."
            },
            _ => null
        };

        return result != null;
    }
}

