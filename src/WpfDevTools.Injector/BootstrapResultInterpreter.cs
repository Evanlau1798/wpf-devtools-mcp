using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Injector;

/// <summary>
/// Pure logic: maps bootstrapper exit codes to structured error information.
/// Exit codes are defined in the native bootstrapper's exit_codes.h.
/// </summary>
public static class BootstrapResultInterpreter
{
    /// <summary>
    /// Interpretation result containing error type, stage, and message.
    /// </summary>
    public sealed class InterpretationResult
    {
        /// <summary>
        /// Mapped injection error.
        /// </summary>
        public required InjectionError Error { get; init; }

        /// <summary>
        /// Bootstrap stage associated with the interpreted exit code.
        /// </summary>
        public BootstrapStage? Stage { get; init; }

        /// <summary>
        /// Human-readable diagnostic message.
        /// </summary>
        public string? Message { get; init; }
    }

    /// <summary>
    /// Interpret a bootstrapper exit code into structured error information.
    /// </summary>
    /// <param name="exitCode">Remote thread exit code from BootstrapInspector</param>
    /// <returns>Structured interpretation with error, stage, and message</returns>
    public static InterpretationResult Interpret(int exitCode)
    {
        if (exitCode == 0x00)
        {
            return new InterpretationResult
            {
                Error = InjectionError.None
            };
        }

        var (stage, message) = exitCode switch
        {
            0x10 => (BootstrapStage.ClrDetection,
                "No CLR found in target process (neither clr.dll nor coreclr.dll loaded)"),
            0x11 => (BootstrapStage.ClrHosting,
                "CLR hosting initialization failed (CLRCreateInstance or hostfxr)"),
            0x12 => (BootstrapStage.ManagedEntrypoint,
                "ExecuteInDefaultAppDomain failed to invoke managed bootstrap bridge"),
            0x13 => (BootstrapStage.ManagedEntrypoint,
                "hostfxr load_assembly_and_get_function_pointer failed"),
            0x14 => (BootstrapStage.LoadLibrary,
                "Inspector DLL path invalid or not found by bootstrapper"),
            _ => (BootstrapStage.Unknown,
                $"Unknown bootstrap exit code: 0x{exitCode:X}")
        };

        return new InterpretationResult
        {
            Error = InjectionError.BootstrapFailed,
            Stage = stage,
            Message = message
        };
    }
}
