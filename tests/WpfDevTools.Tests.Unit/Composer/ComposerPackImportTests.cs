using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("ComposerPackLoaderCache")]
public sealed class ComposerPackImportTests
{
    [Fact]
    public void PackImportService_ShouldReturnDryRunFilePlanWithoutWriting()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var archivePath = GetRepoFilePath("packs/baselines/wpfui/0.1.0/archives/wpfui-0.1.0.zip");
            using var archive = ZipFile.OpenRead(archivePath);
            var destinationRoot = Path.Combine(tempRoot, "packs");

            var plan = PackImportService.CreateDryRunPlan(archivePath, destinationRoot);

            plan.PackId.Should().Be("wpfui");
            plan.Version.Should().Be("0.1.0");
            plan.DryRun.Should().BeTrue();
            plan.FilePlan.Should().HaveCount(archive.Entries.Count);
            plan.FilePlan.Should().Contain(item => item.RelativePath == "pack.json");
            plan.FilePlan.Should().Contain(item => item.RelativePath == "recipes/tabbedSettings.recipe.json");
            plan.WouldModifyProjectFiles.Should().BeFalse();
            plan.WouldRunNuGetRestore.Should().BeFalse();
            Directory.Exists(destinationRoot).Should().BeFalse("dry-run import must not write files");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackImportService_ShouldRejectZipSlipEntries()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(tempRoot, "bad.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../evil.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("bad");
            }

            var act = () => PackImportService.CreateDryRunPlan(archivePath, Path.Combine(tempRoot, "packs"));

