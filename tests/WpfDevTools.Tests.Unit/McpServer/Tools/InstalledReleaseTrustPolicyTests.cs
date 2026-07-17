using FluentAssertions;
using System.IO.Compression;
using System.Security.Cryptography;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("ProcessEnvironment")]
public sealed class InstalledReleaseTrustPolicyTests
{
    [Fact]
    public void CanSkipSignatureForChecksumOnlyPayload_WithInstalledInspectorPayload_ShouldReturnFalse()
    {
        using var layout = InstalledLayout.Create();

        var result = InstalledReleaseTrustPolicy.CanSkipSignatureForChecksumOnlyPayload(
            layout.InspectorNet8Path,
            layout.BaseDirectory,
            layout.ExecutablePath);

        result.Should().BeFalse(
            "mutable installed metadata cannot authenticate the Inspector bytes loaded into a target process");
    }

    [Fact]
    public void CanSkipSignatureForChecksumOnlyPayload_WithInstalledBootstrapperPayload_ShouldReturnFalse()
    {
        using var layout = InstalledLayout.Create();

        var result = InstalledReleaseTrustPolicy.CanSkipSignatureForChecksumOnlyPayload(
            layout.BootstrapperPath,
            layout.BaseDirectory,
            layout.ExecutablePath);

        result.Should().BeFalse(
            "mutable installed metadata cannot authenticate the native Bootstrapper bytes loaded into a target process");
    }

    [Fact]
    public void CanSkipSignatureForChecksumOnlyPayload_WithUnlistedDll_ShouldReturnFalse()
    {
        using var layout = InstalledLayout.Create();
        var unlistedPath = Path.Combine(layout.BaseDirectory, "unlisted.dll");
        File.WriteAllText(unlistedPath, string.Empty);

        var result = InstalledReleaseTrustPolicy.CanSkipSignatureForChecksumOnlyPayload(
            unlistedPath,
            layout.BaseDirectory,
            layout.ExecutablePath);

        result.Should().BeFalse("the checksum-only trust path must not become a broad directory-wide signature bypass");
    }

    [Fact]
    public void CanSkipSignatureForChecksumOnlyPayload_WithSignedPolicyManifest_ShouldReturnFalse()
    {
        using var layout = InstalledLayout.Create(packageSignaturePolicy: "RequireAuthenticodeSignature");

        var result = InstalledReleaseTrustPolicy.CanSkipSignatureForChecksumOnlyPayload(
            layout.InspectorNet8Path,
            layout.BaseDirectory,
            layout.ExecutablePath);

        result.Should().BeFalse("signed releases should continue to require Authenticode validation");
    }

    [Fact]
    public void CanSkipSignatureForChecksumOnlyPayload_WithMismatchedExecutable_ShouldReturnFalse()
    {
        using var layout = InstalledLayout.Create();
        var otherExecutable = Path.Combine(layout.BaseDirectory, "wpf-devtools-other.exe");
        File.WriteAllText(otherExecutable, string.Empty);

        var result = InstalledReleaseTrustPolicy.CanSkipSignatureForChecksumOnlyPayload(
            layout.InspectorNet8Path,
            layout.BaseDirectory,
            otherExecutable);

        result.Should().BeFalse("the install manifest must bind the trust decision to the running packaged server executable");
    }

    [Fact]
    public void CanSkipSignatureForChecksumOnlyPayload_WithPortableReleaseArchiveAndShaSidecar_ShouldReturnTrue()
    {
        using var layout = PortableReleaseLayout.Create();

        var result = InstalledReleaseTrustPolicy.CanSkipSignatureForChecksumOnlyPayload(
            layout.InspectorNet8Path,
            layout.BaseDirectory,
            layout.ExecutablePath);

        result.Should().BeTrue(
            "directly extracted GitHub prerelease assets should keep the checksum-only trust path when the original ZIP and SHA sidecar still verify the payload");
    }

