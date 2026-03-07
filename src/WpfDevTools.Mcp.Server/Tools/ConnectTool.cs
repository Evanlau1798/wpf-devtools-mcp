using System.Text.Json;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to connect to a WPF process.
///
/// This class intentionally does NOT extend PipeConnectedToolBase because the connect
/// tool is responsible for establishing the Named Pipe connection in the first place.
/// PipeConnectedToolBase requires an existing pipe session to send inspector requests,
/// but ConnectTool must inject the Inspector DLL and create the session before any pipe
/// communication is possible.
/// </summary>
public sealed class ConnectTool
{
    private readonly IProcessInjector _injector;
    private readonly SessionManager _sessionManager;
    private readonly WpfProcessDetector _processDetector;

#if DEBUG
    private static readonly bool IsDebugBuild = true;
#else
    private static readonly bool IsDebugBuild = false;
#endif

    /// <summary>
    /// Create ConnectTool with dependency injection
    /// </summary>
    public ConnectTool(
        SessionManager sessionManager,
        IProcessInjector injector,
        WpfProcessDetector? processDetector = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        _processDetector = processDetector ?? new WpfProcessDetector();
    }

    /// <summary>
    /// Create ConnectTool (backward compatibility constructor)
    /// </summary>
    public ConnectTool(SessionManager sessionManager)
        : this(sessionManager, new ProcessInjector())
    {
    }

    /// <summary>
    /// Execute the tool to connect to a WPF process
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        int? processId = null;
        if (arguments.HasValue && arguments.Value.TryGetProperty("processId", out var pidProp))
        {
            if (!pidProp.TryGetInt32(out var parsedPid))
            {
                return new
                {
                    success = false,
                    error = "processId must be a valid 32-bit integer"
                };
            }
            processId = parsedPid;
        }

        if (!processId.HasValue)
        {
            return new
            {
                success = false,
                error = "Missing required parameter: processId"
            };
        }

        // SECURITY: Rate limit connect attempts to prevent DoS
        if (!_sessionManager.CheckRateLimit(processId.Value))
        {
            var availableTokens = _sessionManager.GetAvailableTokens(processId.Value);
            return new
            {
                success = false,
                error = "Rate limit exceeded for connect operations. Please slow down your requests.",
                availableTokens,
                retryAfterSeconds = 60,
                retryAfter = "Wait 1 minute for rate limit to reset"
            };
        }

        // Check if already connected
        if (_sessionManager.HasSession(processId.Value))
        {
            return new
            {
                success = true,
                message = "Already connected to process",
                processId = processId.Value
            };
        }

        // Validate target process
        var validationError = _injector.ValidateTarget(processId.Value);
        if (validationError != InjectionError.None)
        {
            return new
            {
                success = false,
                error = GetErrorMessage(validationError, processId.Value)
            };
        }

        // Build injection plan dynamically based on target process info
        var processInfo = _processDetector.GetProcessInfo(processId.Value);
        if (processInfo == null)
        {
            return new
            {
                success = false,
                error = $"Could not detect process info for {processId.Value}"
            };
        }

        var inspectorCandidates = EnumerateInspectorDllCandidates(
            AppContext.BaseDirectory).Where(File.Exists).ToArray();
        var bootstrapperCandidates = EnumerateBootstrapperCandidates(
            AppContext.BaseDirectory).Where(File.Exists).ToArray();

        var injectionRequest = InjectionPlanFactory.CreateRequest(
            processInfo, inspectorCandidates, bootstrapperCandidates);

        if (injectionRequest == null)
        {
            return new
            {
                success = false,
                error = "No matching Inspector or Bootstrapper DLL found for target process. " +
                    $"Runtime: {processInfo.Runtime}, Architecture: {processInfo.Architecture}"
            };
        }

        // SECURITY: Validate resolved DLL paths
        ValidateDllPath(injectionRequest.InspectorDllPath);
        ValidateDllPath(injectionRequest.BootstrapperDllPath);

