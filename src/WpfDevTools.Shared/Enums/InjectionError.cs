namespace WpfDevTools.Shared.Enums;

/// <summary>
/// DLL injection error types
/// </summary>
public enum InjectionError
{
    /// <summary>
    /// No error
    /// </summary>
    None = 0,

    /// <summary>
    /// Process not found
    /// </summary>
    ProcessNotFound = 1,

    /// <summary>
    /// Access denied to process
    /// </summary>
    AccessDenied = 2,

    /// <summary>
    /// Architecture mismatch (e.g., trying to inject x64 DLL into x86 process)
    /// </summary>
    ArchitectureMismatch = 3,

    /// <summary>
    /// Not a WPF application
    /// </summary>
    NotWpfApplication = 4,

    /// <summary>
    /// Single-file application (cannot inject)
    /// </summary>
    SingleFileApplication = 5,

    /// <summary>
    /// Failed to allocate memory in target process
    /// </summary>
    AllocationFailed = 6,

    /// <summary>
    /// Failed to write DLL path to target process
    /// </summary>
    WriteFailed = 7,

    /// <summary>
    /// Failed to create remote thread
    /// </summary>
    CreateThreadFailed = 8,

    /// <summary>
    /// Timeout waiting for injection to complete
    /// </summary>
    Timeout = 9,

    /// <summary>
    /// Unknown error
    /// </summary>
    Unknown = 99
}
