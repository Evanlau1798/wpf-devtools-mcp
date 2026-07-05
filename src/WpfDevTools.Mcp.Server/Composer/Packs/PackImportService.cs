using System.IO.Compression;
using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Composer.Packs;

internal static class PackImportService
{
    public static PackImportPlan CreateDryRunPlan(string archivePath, string destinationRoot)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var files = archive.Entries
            .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .ToArray();

        var root = GetRoot(files);
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
            var relativePath = entry.FullName[(root.Prefix.Length + 1)..];
            return new PackImportFilePlan(
                relativePath,
                Path.GetFullPath(Path.Combine(destinationRoot, root.PackId, root.Version, relativePath.Replace('/', Path.DirectorySeparatorChar))),
                entry.Length);
        }).ToArray();

        return new PackImportPlan(root.PackId, root.Version, true, filePlan, false, false);
    }

    private static (string PackId, string Version, string Prefix) GetRoot(IReadOnlyCollection<ZipArchiveEntry> entries)
    {
        string? packId = null;
        string? version = null;
        foreach (var entry in entries)
        {
            var parts = ValidateEntry(entry.FullName);
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

    private static string[] ValidateEntry(string fullName)
    {
        if (fullName.Contains('\\') || Path.IsPathRooted(fullName))
        {
            throw new InvalidDataException($"Unsafe archive entry '{fullName}'.");
        }

        var parts = fullName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or ".."))
        {
            throw new InvalidDataException($"Unsafe archive entry '{fullName}'.");
        }

        return parts;
    }
}

internal sealed record PackImportPlan(
    string PackId,
    string Version,
    bool DryRun,
    IReadOnlyList<PackImportFilePlan> FilePlan,
    bool WouldModifyProjectFiles,
    bool WouldRunNuGetRestore);

internal sealed record PackImportFilePlan(string RelativePath, string TargetPath, long Length);
