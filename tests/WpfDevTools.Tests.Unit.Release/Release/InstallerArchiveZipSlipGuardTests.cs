using System.IO.Compression;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

/// <summary>
/// Contract tests for the <c>Assert-ArchiveSafeEntries</c> zip-slip guard in
/// <see href="../../../../scripts/installer/Installer.PackageIntegrity.ps1"/>.
/// </summary>
public sealed class InstallerArchiveZipSlipGuardTests
{
    [Fact]
    public void AssertArchiveSafeEntries_ShouldRejectArchiveWithTraversalEntry()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(tempRoot, "malicious.zip");
            CreateArchiveWithEntries(archivePath, new[]
            {
                ("normal.txt", "ok"),
                ("..\\escape.txt", "pwned"),
            });

            var result = InvokeGuard(archivePath, Path.Combine(tempRoot, "dest"));

            result.ExitCode.Should().NotBe(0, result.Stdout + result.Stderr);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("Unsafe archive entry")
                .And.Contain("escape.txt");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void AssertArchiveSafeEntries_ShouldRejectArchiveWithAbsolutePathEntry()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(tempRoot, "absolute.zip");
            CreateArchiveWithEntries(archivePath, new[]
            {
                ("C:\\Windows\\evil.exe", "pwned"),
            });

            var result = InvokeGuard(archivePath, Path.Combine(tempRoot, "dest"));

            result.ExitCode.Should().NotBe(0, result.Stdout + result.Stderr);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("Unsafe archive entry")
                .And.Contain("absolute path");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void AssertArchiveSafeEntries_ShouldAcceptWellFormedArchive()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(tempRoot, "clean.zip");
            CreateArchiveWithEntries(archivePath, new[]
            {
                ("bin/wpf-devtools-x64.exe", "exe"),
                ("manifest.json", "{}"),
                ("subdir/file.txt", "data"),
            });

            var result = InvokeGuard(archivePath, Path.Combine(tempRoot, "dest"));

            result.ExitCode.Should().Be(0, result.Stdout + result.Stderr);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static void CreateArchiveWithEntries(
        string archivePath,
        IEnumerable<(string Name, string Content)> entries)
    {
        using var stream = new FileStream(archivePath, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        foreach (var (name, content) in entries)
        {
            // ZipArchive.CreateEntry rejects some absolute/traversal names via
            // normalization; use the raw constructor via reflection-free API by
            // writing directly into the entry stream after creation.
            var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) InvokeGuard(
        string archivePath,
        string destinationPath)
    {
        var helperPath = ReleaseScriptTestHarness
            .GetRepoFilePath("scripts/installer/Installer.PackageIntegrity.ps1");
        var command =
            $". '{helperPath}'; " +
            $"Assert-ArchiveSafeEntries -ArchivePath '{archivePath}' -DestinationPath '{destinationPath}'";
        return ReleaseScriptTestHarness.RunPowerShellCommand(command);
    }
}
