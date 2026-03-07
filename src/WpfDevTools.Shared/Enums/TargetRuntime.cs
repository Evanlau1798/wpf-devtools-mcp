namespace WpfDevTools.Shared.Enums;

/// <summary>
/// Target process CLR runtime type, detected via loaded modules.
/// Used by RuntimeSelector to choose correct Inspector TFM.
/// </summary>
public enum TargetRuntime
{
    /// <summary>Unknown CLR — neither clr.dll nor coreclr.dll detected</summary>
    Unknown = 0,
    /// <summary>.NET Framework (clr.dll loaded)</summary>
    NetFramework = 1,
    /// <summary>.NET Core / .NET 5+ (coreclr.dll loaded)</summary>
    NetCore = 2
}
