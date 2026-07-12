namespace WpfDevTools.Tests.Unit.TestSupport;

internal static class TestDirectory
{
    public static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpf-devtools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void Delete(string path)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            try
            {
                NormalizeAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }

    private static void NormalizeAttributes(string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(entry, FileAttributes.Normal);
        }

        File.SetAttributes(root, FileAttributes.Normal);
    }
}
