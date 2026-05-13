using System.Diagnostics;
using System.Text.Json;
using System.Threading;

namespace WpfDevTools.Tests.Unit.Release;

internal static partial class ReleaseScriptTestHarness
{
    public static (string Thumbprint, string Subject) GetSignedPayloadSigner()
    {
        var signedPayload = SignedPayload.Value;
        return (signedPayload.Thumbprint, signedPayload.Subject);
    }

    public static (string Path, string Thumbprint, string Subject) CreateSelfSignedPayloadForTesting(string tempRoot)
    {
        var signedPayload = CreateSelfSignedPayloadInfo(tempRoot, []);
        return (signedPayload.Path, signedPayload.Thumbprint, signedPayload.Subject);
    }

    public static void CleanupGeneratedCertificateForTesting(string thumbprint)
    {
        CleanupGeneratedCertificate(thumbprint);
    }

    private static SignedPayloadInfo ResolveSignedPayloadInfo()
    {
        var errors = new List<string>();
        foreach (var candidatePath in EnumerateSignedPayloadCandidatePaths())
        {
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            if (TryGetSignedPayloadSignerMetadata(candidatePath, out var signer, out var error))
            {
                return new SignedPayloadInfo(candidatePath, signer.Thumbprint, signer.Subject);
            }

            errors.Add(error);
        }

        return CreateSelfSignedPayloadInfo(
            Path.Combine(GetRepoFilePath("tmp"), "release-script-harness-signed-payload", Guid.NewGuid().ToString("N")),
            errors);
    }

