using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Injector.Injection;

/// <summary>
/// Reserved negative exit codes used by the injector transport itself.
/// These codes are distinct from bootstrapper / managed bridge exit codes.
/// </summary>
internal static class InjectionMechanismFailure
{
    internal const int LoadBootstrapperFailed = -101;
    internal const int LoadBootstrapperTimedOut = -111;
    internal const int LoadBootstrapperBudgetExhausted = -112;
    internal const int ResolveRemoteModuleFailed = -102;
    internal const int ResolveBootstrapExportFailed = -103;
    internal const int AllocateBootstrapParametersFailed = -104;
    internal const int WriteBootstrapParametersFailed = -105;
    internal const int StartBootstrapExportFailed = -106;
    internal const int InvokeBootstrapExportTimedOut = -107;
    internal const int ReadBootstrapExitCodeFailed = -108;
    internal const int ScheduleBootstrapCleanupFailed = -109;
    internal const int InvokeBootstrapExportBudgetExhausted = -110;

    internal sealed class InterpretationResult
    {
        internal required InjectionError Error { get; init; }
        internal required BootstrapStage Stage { get; init; }
        internal required string Message { get; init; }
        internal InjectionTimeoutReason? TimeoutReason { get; init; }
    }

    internal static bool TryInterpret(int exitCode, out InterpretationResult? result)
    {
        result = exitCode switch
        {
            LoadBootstrapperFailed => new InterpretationResult
            {
                Error = InjectionError.BootstrapFailed,
                Stage = BootstrapStage.LoadLibrary,
                Message = "LoadLibraryW failed to load the native bootstrapper DLL into the target process."
            },
            LoadBootstrapperTimedOut => new InterpretationResult
            {
                Error = InjectionError.Timeout,
                Stage = BootstrapStage.LoadLibrary,
                Message = "LoadLibraryW did not complete before the timeout expired."
            },
            LoadBootstrapperBudgetExhausted => new InterpretationResult
            {
                Error = InjectionError.Timeout,
                Stage = BootstrapStage.LoadLibrary,
                Message = "The remaining timeout budget was exhausted before the bootstrap DLL could start loading.",
                TimeoutReason = InjectionTimeoutReason.SharedBudgetExhaustedBeforePhaseStart
            },
            ResolveRemoteModuleFailed => new InterpretationResult
            {
                Error = InjectionError.BootstrapFailed,
                Stage = BootstrapStage.LoadLibrary,
                Message = "The bootstrapper DLL loaded, but its remote module base could not be resolved."
            },
            ResolveBootstrapExportFailed => new InterpretationResult
            {
                Error = InjectionError.BootstrapFailed,
                Stage = BootstrapStage.ManagedEntrypoint,
                Message = "The native bootstrapper export 'BootstrapInspector' could not be resolved."
            },
            AllocateBootstrapParametersFailed => new InterpretationResult
            {
                Error = InjectionError.BootstrapFailed,
                Stage = BootstrapStage.ManagedEntrypoint,
                Message = "Failed to allocate remote memory for bootstrap parameters."
            },
            WriteBootstrapParametersFailed => new InterpretationResult
            {
                Error = InjectionError.BootstrapFailed,
                Stage = BootstrapStage.ManagedEntrypoint,
                Message = "Failed to write bootstrap parameters into the target process."
            },
            StartBootstrapExportFailed => new InterpretationResult
            {
                Error = InjectionError.BootstrapFailed,
                Stage = BootstrapStage.ManagedEntrypoint,
                Message = "Failed to start the bootstrap export thread in the target process."
            },
            InvokeBootstrapExportTimedOut => new InterpretationResult
            {
                Error = InjectionError.Timeout,
                Stage = BootstrapStage.ManagedEntrypoint,
                Message = "The bootstrap export thread did not complete before the timeout expired."
            },
            ReadBootstrapExitCodeFailed => new InterpretationResult
            {
                Error = InjectionError.BootstrapFailed,
                Stage = BootstrapStage.ManagedEntrypoint,
                Message = "The bootstrap export thread completed, but its exit code could not be read."
            },
            ScheduleBootstrapCleanupFailed => new InterpretationResult
            {
                Error = InjectionError.BootstrapFailed,
                Stage = BootstrapStage.ManagedEntrypoint,
                Message = "The bootstrap export thread did not complete, and deferred remote memory cleanup could not be scheduled."
            },
            InvokeBootstrapExportBudgetExhausted => new InterpretationResult
            {
                Error = InjectionError.Timeout,
                Stage = BootstrapStage.ManagedEntrypoint,
                Message = "The remaining timeout budget was exhausted before the bootstrap export thread could start.",
                TimeoutReason = InjectionTimeoutReason.SharedBudgetExhaustedBeforePhaseStart
            },
            _ => null
        };

        return result != null;
    }
}

