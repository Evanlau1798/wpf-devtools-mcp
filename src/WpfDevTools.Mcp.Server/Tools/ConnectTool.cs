using System.Text.Json;
using WpfDevTools.Injector;
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
    private readonly string _inspectorDllPath;

    /// <summary>
    /// Create ConnectTool with dependency injection
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking active connections</param>
    /// <param name="injector">Process injector for DLL injection</param>
    /// <param name="inspectorDllPath">Optional path to Inspector DLL. If null, uses default location.</param>
    /// <exception cref="ArgumentNullException">Thrown when sessionManager or injector is null</exception>
    /// <exception cref="FileNotFoundException">Thrown when specified Inspector DLL does not exist</exception>
    /// <exception cref="ArgumentException">Thrown when DLL path validation fails</exception>
    public ConnectTool(SessionManager sessionManager, IProcessInjector injector, string? inspectorDllPath = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));

        if (inspectorDllPath != null)
        {
            ValidateDllPath(inspectorDllPath);
            if (!File.Exists(inspectorDllPath))
                throw new FileNotFoundException("Inspector DLL not found", inspectorDllPath);
        }

        _inspectorDllPath = inspectorDllPath ?? GetInspectorDllPath();
        ValidateDllPath(_inspectorDllPath);
    }

    /// <summary>
    /// Create ConnectTool (backward compatibility constructor)
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking active connections</param>
    /// <param name="inspectorDllPath">Optional path to Inspector DLL. If null, uses default location.</param>
    /// <exception cref="ArgumentNullException">Thrown when sessionManager is null</exception>
    /// <exception cref="FileNotFoundException">Thrown when specified Inspector DLL does not exist</exception>
    /// <exception cref="ArgumentException">Thrown when DLL path validation fails</exception>
    public ConnectTool(SessionManager sessionManager, string? inspectorDllPath = null)
        : this(sessionManager, new ProcessInjector(), inspectorDllPath)
    {
    }

    /// <summary>
    /// Execute the tool to connect to a WPF process
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result object with success status and connection details</returns>
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
        // Use processId as the rate limit key (even if session doesn't exist yet)
        if (!_sessionManager.CheckRateLimit(processId.Value))
        {
            var availableTokens = _sessionManager.GetAvailableTokens(processId.Value);
            return new
            {
                success = false,
                error = "Rate limit exceeded for connect operations. Please slow down your requests.",
                availableTokens,
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

        // Perform injection
        var injectionResult = _injector.Inject(processId.Value, _inspectorDllPath);
        if (!injectionResult.Success)
        {
            return new
            {
                success = false,
                error = injectionResult.ErrorMessage ?? "Injection failed"
            };
        }

        // Add to session manager (also creates NamedPipeClient)
        _sessionManager.AddSession(processId.Value);

        // Connect to the Named Pipe
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

    private static string GetInspectorDllPath()
    {
        var serverDir = AppContext.BaseDirectory;
        foreach (var candidate in EnumerateInspectorDllCandidates(serverDir))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Don't expose full path in exception message for security
        // Log the actual path internally if needed
        throw new FileNotFoundException("Inspector DLL not found. Please ensure the application is built correctly.");
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
            InjectionError.ArchitectureMismatch => $"Architecture mismatch for process {processId}",
            _ => $"Validation failed: {error}"
        };
    }

    /// <summary>
    /// Validate DLL path to prevent security issues
    /// </summary>
    /// <param name="dllPath">Path to DLL file to validate</param>
    /// <exception cref="ArgumentException">Thrown when path is empty, not a .dll file, is a network path, not in application directory, or in system directories</exception>
    private static void ValidateDllPath(string dllPath)
    {
        if (string.IsNullOrWhiteSpace(dllPath))
            throw new ArgumentException("DLL path cannot be empty", nameof(dllPath));

        // Check if path has .dll extension
        if (!dllPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("DLL path must have .dll extension", nameof(dllPath));

        // Prevent network paths (UNC: \\server\share or //server/share)
        if (dllPath.StartsWith(@"\\", StringComparison.Ordinal) ||
            dllPath.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("Network paths are not allowed", nameof(dllPath));

        // SECURITY: Normalize path first to prevent traversal attacks
        // Path.GetFullPath resolves "..", ".", and other relative components
        var fullPath = Path.GetFullPath(dllPath);

        // SECURITY: Whitelist approach - only allow DLLs from application directory
        // This prevents path traversal attacks like "C:\\App\\..\\..\\System32\\evil.dll"
        if (!IsUnderTrustedRoot(fullPath))
        {
            throw new ArgumentException(
                "DLL must be located within the application directory or trusted WpfDevTools workspace",
                nameof(dllPath));
        }

        // Additional check: Prevent system directories (defense in depth)
        var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        if (fullPath.StartsWith(systemDir, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Cannot load DLL from system directories", nameof(dllPath));
        }

        // SECURITY: Verify Authenticode signature
        // CRITICAL FIX: Environment variable bypass only in DEBUG builds for testing
        // RELEASE builds ALWAYS verify signatures - no exceptions
#if DEBUG
        var skipSignatureCheck = Environment.GetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK") == "1";
        if (skipSignatureCheck)
        {
            System.Diagnostics.Trace.TraceWarning(
                "[SECURITY] DLL signature verification bypassed via WPFDEVTOOLS_SKIP_SIGNATURE_CHECK. " +
                "This is only allowed in DEBUG builds.");
        }
        else
        {
            VerifyAuthenticodeSignature(fullPath);
        }
#else
        // RELEASE builds ALWAYS verify signatures - no environment variable check
        VerifyAuthenticodeSignature(fullPath);
#endif
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

    /// <summary>
    /// Verify Authenticode signature of the DLL with full certificate chain validation
    /// </summary>
    /// <param name="filePath">Path to file to verify signature</param>
    /// <exception cref="InvalidOperationException">Thrown when DLL is not signed, certificate chain validation fails, certificate is expired, or thumbprint mismatch</exception>
    private static void VerifyAuthenticodeSignature(string filePath)
    {
        try
        {
            // Load certificate from signed file
            using var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(filePath);

            if (cert == null)
            {
                throw new InvalidOperationException("DLL is not digitally signed");
            }

            // Convert to X509Certificate2 for chain validation
            using var cert2 = new System.Security.Cryptography.X509Certificates.X509Certificate2(cert);

            // SECURITY: Verify certificate chain and trust
            using var chain = new System.Security.Cryptography.X509Certificates.X509Chain();

            // CRITICAL FIX: Use offline revocation mode in DEBUG to prevent network blocking
            // In RELEASE builds, use online revocation for maximum security
#if DEBUG
            chain.ChainPolicy.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.Offline;
#else
            chain.ChainPolicy.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.Online;
#endif
            chain.ChainPolicy.RevocationFlag = System.Security.Cryptography.X509Certificates.X509RevocationFlag.EntireChain;
            chain.ChainPolicy.VerificationFlags = System.Security.Cryptography.X509Certificates.X509VerificationFlags.NoFlag;

            if (!chain.Build(cert2))
            {
                var errors = string.Join(", ",
                    chain.ChainStatus.Select(s => $"{s.Status}: {s.StatusInformation}"));
                throw new InvalidOperationException(
                    $"Certificate chain validation failed: {errors}");
            }

            // SECURITY: Verify certificate is not expired
            var now = DateTime.UtcNow;
            if (now < cert2.NotBefore.ToUniversalTime() || now > cert2.NotAfter.ToUniversalTime())
            {
                throw new InvalidOperationException(
                    $"Certificate has expired or is not yet valid. Valid from {cert2.NotBefore} to {cert2.NotAfter}");
            }

            // SECURITY: Optional thumbprint verification for additional security
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
                $"DLL signature verification failed: {ex.Message}", ex);
        }
    }
}
