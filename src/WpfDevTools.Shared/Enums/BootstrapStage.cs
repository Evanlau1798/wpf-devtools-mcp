namespace WpfDevTools.Shared.Enums;

/// <summary>
/// Identifies the stage at which bootstrap injection failed.
/// Used by BootstrapResultInterpreter to map exit codes to diagnostic info.
/// </summary>
public enum BootstrapStage
{
    /// <summary>Stage unknown or not applicable</summary>
    Unknown = 0,
    /// <summary>LoadLibraryW stage — native DLL loading</summary>
    LoadLibrary = 1,
    /// <summary>CLR detection — checking for clr.dll or coreclr.dll</summary>
    ClrDetection = 2,
    /// <summary>CLR hosting initialization — CLRCreateInstance or hostfxr</summary>
    ClrHosting = 3,
    /// <summary>Managed entrypoint — ExecuteInDefaultAppDomain or load_assembly</summary>
    ManagedEntrypoint = 4,
    /// <summary>Inspector initialization — Bootstrap.Initialize called</summary>
    InspectorInit = 5,
    /// <summary>Named Pipe readiness — WaitNamedPipe check</summary>
    PipeReady = 6,
    /// <summary>Authentication secret loading — bootstrapper reads the temporary secret file</summary>
    AuthSecretLoad = 7
}
