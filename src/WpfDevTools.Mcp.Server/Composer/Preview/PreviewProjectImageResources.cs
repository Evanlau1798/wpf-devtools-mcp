using WpfDevTools.Mcp.Server.Composer.Apply;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal static class PreviewProjectImageResources
{
    public static void Copy(
        string? projectRoot,
        string previewRoot,
        IReadOnlyList<string> relativePaths)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || relativePaths.Count == 0)
        {
            return;
        }

        foreach (var relativePath in relativePaths)
        {
            var source = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
            var destination = Path.GetFullPath(Path.Combine(previewRoot, relativePath));
            if (!ProjectWritePolicy.IsPathUnderRoot(projectRoot, source)
                || !ProjectWritePolicy.IsPathUnderRoot(previewRoot, destination))
            {
                throw new InvalidOperationException("Preview image resource escaped its reviewed root.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }
}
