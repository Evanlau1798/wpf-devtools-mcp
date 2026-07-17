using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Diagnostics;

namespace WpfDevTools.Mcp.Server.Composer.Packs;

internal static class PackImportService
{
    public static PackImportPlan CreateDryRunPlan(
        string archivePath,
        string destinationRoot,
        PackImportLimits? limits = null)
    {
        using var archiveStream = OpenArchive(archivePath);
        var archiveSha256 = ComputeSha256(archiveStream);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        return CreatePlan(
            archive,
            destinationRoot,
            dryRun: true,
            limits ?? PackImportLimits.Default,
            archiveSha256);
    }

    public static PackImportPlan Import(
        string archivePath,
        string destinationRoot,
        string sourceScope,
        string reviewedArchiveSha256,
        bool allowOverwrite = false,
        PackImportLimits? limits = null)
    {
        using var archiveStream = OpenArchive(archivePath);
        return Import(
            archiveStream,
            destinationRoot,
            sourceScope,
            reviewedArchiveSha256,
            allowOverwrite,
            limits);
    }

    public static PackImportPlan Import(
        Stream archiveStream,
        string destinationRoot,
        string sourceScope,
        string reviewedArchiveSha256,
        bool allowOverwrite = false,
        PackImportLimits? limits = null)
    {
        var activeLimits = limits ?? PackImportLimits.Default;
        var archiveSha256 = ComputeSha256(archiveStream);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        var plan = CreatePlan(archive, destinationRoot, dryRun: false, activeLimits, archiveSha256);
        if (!string.Equals(
                plan.ArchiveSha256,
                reviewedArchiveSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new PackImportPlanChangedException();
        }

        var destination = Path.Combine(destinationRoot, plan.PackId, plan.Version);
        if (Directory.Exists(destination) && !allowOverwrite)
        {
            throw new IOException($"Pack '{plan.PackId}' {plan.Version} already exists.");
        }

        var stagingRoot = Path.Combine(destinationRoot, ".staging");
        var stagingBase = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        var stagingPackRoot = Path.Combine(stagingBase, plan.PackId, plan.Version);
        var backupDestination = Path.Combine(stagingBase, "previous");
        var cleanupStaging = true;
        try
        {
            Directory.CreateDirectory(stagingPackRoot);
            ExtractArchive(archive, $"{plan.PackId}/{plan.Version}", stagingPackRoot, activeLimits);
            WriteInstallManifest(stagingPackRoot, destination, plan, sourceScope);
            ComposerPackLoader.LoadUncachedForValidation(stagingPackRoot);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (Directory.Exists(destination))
            {
                Directory.Move(destination, backupDestination);
            }

            Directory.Move(stagingPackRoot, destination);
            TryDeleteDirectory(backupDestination);
            cleanupStaging = false;
            TryDeleteDirectory(stagingBase);
            return plan;
        }
        catch (Exception ex)
        {
            if (Directory.Exists(backupDestination))
            {
                if (Directory.Exists(destination))
                {
                    cleanupStaging = false;
                    throw new IOException(
                        $"Pack overwrite rollback could not restore '{destination}'. Backup retained at '{backupDestination}'.",
                        ex);
                }

                try
                {
                    Directory.Move(backupDestination, destination);
                }
                catch (Exception restoreException)
                {
                    cleanupStaging = false;
                    throw new IOException(
                        $"Pack overwrite rollback failed for '{destination}'. Backup retained at '{backupDestination}'.",
                        new AggregateException(ex, restoreException));
                }
            }

            throw;
        }
        finally
        {
            if (cleanupStaging)
            {
                DeleteDirectoryIfExists(stagingBase);
            }

            DeleteEmptyDirectoryIfExists(stagingRoot);
        }
    }

    private static PackImportPlan CreatePlan(
        ZipArchive archive,
        string destinationRoot,
        bool dryRun,
        PackImportLimits limits,
        string archiveSha256)
    {
        var files = archive.Entries
            .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .ToArray();

        ValidateLimits(files, limits);
        var root = GetRoot(files, limits);
        var packJsonEntry = files.SingleOrDefault(entry => entry.FullName == $"{root.PackId}/{root.Version}/pack.json")
            ?? throw new InvalidDataException("Archive must contain pack.json under <pack-id>/<version>/.");

        using var packJson = JsonDocument.Parse(packJsonEntry.Open());
        var packId = packJson.RootElement.GetProperty("id").GetString();
        var version = packJson.RootElement.GetProperty("version").GetString();
        if (!string.Equals(packId, root.PackId, StringComparison.Ordinal) ||
            !string.Equals(version, root.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Archive root must match pack id and version.");
        }

        var filePlan = files.Select(entry =>
        {
            var relativePath = GetRelativeArchivePath(entry.FullName, root.Prefix, limits);
            var targetRoot = Path.Combine(destinationRoot, root.PackId, root.Version);
            return new PackImportFilePlan(
                relativePath,
                GetSafeTargetPath(targetRoot, relativePath, entry.FullName),
                entry.Length);
        }).ToArray();

        return new PackImportPlan(
            root.PackId,
            root.Version,
            dryRun,
            filePlan,
            false,
            false,
            archiveSha256,
            ComposerObservability.ForPackImport(root.PackId, root.Version, dryRun, filePlan.Length));
    }

    private static void ValidateLimits(IReadOnlyCollection<ZipArchiveEntry> entries, PackImportLimits limits)
    {
        if (entries.Count > limits.MaxFileCount)
        {
            throw new InvalidDataException("Archive contains too many files.");
        }

        long totalBytes = 0;
        foreach (var entry in entries)
        {
            if (entry.FullName.Length > limits.MaxEntryPathLength)
            {
                throw new InvalidDataException($"Archive entry path is too long: '{entry.FullName}'.");
            }

            if (entry.Length > limits.MaxEntryBytes)
            {
                throw new InvalidDataException($"Archive entry is too large: '{entry.FullName}'.");
            }

            totalBytes += entry.Length;
            if (totalBytes > limits.MaxTotalBytes)
            {
                throw new InvalidDataException("Archive total uncompressed size is too large.");
            }
        }
    }

    private static (string PackId, string Version, string Prefix) GetRoot(
        IReadOnlyCollection<ZipArchiveEntry> entries,
        PackImportLimits limits)
    {
        string? packId = null;
        string? version = null;
        foreach (var entry in entries)
        {
            RejectUnsupportedEntryType(entry);
            var parts = ValidateEntry(entry.FullName, limits);
            if (parts.Length < 3)
            {
                throw new InvalidDataException("Archive entries must use <pack-id>/<version>/<path>.");
            }

            packId ??= parts[0];
            version ??= parts[1];
            if (!string.Equals(packId, parts[0], StringComparison.Ordinal) ||
                !string.Equals(version, parts[1], StringComparison.Ordinal))
            {
                throw new InvalidDataException("Archive must contain a single <pack-id>/<version>/ root.");
            }
        }

        if (packId is null || version is null)
        {
            throw new InvalidDataException("Archive is empty.");
        }

        return (packId, version, $"{packId}/{version}");
    }

    private static string[] ValidateEntry(string fullName, PackImportLimits limits)
    {
        if (fullName.Length > limits.MaxEntryPathLength
            || fullName.Contains('\\', StringComparison.Ordinal)
            || fullName.Contains(':', StringComparison.Ordinal)
            || Path.IsPathRooted(fullName))
        {
            throw new InvalidDataException($"Unsafe archive entry '{fullName}'.");
        }

        var parts = fullName.Split('/');
        if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".."))
        {
            throw new InvalidDataException($"Unsafe archive entry '{fullName}'.");
        }

        return parts;
    }