            act.Should().Throw<InvalidDataException>()
                .WithMessage("*Unsafe archive entry*evil.txt*");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Theory]
    [InlineData("C:/evil.txt")]
    [InlineData("/evil.txt")]
    [InlineData("wpfui\\0.1.0\\pack.json")]
    [InlineData("wpfui/0.1.0//evil.txt")]
    public void PackImportService_ShouldRejectUnsafeArchivePaths(string entryName)
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(tempRoot, "bad.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                archive.CreateEntry(entryName);
            }

            var act = () => PackImportService.CreateDryRunPlan(archivePath, Path.Combine(tempRoot, "packs"));

            act.Should().Throw<InvalidDataException>()
                .WithMessage("*Unsafe archive entry*");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackImportService_ShouldRejectArchiveLimitsAndSymlinkEntries()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(tempRoot, "bad.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "wpfui/0.1.0/pack.json", """
                    {"id":"wpfui","version":"0.1.0"}
                    """);
                var symlink = archive.CreateEntry("wpfui/0.1.0/link");
                symlink.ExternalAttributes = Convert.ToInt32("120000", 8) << 16;
            }

            var act = () => PackImportService.CreateDryRunPlan(
                archivePath,
                Path.Combine(tempRoot, "packs"),
                new PackImportLimits(MaxFileCount: 1));

            act.Should().Throw<InvalidDataException>()
                .WithMessage("*Archive contains too many files*");

            act = () => PackImportService.CreateDryRunPlan(archivePath, Path.Combine(tempRoot, "packs"));
            act.Should().Throw<InvalidDataException>()
                .WithMessage("*Unsupported archive entry type*");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackImportService_ShouldRejectPathAndSizeLimits()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(tempRoot, "bad.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "wpfui/0.1.0/pack.json", """
                    {"id":"wpfui","version":"0.1.0"}
                    """);
                WriteEntry(archive, "wpfui/0.1.0/blocks/button.block.json", "button");
            }

            var destinationRoot = Path.Combine(tempRoot, "packs");
            var pathAct = () => PackImportService.CreateDryRunPlan(
                archivePath,
                destinationRoot,
                new PackImportLimits(MaxEntryPathLength: 10));
            var entryAct = () => PackImportService.CreateDryRunPlan(
                archivePath,
                destinationRoot,
                new PackImportLimits(MaxEntryBytes: 1));
            var totalAct = () => PackImportService.CreateDryRunPlan(
                archivePath,
                destinationRoot,
                new PackImportLimits(MaxEntryBytes: 1000, MaxTotalBytes: 1));

            pathAct.Should().Throw<InvalidDataException>().WithMessage("*path is too long*");
            entryAct.Should().Throw<InvalidDataException>().WithMessage("*entry is too large*");
            totalAct.Should().Throw<InvalidDataException>().WithMessage("*total uncompressed size*");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackImportService_ShouldInstallWithManifestHashAndRejectOverwriteByDefault()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var archivePath = GetRepoFilePath("packs/baselines/wpfui/0.1.0/archives/wpfui-0.1.0.zip");
            var destinationRoot = Path.Combine(tempRoot, "packs");

            var plan = ImportReviewed(archivePath, destinationRoot);

            plan.DryRun.Should().BeFalse();
            plan.ArchiveSha256.Should().HaveLength(64);
            var installedRoot = Path.Combine(destinationRoot, "wpfui", "0.1.0");
            File.Exists(Path.Combine(installedRoot, "pack.json")).Should().BeTrue();

            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(installedRoot, "install.manifest.json")));
            manifest.RootElement.GetProperty("id").GetString().Should().Be("wpfui");
            manifest.RootElement.GetProperty("metadata").GetProperty("archiveSha256").GetString()
                .Should().Be(plan.ArchiveSha256);
            manifest.RootElement.GetProperty("metadata").GetProperty("sourceScope").GetString()
                .Should().Be("project-local");
            manifest.RootElement.GetProperty("metadata").GetProperty("installedAtUtc").GetString()
                .Should().NotBeNullOrWhiteSpace();

            var act = () => ImportReviewed(archivePath, destinationRoot);
            act.Should().Throw<IOException>()
                .WithMessage("*already exists*");

            var overwrite = ImportReviewed(archivePath, destinationRoot, allowOverwrite: true);
            overwrite.ArchiveSha256.Should().Be(plan.ArchiveSha256);
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackImportService_ShouldCleanStagingDirectoryWhenInstallFails()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var archivePath = CreateCollisionArchive(tempRoot, "bad.zip");
            var destinationRoot = Path.Combine(tempRoot, "packs");
            var act = () => ImportReviewed(archivePath, destinationRoot);

            act.Should().Throw<Exception>();
            Directory.Exists(Path.Combine(destinationRoot, ".staging")).Should().BeFalse();
            Directory.Exists(Path.Combine(destinationRoot, "wpfui", "0.1.0")).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackImportService_ShouldRejectIncompletePackBeforeInstall()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(tempRoot, "incomplete.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "wpfui/0.1.0/pack.json", """
                    {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"wpfui","version":"0.1.0"}
                    """);
            }

            var destinationRoot = Path.Combine(tempRoot, "packs");
            var act = () => ImportReviewed(archivePath, destinationRoot);

            act.Should().Throw<Exception>();
            Directory.Exists(Path.Combine(destinationRoot, "wpfui", "0.1.0")).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackImportService_ShouldPreserveExistingPackWhenOverwriteInstallFails()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var goodArchive = Path.Combine(tempRoot, "good.zip");
            using (var archive = ZipFile.Open(goodArchive, ZipArchiveMode.Create))
            {
                WriteMinimalPackEntries(archive);
                WriteEntry(archive, "wpfui/0.1.0/original.txt", "original");
            }

            var destinationRoot = Path.Combine(tempRoot, "packs");
            ImportReviewed(goodArchive, destinationRoot);

            var badArchive = CreateCollisionArchive(tempRoot, "bad.zip");
            var act = () => ImportReviewed(badArchive, destinationRoot, allowOverwrite: true);

            act.Should().Throw<Exception>();
            var installedRoot = Path.Combine(destinationRoot, "wpfui", "0.1.0");
            File.ReadAllText(Path.Combine(installedRoot, "original.txt")).Should().Be("original");
            Directory.Exists(Path.Combine(destinationRoot, ".staging")).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackImportService_ShouldNotCacheTransientStagingPackRoot()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(tempRoot, "pack.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteMinimalPackEntries(archive);
            }

            ComposerPackLoader.ClearCacheForTests();
            var destinationRoot = Path.Combine(tempRoot, "packs");
            ImportReviewed(archivePath, destinationRoot);

            ComposerPackLoader.CachedPackRootsForTests().Should().NotContain(
                root => root.Contains($"{Path.DirectorySeparatorChar}.staging{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase),
                "pack import validation must not cache transient staging pack roots");
        }
        finally
        {
            ComposerPackLoader.ClearCacheForTests();
            DeleteDirectory(tempRoot);
        }
    }

    private static string CreateCollisionArchive(string tempRoot, string fileName)
    {
        var archivePath = Path.Combine(tempRoot, fileName);
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        WriteEntry(archive, "wpfui/0.1.0/pack.json", """
            {"id":"wpfui","version":"0.1.0"}
            """);
        WriteEntry(archive, "wpfui/0.1.0/collision/file.txt", "file");
        WriteEntry(archive, "wpfui/0.1.0/collision", "file");
        return archivePath;
    }

    [Fact]
    public void PackImportService_ShouldUseOneArchiveSnapshotForPlanAndExtraction()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(tempRoot, "pack.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteMinimalPackEntries(archive);
                WriteEntry(archive, "wpfui/0.1.0/reviewed.txt", "reviewed");
            }

            var destinationRoot = Path.Combine(tempRoot, "packs");
            var review = PackImportService.CreateDryRunPlan(archivePath, destinationRoot);
            using var reviewedSnapshot = new MemoryStream(File.ReadAllBytes(archivePath));
            File.Delete(archivePath);
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteMinimalPackEntries(archive);
                WriteEntry(archive, "wpfui/0.1.0/unreviewed.txt", "unreviewed");
            }

            PackImportService.Import(
                reviewedSnapshot,
                destinationRoot,
                "project-local",
                review.ArchiveSha256);

            var installedRoot = Path.Combine(destinationRoot, "wpfui", "0.1.0");
            File.ReadAllText(Path.Combine(installedRoot, "reviewed.txt")).Should().Be("reviewed");
            File.Exists(Path.Combine(installedRoot, "unreviewed.txt")).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static PackImportPlan ImportReviewed(
        string archivePath,
        string destinationRoot,
        bool allowOverwrite = false)
    {
        var review = PackImportService.CreateDryRunPlan(archivePath, destinationRoot);
        return PackImportService.Import(
            archivePath,
            destinationRoot,
            "project-local",
            review.ArchiveSha256,
            allowOverwrite);
    }

    private static void WriteMinimalPackEntries(ZipArchive archive)
    {
        WriteEntry(archive, "wpfui/0.1.0/pack.json", """
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"wpfui","displayName":"WPF UI","version":"0.1.0","blocks":[],"recipes":[]}
            """);
        WriteEntry(archive, "wpfui/0.1.0/source.lock.json", """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}
            """);
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string GetRepoFilePath(string relativePath)
        => TestRepositoryPaths.GetRepoFilePath(relativePath);
}
