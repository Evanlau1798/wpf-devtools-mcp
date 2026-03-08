using System.Security.Cryptography.X509Certificates;

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
        if (!IsUnderTrustedRoot(fullPath))
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

        var solutionRoot = DllCandidateResolver.GetSolutionRoot(AppContext.BaseDirectory);
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

    internal static X509RevocationMode GetCurrentBuildRevocationMode()
        => SignaturePolicy.GetRevocationMode(IsDebugBuild);
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