        // Perform bootstrap injection
        var injectionResult = _injector.InjectWithBootstrap(injectionRequest);
        if (!injectionResult.Success)
        {
            return new
            {
                success = false,
                error = injectionResult.ErrorMessage ?? "Injection failed",
                stage = injectionResult.FailedAtStage?.ToString(),
                exitCode = injectionResult.BootstrapExitCode
            };
        }

        // Add to session manager (also creates NamedPipeClient)
        _sessionManager.AddSession(processId.Value);
        try
        {
            var pipeClient = _sessionManager.GetPipeClient(processId.Value);
            if (pipeClient == null)
            {
                _sessionManager.RemoveSession(processId.Value);
                return new
                {
                    success = false,
                    error = "Failed to create Named Pipe client"
                };
            }

            var connected = await pipeClient.ConnectAsync(
                TimeSpan.FromSeconds(McpServerConfiguration.ConnectTimeoutSeconds),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!connected)
            {
                _sessionManager.RemoveSession(processId.Value);
                return new
                {
                    success = false,
                    error = "Timeout connecting to Inspector Named Pipe"
                };
            }

            return new
            {
                success = true,
                message = "Connected successfully. You can now use inspection tools (get_visual_tree, get_bindings, get_binding_errors, etc.) with this processId.",
                processId = processId.Value
            };
        }
        catch
        {
            _sessionManager.RemoveSession(processId.Value);
            throw;
        }
    }

    private static IEnumerable<string> EnumerateInspectorDllCandidates(string serverDir)
    {
        yield return Path.GetFullPath(Path.Combine(serverDir, "WpfDevTools.Inspector.dll"));

        var solutionRoot = GetSolutionRoot(serverDir);
        if (solutionRoot == null)
        {
            yield break;
        }

        var inspectorBinRoot = Path.Combine(solutionRoot, "src", "WpfDevTools.Inspector", "bin");
        var configurations = new[] { "Debug", "Release" };
        var frameworks = new[] { "net8.0-windows", "net48" };

        foreach (var configuration in configurations)
        {
            foreach (var framework in frameworks)
            {
                yield return Path.GetFullPath(Path.Combine(
                    inspectorBinRoot,
                    configuration,
                    framework,
                    "WpfDevTools.Inspector.dll"));
            }
        }
    }

    private static IEnumerable<string> EnumerateBootstrapperCandidates(string serverDir)
    {
        yield return Path.GetFullPath(Path.Combine(serverDir, "WpfDevTools.Bootstrapper.x64.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "WpfDevTools.Bootstrapper.x86.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "WpfDevTools.Bootstrapper.arm64.dll"));

        var solutionRoot = GetSolutionRoot(serverDir);
        if (solutionRoot == null) yield break;

        var artifactsRoot = Path.Combine(solutionRoot, "artifacts", "bootstrapper");
        var configurations = new[] { "Debug", "Release" };
        var platforms = new[] { ("x64", "x64"), ("Win32", "x86"), ("ARM64", "arm64") };

        foreach (var configuration in configurations)
        {
            foreach (var (platform, suffix) in platforms)
            {
                yield return Path.GetFullPath(Path.Combine(
                    artifactsRoot, configuration, platform,
                    $"WpfDevTools.Bootstrapper.{suffix}.dll"));
            }
        }
    }

    private static string? GetSolutionRoot(string startDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string GetErrorMessage(InjectionError error, int processId)
    {
        return error switch
        {
            InjectionError.ProcessNotFound => $"Process {processId} not found or has exited",
            InjectionError.NotWpfApplication => $"Process {processId} is not a WPF application",
            InjectionError.AccessDenied => $"Access denied to process {processId}. Try running as administrator.",
            InjectionError.ArchitectureMismatch => $"Architecture mismatch for process {processId}. Ensure the MCP server architecture matches the target process (both x64 or both x86).",
            _ => $"Validation failed: {error}"
        };
    }

    /// <summary>
    /// Validate DLL path to prevent security issues.
    /// Internal for testability.
    /// </summary>
    internal static void ValidateDllPath(string dllPath)
    {
        if (string.IsNullOrWhiteSpace(dllPath))
            throw new ArgumentException("DLL path cannot be empty", nameof(dllPath));

        if (!dllPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("DLL path must have .dll extension", nameof(dllPath));

        if (dllPath.StartsWith(@"\\", StringComparison.Ordinal) ||
            dllPath.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("Network paths are not allowed", nameof(dllPath));

        var fullPath = Path.GetFullPath(dllPath);

        if (!IsUnderTrustedRoot(fullPath))
        {
            throw new ArgumentException(
                "DLL must be located within the application directory or trusted WpfDevTools workspace",
                nameof(dllPath));
        }

        var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        if (fullPath.StartsWith(systemDir, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Cannot load DLL from system directories", nameof(dllPath));
        }

        var signatureAction = SignaturePolicy.Evaluate(isDebugBuild: IsDebugBuild);

        if (signatureAction == SignaturePolicy.Action.Skip)
        {
            System.Diagnostics.Trace.TraceInformation(
                "[SECURITY] DLL signature verification skipped per policy (DEBUG build, trusted context).");
        }
        else
        {
            VerifyAuthenticodeSignature(fullPath);
        }
    }

    private static bool IsUnderTrustedRoot(string fullPath)
    {
        foreach (var trustedRoot in GetTrustedRoots())
        {
            if (IsPathWithinRoot(fullPath, trustedRoot))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetTrustedRoots()
    {
        yield return Path.GetFullPath(AppContext.BaseDirectory);

        var solutionRoot = GetSolutionRoot(AppContext.BaseDirectory);
        if (solutionRoot != null)
        {
            yield return solutionRoot;
        }
    }

    private static bool IsPathWithinRoot(string fullPath, string rootPath)
    {
        var normalizedFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
        var normalizedRootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

        return normalizedFullPath.Equals(normalizedRootPath, StringComparison.OrdinalIgnoreCase) ||
               normalizedFullPath.StartsWith(normalizedRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void VerifyAuthenticodeSignature(string filePath)
    {
        try
        {
            using var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(filePath);

            if (cert == null)
            {
                throw new InvalidOperationException("DLL is not digitally signed");
            }

            using var cert2 = new System.Security.Cryptography.X509Certificates.X509Certificate2(cert);

            using var chain = new System.Security.Cryptography.X509Certificates.X509Chain();

            chain.ChainPolicy.RevocationMode = SignaturePolicy.GetRevocationMode(IsDebugBuild);
            chain.ChainPolicy.RevocationFlag = System.Security.Cryptography.X509Certificates.X509RevocationFlag.EntireChain;
            chain.ChainPolicy.VerificationFlags = System.Security.Cryptography.X509Certificates.X509VerificationFlags.NoFlag;

            if (!chain.Build(cert2))
            {
                var errors = string.Join(", ",
                    chain.ChainStatus.Select(s => $"{s.Status}: {s.StatusInformation}"));
                throw new InvalidOperationException(
                    $"Certificate chain validation failed: {errors}");
            }

            var now = DateTime.UtcNow;
            if (now < cert2.NotBefore.ToUniversalTime() || now > cert2.NotAfter.ToUniversalTime())
            {
                throw new InvalidOperationException(
                    $"Certificate has expired or is not yet valid. Valid from {cert2.NotBefore} to {cert2.NotAfter}");
            }

            var expectedThumbprint = Environment.GetEnvironmentVariable("WPFDEVTOOLS_CERT_THUMBPRINT");
            if (!string.IsNullOrEmpty(expectedThumbprint))
            {
                if (!cert2.Thumbprint.Equals(expectedThumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Certificate thumbprint mismatch. Expected: {expectedThumbprint}, Got: {cert2.Thumbprint}");
                }
            }
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Inspector DLL is not digitally signed or has an invalid signature. " +
                "In development, use a DEBUG build which auto-skips signature verification for local DLLs. " +
                "In production, sign the DLL with Authenticode.", ex);
        }
    }
}