    private static SignedPayloadInfo CreateSelfSignedPayloadInfo(string payloadRoot, IReadOnlyCollection<string> discoveryErrors)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(
                "Could not locate a signed Windows payload that exposes signer metadata. " + string.Join(" | ", discoveryErrors));
        }

        Directory.CreateDirectory(payloadRoot);
        var payloadPath = Path.Combine(payloadRoot, "payload.exe");
        var certificateThumbprintPath = Path.Combine(payloadRoot, "payload.cert-thumbprint.txt");
        File.Copy(ResolveSelfSignedPayloadTemplatePath(), payloadPath, overwrite: true);

        var subject = "CN=WpfDevTools Harness Signed Payload " + Guid.NewGuid().ToString("N");
        var command = string.Join(" ",
            "$ErrorActionPreference = 'Stop';",
            "Remove-TypeData -TypeName System.Security.AccessControl.ObjectSecurity -ErrorAction SilentlyContinue;",
            "Import-Module Microsoft.PowerShell.Security -ErrorAction Stop;",
            "try { Import-Module PKI -ErrorAction Stop } catch { };",
            "if ($null -eq (Get-PSProvider Certificate -ErrorAction SilentlyContinue)) { throw 'Certificate provider is unavailable.' };",
            "if ($null -eq (Get-PSDrive -Name Cert -ErrorAction SilentlyContinue)) { New-PSDrive -Name Cert -PSProvider Certificate -Root '\\' -ErrorAction Stop | Out-Null };",
            "Get-Command New-SelfSignedCertificate -ErrorAction Stop | Out-Null;",
            "$payload = " + QuotePowerShellString(payloadPath) + ";",
            "$thumbprintPath = " + QuotePowerShellString(certificateThumbprintPath) + ";",
            "$subject = " + QuotePowerShellString(subject) + ";",
            "$cert = $null; $success = $false;",
            "try {",
            "$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\\CurrentUser\\My -NotAfter (Get-Date).AddDays(1);",
            "Set-Content -LiteralPath $thumbprintPath -Value $cert.Thumbprint -Encoding ASCII;",
            "$store = [System.Security.Cryptography.X509Certificates.X509Store]::new('Root', 'CurrentUser');",
            "$store.Open('ReadWrite');",
            "try { $store.Add($cert) } finally { $store.Close() };",
            "$signature = Set-AuthenticodeSignature -FilePath $payload -Certificate $cert;",
            "$check = Get-AuthenticodeSignature -FilePath $payload;",
            "if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or $check.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or $null -eq $check.SignerCertificate) { throw \"Self-signed payload signature did not validate. Set=$($signature.Status); Check=$($check.Status).\" };",
            "$success = $true;",
            "[ordered]@{ Thumbprint = $check.SignerCertificate.Thumbprint; Subject = $check.SignerCertificate.Subject; CertificateThumbprint = $cert.Thumbprint } | ConvertTo-Json -Compress",
            "}",
            "finally {",
            "if (-not $success -and $null -ne $cert) { foreach ($storeName in @('Root', 'My')) { $cleanupStore = [System.Security.Cryptography.X509Certificates.X509Store]::new($storeName, 'CurrentUser'); $cleanupStore.Open('ReadWrite'); try { foreach ($cleanupCert in @($cleanupStore.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $cert.Thumbprint, $false))) { $cleanupStore.Remove($cleanupCert) } } finally { $cleanupStore.Close() } } }",
            "}");
        (int ExitCode, string Stdout, string Stderr) result;
        try
        {
            result = RunPowerShellCommand(command, timeout: SelfSignedPayloadTimeout);
        }
        catch
        {
            CleanupGeneratedCertificateFromFile(certificateThumbprintPath);
            throw;
        }

        var generatedThumbprint = RegisterGeneratedCertificateFromFile(certificateThumbprintPath);

        if (result.ExitCode != 0)
        {
            CleanupGeneratedCertificateIfKnown(generatedThumbprint);
            throw new InvalidOperationException(
                "Could not create a self-signed payload for release test harness. " +
                string.Join(" | ", discoveryErrors) + " | " + result.Stderr);
        }

        using var payload = JsonDocument.Parse(result.Stdout);
        var root = payload.RootElement;
        var thumbprint = root.GetProperty("Thumbprint").GetString();
        var signerSubject = root.GetProperty("Subject").GetString();
        var certificateThumbprint = root.GetProperty("CertificateThumbprint").GetString();
        if (string.IsNullOrWhiteSpace(thumbprint) ||
            string.IsNullOrWhiteSpace(signerSubject) ||
            string.IsNullOrWhiteSpace(certificateThumbprint))
        {
            CleanupGeneratedCertificateIfKnown(generatedThumbprint);
            throw new InvalidOperationException("Self-signed payload signer metadata was incomplete.");
        }

        RegisterGeneratedCertificateCleanup(certificateThumbprint);
        return new SignedPayloadInfo(payloadPath, thumbprint, signerSubject);
    }

    private static string? RegisterGeneratedCertificateFromFile(string certificateThumbprintPath)
    {
        var thumbprint = TryReadGeneratedCertificateThumbprint(certificateThumbprintPath);
        if (!string.IsNullOrWhiteSpace(thumbprint))
        {
            RegisterGeneratedCertificateCleanup(thumbprint);
        }

        return thumbprint;
    }

    private static void CleanupGeneratedCertificateFromFile(string certificateThumbprintPath)
    {
        CleanupGeneratedCertificateIfKnown(TryReadGeneratedCertificateThumbprint(certificateThumbprintPath));
    }

    private static string? TryReadGeneratedCertificateThumbprint(string certificateThumbprintPath)
    {
        if (!File.Exists(certificateThumbprintPath))
        {
            return null;
        }

        var thumbprint = File.ReadAllText(certificateThumbprintPath).Trim();
        return string.IsNullOrWhiteSpace(thumbprint) ? null : thumbprint;
    }

    private static void CleanupGeneratedCertificateIfKnown(string? thumbprint)
    {
        if (!string.IsNullOrWhiteSpace(thumbprint))
        {
            CleanupGeneratedCertificate(thumbprint);
        }
    }

    private static string ResolveSelfSignedPayloadTemplatePath()
    {
        foreach (var candidate in EnumerateSelfSignedPayloadTemplatePaths())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not locate an executable PE file to use as the self-signed test payload template.");
    }

    private static IEnumerable<string?> EnumerateSelfSignedPayloadTemplatePaths()
    {
        yield return Environment.ProcessPath;
        yield return Process.GetCurrentProcess().MainModule?.FileName;

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            yield return Path.Combine(dotnetRoot, "dotnet.exe");
        }

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet",
            "dotnet.exe");
    }

    private static void RegisterGeneratedCertificateCleanup(string thumbprint)
    {
        GeneratedCertificateThumbprints.TryAdd(thumbprint, 0);
        if (Interlocked.Exchange(ref generatedCertificateCleanupRegistered, 1) == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupGeneratedCertificates();
        }
    }

    private static void CleanupGeneratedCertificates()
    {
        foreach (var thumbprint in GeneratedCertificateThumbprints.Keys)
        {
            try
            {
                CleanupGeneratedCertificate(thumbprint);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ReleaseScriptTestHarness: certificate cleanup skipped for '{thumbprint}': {ex.Message}");
            }
        }
    }

    private static void CleanupGeneratedCertificate(string thumbprint)
    {
        var command = string.Join(" ",
            "$thumbprint = " + QuotePowerShellString(thumbprint) + ";",
            "foreach ($storeName in @('Root', 'My')) {",
            "$store = [System.Security.Cryptography.X509Certificates.X509Store]::new($storeName, 'CurrentUser');",
            "$store.Open('ReadWrite');",
            "try { foreach ($cert in @($store.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $thumbprint, $false))) { $store.Remove($cert) } } finally { $store.Close() }",
            "}");
        RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(10));
    }

    private static IEnumerable<string> EnumerateSignedPayloadCandidatePaths()
    {
        var system32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32");
        foreach (var relativePath in new[]
                 {
                     Path.Combine("WindowsPowerShell", "v1.0", "powershell.exe"),
                     "notepad.exe",
                     "cmd.exe",
                     "wscript.exe",
                     "cscript.exe",
                     "msiexec.exe",
                     "regedit.exe"
                 })
        {
            yield return Path.Combine(system32, relativePath);
        }

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            yield return Path.Combine(dotnetRoot, "dotnet.exe");
        }

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PowerShell",
            "7",
            "pwsh.exe");
    }

    private static bool TryGetSignedPayloadSignerMetadata(
        string signedSourcePath,
        out (string Thumbprint, string Subject) signer,
        out string error)
    {
        signer = default;
        error = string.Empty;
        var command = string.Join(" ",
            "$ErrorActionPreference = 'Stop';",
            "$sig = Get-AuthenticodeSignature -FilePath " + QuotePowerShellString(signedSourcePath) + ";",
            "[ordered]@{ Status = [string]$sig.Status; Thumbprint = if ($null -ne $sig.SignerCertificate) { $sig.SignerCertificate.Thumbprint } else { $null }; Subject = if ($null -ne $sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { $null } } | ConvertTo-Json -Compress");

        var result = RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(10));
        if (result.ExitCode != 0)
        {
            error = $"Failed to resolve signer metadata for {signedSourcePath}: {result.Stderr}";
            return false;
        }

        using var payload = JsonDocument.Parse(result.Stdout);
        var status = payload.RootElement.GetProperty("Status").GetString();
        var thumbprint = payload.RootElement.GetProperty("Thumbprint").GetString();
        var subject = payload.RootElement.GetProperty("Subject").GetString();
        if (!string.Equals(status, "Valid", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(thumbprint) ||
            string.IsNullOrWhiteSpace(subject))
        {
            error = $"Signed payload {signedSourcePath} did not expose valid signer metadata. Status: {status ?? "<null>"}.";
            return false;
        }

        signer = (thumbprint, subject);
        return true;
    }

}