    private static void RejectUnsupportedEntryType(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixMode == 0xA000)
        {
            throw new InvalidDataException($"Unsupported archive entry type '{entry.FullName}'.");
        }
    }

    private static void ExtractArchive(
        ZipArchive archive,
        string prefix,
        string destination,
        PackImportLimits limits)
    {
        var files = archive.Entries
            .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .ToArray();
        ValidateLimits(files, limits);

        foreach (var entry in files)
        {
            RejectUnsupportedEntryType(entry);
            var relativePath = GetRelativeArchivePath(entry.FullName, prefix, limits);
            var targetPath = GetSafeTargetPath(destination, relativePath, entry.FullName);

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath);
        }
    }

    private static string GetRelativeArchivePath(string fullName, string prefix, PackImportLimits limits)
    {
        ValidateEntry(fullName, limits);
        if (!fullName.StartsWith(prefix + "/", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Archive entry is outside the expected root: '{fullName}'.");
        }

        return fullName[(prefix.Length + 1)..];
    }

    private static string GetSafeTargetPath(string destinationRoot, string relativePath, string entryName)
    {
        var normalizedRoot = Path.GetFullPath(destinationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var targetPath = Path.GetFullPath(
            Path.Combine(destinationRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!targetPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsafe archive entry '{entryName}'.");
        }

        return targetPath;
    }

    private static void WriteInstallManifest(
        string stagingPackRoot,
        string destination,
        PackImportPlan plan,
        string sourceScope)
    {
        var manifest = new
        {
            schemaVersion = UiComposerSchemaVersions.PackInstallManifest,
            id = plan.PackId,
            version = plan.Version,
            scope = sourceScope,
            path = destination,
            enabled = true,
            metadata = new
            {
                archiveSha256 = plan.ArchiveSha256,
                installedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                sourceScope
            }
        };

        File.WriteAllText(
            Path.Combine(stagingPackRoot, "install.manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            DeleteDirectoryIfExists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteEmptyDirectoryIfExists(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
        }
    }

    private static FileStream OpenArchive(string archivePath)
        => new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);

    private static string ComputeSha256(Stream archiveStream)
    {
        if (!archiveStream.CanRead || !archiveStream.CanSeek)
        {
            throw new ArgumentException("Archive stream must be readable and seekable.", nameof(archiveStream));
        }

        archiveStream.Position = 0;
        var digest = Convert.ToHexString(SHA256.HashData(archiveStream)).ToLowerInvariant();
        archiveStream.Position = 0;
        return digest;
    }
}

internal sealed record PackImportLimits(
    int MaxFileCount = 1000,
    long MaxEntryBytes = 10L * 1024 * 1024,
    long MaxTotalBytes = 50L * 1024 * 1024,
    int MaxEntryPathLength = 240)
{
    public static PackImportLimits Default { get; } = new();
}

internal sealed record PackImportPlan(
    string PackId,
    string Version,
    bool DryRun,
    IReadOnlyList<PackImportFilePlan> FilePlan,
    bool WouldModifyProjectFiles,
    bool WouldRunNuGetRestore,
    string ArchiveSha256,
    ComposerObservabilityPayload Observability);

internal sealed record PackImportFilePlan(string RelativePath, string TargetPath, long Length);

internal sealed class PackImportPlanChangedException : InvalidOperationException
{
    public PackImportPlanChangedException()
        : base("Pack archive content changed after review; run a new dry import.")
    {
    }
}
