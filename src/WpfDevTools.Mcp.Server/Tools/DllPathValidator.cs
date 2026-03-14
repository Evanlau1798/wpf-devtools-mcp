using System.Security.Cryptography.X509Certificates;
using WpfDevTools.Shared.IO;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// SECURITY: Validates DLL paths before injection.
/// Enforces trusted-root whitelist, blocks network/system paths,
/// and verifies Authenticode signatures in Release builds.
/// </summary>
internal static class DllPathValidator
{
#if DEBUG
    private static readonly bool IsDebugBuild = true;
#else
    private static readonly bool IsDebugBuild = false;
#endif

    /// <summary>
    /// Validate DLL path to prevent path traversal and untrusted loading.
    /// </summary>
    public static void ValidateDllPath(string dllPath)
        => ValidateDllPath(dllPath, AppContext.BaseDirectory);

    internal static void ValidateDllPath(string dllPath, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(dllPath))
            throw new ArgumentException("DLL path cannot be empty", nameof(dllPath));

        if (!dllPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("DLL path must have .dll extension", nameof(dllPath));

        // SECURITY: Block network (UNC) paths
        if (dllPath.StartsWith(@"\\", StringComparison.Ordinal) ||
            dllPath.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("Network paths are not allowed", nameof(dllPath));

        // SECURITY: Normalize path to prevent traversal attacks (..)
        var fullPath = Path.GetFullPath(dllPath);

        // SECURITY: Whitelist approach ??only allow DLLs under trusted roots
        if (!IsUnderTrustedRoot(fullPath, baseDirectory))
        {
            throw new ArgumentException(
                "DLL must be located within the application directory or trusted WpfDevTools workspace",
                nameof(dllPath));
        }

        // SECURITY: Additional guard against system directories
        var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        if (fullPath.StartsWith(systemDir, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Cannot load DLL from system directories", nameof(dllPath));
        }

        var signatureAction = GetSignatureAction(baseDirectory);

        if (signatureAction == SignaturePolicy.Action.Skip)
        {
            System.Diagnostics.Trace.TraceInformation(
                "[SECURITY] DLL signature verification skipped per policy (development build, trusted context).");
        }
        else
        {
            VerifyAuthenticodeSignature(fullPath);
        }
    }

    private static bool IsUnderTrustedRoot(string fullPath, string baseDirectory)
    {
        foreach (var trustedRoot in EnumerateTrustedRoots(baseDirectory))
        {
            if (IsPathWithinRoot(fullPath, trustedRoot))
            {
                return true;
            }
        }

        return false;
    }

    internal static IReadOnlyList<string> EnumerateTrustedRoots(string baseDirectory)
    {
        var trustedRoots = new List<string> { Path.GetFullPath(baseDirectory) };

        foreach (var solutionRoot in RepositoryLayoutLocator.EnumerateSolutionRoots(baseDirectory))
        {
            if (!trustedRoots.Contains(solutionRoot, StringComparer.OrdinalIgnoreCase))
            {
                trustedRoots.Add(solutionRoot);
            }
        }

        return trustedRoots;
    }

    private static bool IsPathWithinRoot(string fullPath, string rootPath)
    {
        var normalizedFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
        var normalizedRootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

        return normalizedFullPath.Equals(normalizedRootPath, StringComparison.OrdinalIgnoreCase) ||
               normalizedFullPath.StartsWith(normalizedRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    internal static SignaturePolicy.Action GetCurrentBuildSignatureAction()
        => GetSignatureAction(AppContext.BaseDirectory);

    internal static SignaturePolicy.Action GetSignatureAction(string baseDirectory)
        => SignaturePolicy.Evaluate(
            isDebugBuild: IsDebugBuild,
            isTrustedLocalDevelopmentBuild: IsTrustedLocalDevelopmentBuild(baseDirectory));

    internal static X509RevocationMode GetCurrentBuildRevocationMode()
        => SignaturePolicy.GetRevocationMode(
            isDebugBuild: IsDebugBuild,
            isTrustedLocalDevelopmentBuild: IsTrustedLocalDevelopmentBuild(AppContext.BaseDirectory));

    internal static bool IsTrustedLocalDevelopmentBuild(string baseDirectory)
    {
        if (IsDebugBuild)
        {
            return true;
        }

        var buildConfiguration = TryGetBuildConfiguration(baseDirectory);
        if (string.IsNullOrWhiteSpace(buildConfiguration)
            || string.Equals(buildConfiguration, "Release", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return RepositoryLayoutLocator.EnumerateSolutionRoots(baseDirectory).Any();
    }

    internal static string? TryGetBuildConfiguration(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return null;
        }

        var normalizedBaseDirectory = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var currentDirectory = new DirectoryInfo(normalizedBaseDirectory);

        if (string.Equals(currentDirectory.Name, "publish", StringComparison.OrdinalIgnoreCase))
        {
            currentDirectory = currentDirectory.Parent!;
        }

        while (currentDirectory != null)
        {
            var configurationDirectory = currentDirectory.Parent;
            var binDirectory = configurationDirectory?.Parent;

            if (configurationDirectory != null
                && binDirectory != null
                && string.Equals(binDirectory.Name, "bin", StringComparison.OrdinalIgnoreCase))
            {
                return configurationDirectory.Name;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return null;
    }

    private static void VerifyAuthenticodeSignature(string filePath)
    {
        try
        {
            using var cert = X509Certificate.CreateFromSignedFile(filePath);

            if (cert == null)
            {
                throw new InvalidOperationException("DLL is not digitally signed");
            }

            using var cert2 = new X509Certificate2(cert);
            using var chain = new X509Chain();

            chain.ChainPolicy.RevocationMode = GetCurrentBuildRevocationMode();
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

            if (!chain.Build(cert2))
            {
                var errors = string.Join(", ",
                    chain.ChainStatus.Select(s => $"{s.Status}: {s.StatusInformation}"));
                throw new InvalidOperationException(
                    $"Certificate chain validation failed: {errors}");
            }

            var validityWindow = new CertificateValidityWindow(
                new DateTimeOffset(cert2.NotBefore),
                new DateTimeOffset(cert2.NotAfter));
            var now = DateTimeOffset.UtcNow;
            if (!validityWindow.Contains(now))
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
                        "Certificate thumbprint does not match the expected value configured in WPFDEVTOOLS_CERT_THUMBPRINT.");
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
