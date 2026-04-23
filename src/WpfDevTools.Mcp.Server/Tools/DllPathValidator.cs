using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using WpfDevTools.Shared.IO;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// SECURITY: Validates DLL paths before injection.
/// Enforces trusted-root whitelist, blocks network/system paths,
/// and verifies Authenticode signatures in Release builds.
/// </summary>
internal static class DllPathValidator
{
    private const string SkipSignatureCheckEnvironmentVariable = "WPFDEVTOOLS_SKIP_SIGNATURE_CHECK";
    private const string ReleaseSignerThumbprintEnvironmentVariable = "WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT";
    private const string ReleaseSignerSubjectEnvironmentVariable = "WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT";
    private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    internal static Func<string, int>? WinVerifyTrustOverrideForTesting { get; set; }
    internal static Func<string, ValidatedAuthenticodeSigner?>? ValidatedSignerOverrideForTesting { get; set; }
    internal static ValidatedAuthenticodeSigner? CurrentProcessReleaseSignerOverrideForTesting { get; set; }
    internal static bool? TrustedLocalDevelopmentBuildOverrideForTesting { get; set; }

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
                "[SECURITY] DLL signature verification skipped per policy (debug build or trusted local opt-in).");
        }
        else
        {
            VerifyAuthenticodeSignature(fullPath, baseDirectory);
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
    {
        var isTrustedLocalDevelopmentBuild = IsTrustedLocalDevelopmentBuild(baseDirectory);

        return SignaturePolicy.Evaluate(
            isDebugBuild: IsDebugBuild,
            isTrustedLocalDevelopmentBuild: isTrustedLocalDevelopmentBuild,
            isTrustedLocalDevelopmentSkipOptIn: IsTrustedLocalDevelopmentSignatureSkipOptInEnabled(isTrustedLocalDevelopmentBuild));
    }

    internal static X509RevocationMode GetCurrentBuildRevocationMode()
    {
        var isTrustedLocalDevelopmentBuild = IsTrustedLocalDevelopmentBuild(AppContext.BaseDirectory);

        return SignaturePolicy.GetRevocationMode(
            isDebugBuild: IsDebugBuild,
            isTrustedLocalDevelopmentBuild: isTrustedLocalDevelopmentBuild,
            isTrustedLocalDevelopmentSkipOptIn: IsTrustedLocalDevelopmentSignatureSkipOptInEnabled(isTrustedLocalDevelopmentBuild));
    }

    internal static bool IsTrustedLocalDevelopmentSignatureSkipOptInEnabled(string baseDirectory)
        => IsTrustedLocalDevelopmentSignatureSkipOptInEnabled(IsTrustedLocalDevelopmentBuild(baseDirectory));

    internal static bool IsTrustedLocalDevelopmentBuild(string baseDirectory)
    {
        if (TrustedLocalDevelopmentBuildOverrideForTesting is bool overrideValue)
        {
            return overrideValue;
        }

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

    private static bool IsTrustedLocalDevelopmentSignatureSkipOptInEnabled(bool isTrustedLocalDevelopmentBuild)
    {
        if (!isTrustedLocalDevelopmentBuild)
        {
            return false;
        }

        var value = Environment.GetEnvironmentVariable(SkipSignatureCheckEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
            || (bool.TryParse(value, out var enabled) && enabled);
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

    private static void VerifyAuthenticodeSignature(string filePath, string baseDirectory)
    {
        try
        {
            var isTrustedLocalDevelopmentBuild = IsTrustedLocalDevelopmentBuild(baseDirectory);
            var expectedSigner = isTrustedLocalDevelopmentBuild
                ? null
                : GetExpectedReleaseSigner();
            if (!isTrustedLocalDevelopmentBuild && expectedSigner is null)
            {
                throw new System.Security.Cryptography.CryptographicException(
                    "Release DLL validation requires a pinned signer from the signed MCP server executable or " +
                    "WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT.");
            }

            VerifyFileAuthenticodeTrust(filePath);

            var validatedSignerOverride = ValidatedSignerOverrideForTesting?.Invoke(filePath);
            if (validatedSignerOverride is not null)
            {
                VerifyCertificateValidity(validatedSignerOverride.NotBefore, validatedSignerOverride.NotAfter);
                VerifyExpectedReleaseSigner(
                    validatedSignerOverride.Thumbprint,
                    validatedSignerOverride.Subject,
                    expectedSigner);
                return;
            }

            using var cert = X509Certificate.CreateFromSignedFile(filePath);

            if (cert == null)
            {
                throw new System.Security.Cryptography.CryptographicException("DLL is not digitally signed");
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
                throw new System.Security.Cryptography.CryptographicException(
                    $"Certificate chain validation failed: {errors}");
            }

            VerifyCertificateValidity(new DateTimeOffset(cert2.NotBefore), new DateTimeOffset(cert2.NotAfter));
            VerifyExpectedReleaseSigner(cert2.Thumbprint, cert2.Subject, expectedSigner);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw CreateInvalidSignatureException(ex);
        }
    }

    private static System.Security.Cryptography.CryptographicException CreateInvalidSignatureException(Exception innerException)
    {
        return new System.Security.Cryptography.CryptographicException(
            "Inspector DLL is not digitally signed or has an invalid signature. " +
            "In development, use a DEBUG build which auto-skips signature verification for local DLLs. " +
            "In production, sign the DLL with Authenticode.", innerException);
    }

    private static void VerifyCertificateValidity(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        var validityWindow = new CertificateValidityWindow(notBefore, notAfter);
        var now = DateTimeOffset.UtcNow;
        if (!validityWindow.Contains(now))
        {
            throw new System.Security.Cryptography.CryptographicException(
                $"Certificate has expired or is not yet valid. Valid from {notBefore} to {notAfter}");
        }
    }

    private static void VerifyExpectedReleaseSigner(
        string? certificateThumbprint,
        string? certificateSubject,
        ReleaseSignerMetadata? expectedSigner)
    {
        if (expectedSigner is null)
        {
            return;
        }

        var actualThumbprint = NormalizeThumbprint(certificateThumbprint);
        if (!string.IsNullOrWhiteSpace(expectedSigner.Thumbprint)
            && !string.Equals(actualThumbprint, expectedSigner.Thumbprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new System.Security.Cryptography.CryptographicException(
                $"Certificate thumbprint does not match the pinned release signer. Expected '{expectedSigner.Thumbprint}' but got '{actualThumbprint}'.");
        }

        var actualSubject = NormalizeOptionalValue(certificateSubject);
        if (!string.IsNullOrWhiteSpace(expectedSigner.Subject)
            && !string.Equals(actualSubject, expectedSigner.Subject, StringComparison.OrdinalIgnoreCase))
        {
            throw new System.Security.Cryptography.CryptographicException(
                $"Certificate subject does not match the pinned release signer. Expected '{expectedSigner.Subject}' but got '{actualSubject}'.");
        }
    }

    private static ReleaseSignerMetadata? GetExpectedReleaseSigner()
    {
        var environmentSigner = GetReleaseSignerFromEnvironment();
        if (environmentSigner is not null)
        {
            return environmentSigner;
        }

        return GetReleaseSignerFromCurrentServerExecutable();
    }

    private static ReleaseSignerMetadata? GetReleaseSignerFromEnvironment()
    {
        var thumbprint = NormalizeThumbprint(Environment.GetEnvironmentVariable(ReleaseSignerThumbprintEnvironmentVariable));
        var subject = NormalizeOptionalValue(Environment.GetEnvironmentVariable(ReleaseSignerSubjectEnvironmentVariable));
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            if (!string.IsNullOrWhiteSpace(subject))
            {
                throw new System.Security.Cryptography.CryptographicException(
                    "WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT requires WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT.");
            }

            return null;
        }

        return CreateReleaseSignerMetadata(thumbprint, subject);
    }

    private static ReleaseSignerMetadata? GetReleaseSignerFromCurrentServerExecutable()
    {
        if (CurrentProcessReleaseSignerOverrideForTesting is { } overrideSigner)
        {
            return CreateReleaseSignerMetadata(overrideSigner.Thumbprint, overrideSigner.Subject);
        }

        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
            {
                return null;
            }

            VerifyFileAuthenticodeTrust(processPath);
            using var cert = X509Certificate.CreateFromSignedFile(processPath);
            if (cert == null)
            {
                return null;
            }

            using var cert2 = new X509Certificate2(cert);
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = GetCurrentBuildRevocationMode();
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            if (!chain.Build(cert2))
            {
                return null;
            }

            VerifyCertificateValidity(new DateTimeOffset(cert2.NotBefore), new DateTimeOffset(cert2.NotAfter));
            return CreateReleaseSignerMetadata(cert2.Thumbprint, cert2.Subject);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static ReleaseSignerMetadata? CreateReleaseSignerMetadata(string? thumbprint, string? subject)
        => string.IsNullOrWhiteSpace(thumbprint)
            ? null
            : new ReleaseSignerMetadata(NormalizeThumbprint(thumbprint), NormalizeOptionalValue(subject));

    private static string? NormalizeThumbprint(string? thumbprint)
        => string.IsNullOrWhiteSpace(thumbprint)
            ? null
            : thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    private static string? NormalizeOptionalValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void VerifyFileAuthenticodeTrust(string filePath)
    {
        var fileInfo = new WinTrustFileInfo(filePath);
        var fileInfoPointer = IntPtr.Zero;

        try
        {
            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);

            var trustData = new WinTrustData(fileInfoPointer);
            var result = WinVerifyTrustOverrideForTesting?.Invoke(filePath)
                ?? WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref trustData);
            if (result != 0)
            {
                throw new System.Security.Cryptography.CryptographicException(
                    $"Authenticode verification failed for '{filePath}' (WinVerifyTrust HRESULT: 0x{result:X8}).");
            }
        }
        finally
        {
            if (fileInfoPointer != IntPtr.Zero)
            {
                Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
                Marshal.FreeCoTaskMem(fileInfoPointer);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private readonly struct WinTrustFileInfo
    {
        private readonly uint _cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>();
        [MarshalAs(UnmanagedType.LPWStr)]
        private readonly string _filePath = string.Empty;
        private readonly IntPtr _fileHandle = IntPtr.Zero;
        private readonly IntPtr _knownSubject = IntPtr.Zero;

        public WinTrustFileInfo(string filePath)
        {
            _filePath = filePath;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private readonly struct WinTrustData
    {
        private const uint UiChoiceNone = 2;
        private const uint RevocationChecksNone = 0;
        private const uint ChoiceFile = 1;
        private const uint StateActionIgnore = 0;
        private const uint ProviderFlagsSafer = 0x00000100;
        private const uint UiContextExecute = 0;

        private readonly uint _cbStruct = (uint)Marshal.SizeOf<WinTrustData>();
        private readonly IntPtr _policyCallbackData = IntPtr.Zero;
        private readonly IntPtr _sipClientData = IntPtr.Zero;
        private readonly uint _uiChoice = UiChoiceNone;
        private readonly uint _revocationChecks = RevocationChecksNone;
        private readonly uint _unionChoice = ChoiceFile;
        private readonly IntPtr _fileInfoPointer = IntPtr.Zero;
        private readonly uint _stateAction = StateActionIgnore;
        private readonly IntPtr _stateData = IntPtr.Zero;
        private readonly IntPtr _urlReference = IntPtr.Zero;
        private readonly uint _providerFlags = ProviderFlagsSafer;
        private readonly uint _uiContext = UiContextExecute;

        public WinTrustData(IntPtr fileInfoPointer)
        {
            _fileInfoPointer = fileInfoPointer;
        }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        ref WinTrustData trustData);

    private sealed record ReleaseSignerMetadata(string? Thumbprint, string? Subject);

    internal sealed record ValidatedAuthenticodeSigner(
        string Thumbprint,
        string Subject,
        DateTimeOffset NotBefore,
        DateTimeOffset NotAfter);
}