    [Fact]
    public void CanSkipSignatureForChecksumOnlyPayload_WithTrustedReleaseMetadataDirectory_ShouldReturnTrue()
    {
        using var layout = PortableReleaseLayout.Create();
        var metadataDirectory = Path.Combine(layout.Root, "trusted-metadata");
        Directory.CreateDirectory(metadataDirectory);
        File.Move(layout.ArchivePath, Path.Combine(metadataDirectory, Path.GetFileName(layout.ArchivePath)));
        File.Move(layout.ShaSidecarPath, Path.Combine(metadataDirectory, Path.GetFileName(layout.ShaSidecarPath)));

        var previousMetadataDirectory = Environment.GetEnvironmentVariable("WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY");
        try
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY", metadataDirectory);

            var result = InstalledReleaseTrustPolicy.CanSkipSignatureForChecksumOnlyPayload(
                layout.InspectorNet8Path,
                layout.BaseDirectory,
                layout.ExecutablePath);

            result.Should().BeTrue(
                "portable prerelease validation should accept an explicit trusted release metadata directory without copying sidecars beside the extracted package");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY", previousMetadataDirectory);
        }
    }

    [Fact]
    public void CanSkipSignatureForChecksumOnlyPayload_WithoutPortableReleaseShaSidecar_ShouldReturnFalse()
    {
        using var layout = PortableReleaseLayout.Create();
        File.Delete(layout.ShaSidecarPath);

        var result = InstalledReleaseTrustPolicy.CanSkipSignatureForChecksumOnlyPayload(
            layout.InspectorNet8Path,
            layout.BaseDirectory,
            layout.ExecutablePath);

        result.Should().BeFalse("portable checksum-only trust must remain tied to published release SHA metadata");
    }

    [Fact]
    public void CanSkipSignatureForChecksumOnlyPayload_WithTamperedPortablePayload_ShouldReturnFalse()
    {
        using var layout = PortableReleaseLayout.Create();
        File.WriteAllText(layout.InspectorNet8Path, "tampered");

        var result = InstalledReleaseTrustPolicy.CanSkipSignatureForChecksumOnlyPayload(
            layout.InspectorNet8Path,
            layout.BaseDirectory,
            layout.ExecutablePath);

        result.Should().BeFalse("portable payload bytes must still match the verified GitHub release ZIP entry");
    }

    [Fact]
    public void PortableReleaseTrust_ShouldNotReopenArchivePathAfterHashValidation()
    {
        var source = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(
            "src/WpfDevTools.Mcp.Server/Tools/InstalledReleaseTrustPolicy.cs"));

        source.Should().NotContain("ZipFile.OpenRead(archivePath)",
            "all archive entries must be read from the same locked handle that passed SHA-256 validation");
    }

    private sealed class InstalledLayout : IDisposable
    {
        private InstalledLayout(
            string root,
            string baseDirectory,
            string executablePath,
            string inspectorNet8Path,
            string bootstrapperPath)
        {
            Root = root;
            BaseDirectory = baseDirectory;
            ExecutablePath = executablePath;
            InspectorNet8Path = inspectorNet8Path;
            BootstrapperPath = bootstrapperPath;
        }

        public string Root { get; }
        public string BaseDirectory { get; }
        public string ExecutablePath { get; }
        public string InspectorNet8Path { get; }
        public string BootstrapperPath { get; }

        public static InstalledLayout Create(string packageSignaturePolicy = "ReleaseChecksumOnly")
        {
            var root = Path.Combine(Path.GetTempPath(), "wpf-devtools-installed-trust-" + Guid.NewGuid().ToString("N"));
            var installRoot = Path.Combine(root, "installed-product");
            var installBase = Path.Combine(installRoot, "x64");
            var currentDir = Path.Combine(installBase, "current");
            var baseDirectory = Path.Combine(currentDir, "bin");
            var inspectorDir = Path.Combine(baseDirectory, "inspectors", "net8.0-windows");
            var bootstrapperDir = Path.Combine(baseDirectory, "bootstrapper", "x64");
            Directory.CreateDirectory(inspectorDir);
            Directory.CreateDirectory(bootstrapperDir);

            var executablePath = Path.Combine(baseDirectory, "wpf-devtools-x64.exe");
            var inspectorNet8Path = Path.Combine(inspectorDir, "WpfDevTools.Inspector.dll");
            var bootstrapperPath = Path.Combine(bootstrapperDir, "WpfDevTools.Bootstrapper.x64.dll");
            File.WriteAllText(executablePath, string.Empty);
            File.WriteAllText(inspectorNet8Path, string.Empty);
            File.WriteAllText(bootstrapperPath, string.Empty);

            File.WriteAllText(
                Path.Combine(baseDirectory, "manifest.json"),
                $$"""
                {
                  "name": "wpf-devtools",
                  "version": "1.0.0-beta.2",
                  "architecture": "x64",
                  "channel": "release",
                  "buildConfiguration": "Release",
                  "signaturePolicy": "{{packageSignaturePolicy}}",
                  "entryExecutable": "bin/wpf-devtools-x64.exe",
                  "inspector": {
                    "net8": "bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll",
                    "net48": "bin/inspectors/net48/WpfDevTools.Inspector.dll"
                  },
                  "bootstrapper": "bin/bootstrapper/x64/WpfDevTools.Bootstrapper.x64.dll"
                }
                """);

            File.WriteAllText(
                Path.Combine(installBase, "install-manifest.json"),
                $$"""
                {
                  "name": "wpf-devtools",
                  "architecture": "x64",
                  "version": "1.0.0-beta.2",
                  "installRoot": "{{EscapeJson(installRoot)}}",
                  "installDir": "{{EscapeJson(currentDir)}}",
                  "executable": "{{EscapeJson(executablePath)}}",
                  "channel": "release",
                  "buildConfiguration": "Release",
                  "signaturePolicy": "ReleaseChecksumOnly",
                  "installedUtc": "2026-06-24T00:00:00.0000000Z"
                }
                """);

            return new InstalledLayout(root, baseDirectory, executablePath, inspectorNet8Path, bootstrapperPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string EscapeJson(string value)
            => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private sealed class PortableReleaseLayout : IDisposable
    {
        private PortableReleaseLayout(
            string root,
            string baseDirectory,
            string executablePath,
            string inspectorNet8Path,
            string archivePath,
            string shaSidecarPath)
        {
            Root = root;
            BaseDirectory = baseDirectory;
            ExecutablePath = executablePath;
            InspectorNet8Path = inspectorNet8Path;
            ArchivePath = archivePath;
            ShaSidecarPath = shaSidecarPath;
        }

        public string Root { get; }
        public string BaseDirectory { get; }
        public string ExecutablePath { get; }
        public string InspectorNet8Path { get; }
        public string ArchivePath { get; }
        public string ShaSidecarPath { get; }

        public static PortableReleaseLayout Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "wpf-devtools-portable-trust-" + Guid.NewGuid().ToString("N"));
            var releaseDirectory = Path.Combine(root, "release");
            var packageRoot = Path.Combine(releaseDirectory, "extracted");
            var baseDirectory = Path.Combine(packageRoot, "bin");
            var inspectorDir = Path.Combine(baseDirectory, "inspectors", "net8.0-windows");
            var bootstrapperDir = Path.Combine(baseDirectory, "bootstrapper", "x64");
            Directory.CreateDirectory(inspectorDir);
            Directory.CreateDirectory(bootstrapperDir);

            var executablePath = Path.Combine(baseDirectory, "wpf-devtools-x64.exe");
            var inspectorNet8Path = Path.Combine(inspectorDir, "WpfDevTools.Inspector.dll");
            var bootstrapperPath = Path.Combine(bootstrapperDir, "WpfDevTools.Bootstrapper.x64.dll");
            File.WriteAllText(executablePath, "portable exe");
            File.WriteAllText(inspectorNet8Path, "portable inspector");
            File.WriteAllText(bootstrapperPath, "portable bootstrapper");

            File.WriteAllText(
                Path.Combine(baseDirectory, "manifest.json"),
                """
                {
                  "name": "wpf-devtools",
                  "version": "1.0.0-beta.7",
                  "architecture": "x64",
                  "runtimeId": "win-x64",
                  "channel": "release",
                  "buildConfiguration": "Release",
                  "signaturePolicy": "ReleaseChecksumOnly",
                  "entryExecutable": "bin/wpf-devtools-x64.exe",
                  "inspector": {
                    "net8": "bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll",
                    "net48": "bin/inspectors/net48/WpfDevTools.Inspector.dll"
                  },
                  "bootstrapper": "bin/bootstrapper/x64/WpfDevTools.Bootstrapper.x64.dll"
                }
                """);

            var archivePath = Path.Combine(releaseDirectory, "release_1.0.0-beta.7_win-x64.zip");
            ZipFile.CreateFromDirectory(packageRoot, archivePath);

            var archiveHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archivePath))).ToLowerInvariant();
            var shaSidecarPath = Path.Combine(releaseDirectory, "SHA256SUMS.txt");
            File.WriteAllText(shaSidecarPath, archiveHash + "  release_1.0.0-beta.7_win-x64.zip");

            return new PortableReleaseLayout(root, baseDirectory, executablePath, inspectorNet8Path, archivePath, shaSidecarPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
