using System.Text;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal sealed partial class UiBlueprintApplyService
{
    private static ApplyFileWriteResult WriteViewFile(string projectRoot, string targetPath, string content)
    {
        var targetDirectory = Path.GetDirectoryName(targetPath)!;
        string? backupPath = null;
        var tempPath = Path.Combine(
            targetDirectory,
            "." + Path.GetFileName(targetPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        var existed = File.Exists(targetPath);
        try
        {
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(tempPath, content, Encoding.UTF8);
            if (existed)
            {
                var relative = Path.GetRelativePath(projectRoot, targetPath);
                backupPath = Path.Combine(projectRoot, ".wpfdevtools-backups", DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), relative);
                if (ProjectWritePolicy.FindReparsePoint(projectRoot, backupPath) is { } backupReparsePoint)
                {
                    TryDeleteFile(tempPath);
                    return ApplyFileWriteResult.CreateFailure(
                        backupPath,
                        existed,
                        new ApplyBlueprintIssue(
                            "$.targetPath",
                            "ProjectBackupPathUsesReparsePoint",
                            $"Project backup path uses a reparse point: {backupReparsePoint}.",
                            "Remove the backup directory reparse point or choose a project root without reparse-point backup paths."));
                }

                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Replace(tempPath, targetPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }

            return ApplyFileWriteResult.CreateSuccess(backupPath, existed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteFile(tempPath);
            var message = backupPath is null
                ? $"Project write failed before any target file was replaced: {ex.Message}"
                : $"Project write failed and the original target remains protected by atomic replace semantics. Backup path: {backupPath}. Error: {ex.Message}";
            return ApplyFileWriteResult.CreateFailure(
                backupPath,
                existed,
                new ApplyBlueprintIssue(
                    "$.targetPath",
                    "ProjectWriteFailed",
                    message,
                    "Resolve the file lock or filesystem permission issue, then rerun dry-run before applying again."));
        }
    }

    private static ExistingContentReadResult ReadExistingContent(string targetPath)
    {
        try
        {
            return ExistingContentReadResult.CreateSuccess(File.Exists(targetPath) ? File.ReadAllText(targetPath) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ExistingContentReadResult.CreateFailure(new ApplyBlueprintIssue(
                "$.targetPath",
                "ProjectWriteFailed",
                $"Project write failed before existing safe-slot content could be read: {ex.Message}",
                "Resolve the file lock or filesystem permission issue, then rerun dry-run before applying again."));
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
