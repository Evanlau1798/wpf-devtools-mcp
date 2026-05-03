using System.Text;

namespace WpfDevTools.Injector.Injection;

/// <summary>
/// Structured injection request containing all parameters needed
/// for bootstrap injection. Replaces loose string parameters.
/// </summary>
public sealed class InjectionRequest
{
    /// <summary>Target process ID</summary>
    public required int ProcessId { get; init; }

    /// <summary>Path to native bootstrapper DLL (architecture-specific)</summary>
    public required string BootstrapperDllPath { get; init; }

    /// <summary>Path to managed Inspector DLL (TFM-specific)</summary>
    public required string InspectorDllPath { get; init; }

    /// <summary>Expected Named Pipe name for readiness check</summary>
    public required string ExpectedPipeName { get; init; }

    /// <summary>
    /// Optional base64-encoded authentication secret to hand off to the injected inspector.
    /// </summary>
    public string? AuthenticationSecretBase64 { get; init; }

    /// <summary>
    /// Optional certificate directory for TLS hand-off to the injected inspector.
    /// </summary>
    public string? CertificateDirectory { get; init; }

    /// <summary>Timeout for the injection operation (LoadLibrary + bootstrap)</summary>
    public TimeSpan InjectionTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Timeout for pipe readiness polling after bootstrap</summary>
    public TimeSpan PipeReadyTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Optional total timeout budget shared across all phases of bootstrap injection.
    /// When set, each phase must clamp its own timeout to the remaining shared budget.
    /// </summary>
    public TimeSpan? TotalTimeout { get; init; }

    /// <summary>
    /// Create the standard pipe name for a given process ID.
    /// </summary>
    public static string CreatePipeName(int processId) => $"WpfDevTools_{processId}_{Guid.NewGuid():N}";

    internal TimeSpan ResolvePhaseTimeout(TimeSpan elapsed, TimeSpan configuredTimeout)
    {
        if (configuredTimeout <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (!TotalTimeout.HasValue)
        {
            return configuredTimeout;
        }

        var remaining = TotalTimeout.Value - elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return configuredTimeout <= remaining ? configuredTimeout : remaining;
    }

    /// <summary>
    /// Create a copy of this request with a shared total timeout budget for all bootstrap phases.
    /// </summary>
    public InjectionRequest WithTotalTimeout(TimeSpan totalTimeout)
    {
        return new InjectionRequest
        {
            ProcessId = ProcessId,
            BootstrapperDllPath = BootstrapperDllPath,
            InspectorDllPath = InspectorDllPath,
            ExpectedPipeName = ExpectedPipeName,
            AuthenticationSecretBase64 = AuthenticationSecretBase64,
            CertificateDirectory = CertificateDirectory,
            InjectionTimeout = InjectionTimeout,
            PipeReadyTimeout = PipeReadyTimeout,
            TotalTimeout = totalTimeout
        };
    }

    /// <summary>
    /// Builds the bootstrap parameter string passed through the native bootstrapper.
    /// </summary>
    public string ToBootstrapParameters()
    {
        if (!string.IsNullOrWhiteSpace(AuthenticationSecretBase64))
        {
            throw new InvalidOperationException(
                "Authentication secrets must be handed off with CreateBootstrapParameterPayload so raw secrets are not written into bootstrap arguments.");
        }

        return BuildBootstrapParameters(authenticationSecretFilePath: null);
    }

    internal BootstrapParameterPayload CreateBootstrapParameterPayload()
    {
        return BootstrapParameterPayload.Create(this);
    }

    internal string BuildBootstrapParameters(string? authenticationSecretFilePath)
    {
        ValidateReservedDelimiters(nameof(InspectorDllPath), InspectorDllPath);
        ValidateReservedDelimiters(nameof(ExpectedPipeName), ExpectedPipeName);
        ValidateReservedDelimiters(nameof(authenticationSecretFilePath), authenticationSecretFilePath);
        ValidateReservedDelimiters(nameof(CertificateDirectory), CertificateDirectory);

        var builder = new StringBuilder();

        Append(builder, "inspectorDllPath", InspectorDllPath);
        Append(builder, "pipeName", ExpectedPipeName);

        if (!string.IsNullOrWhiteSpace(authenticationSecretFilePath))
        {
            Append(builder, "auth", "enabled");
            Append(builder, "authSecretFile", authenticationSecretFilePath);
        }

        if (!string.IsNullOrWhiteSpace(CertificateDirectory))
        {
            Append(builder, "encryption", "enabled");
            Append(builder, "certDirectory", CertificateDirectory);
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append(';');
        }

        builder.Append(key);
        builder.Append('=');
        builder.Append(value);
    }

    private static void ValidateReservedDelimiters(string fieldName, string? value)
    {
        if (!string.IsNullOrEmpty(value) && value!.Contains(";"))
        {
            throw new InvalidOperationException(
                $"Bootstrap parameter field '{fieldName}' cannot contain a semicolon because ';' is reserved as the parameter delimiter.");
        }
    }
}
