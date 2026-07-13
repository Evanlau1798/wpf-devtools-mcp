namespace WpfDevTools.Mcp.Server;

public sealed partial class SessionManager
{
    private static bool TryDeleteUnregisteredScreenshotFileCore(string filePath, string storageRoot)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(storageRoot))
        {
            return false;
        }

        try
        {
            storageRoot = ResolveAndValidateScreenshotPath(storageRoot, "Screenshot storage directory");
            var fullPath = ResolveAndValidateScreenshotPath(filePath, "Screenshot file");
            if (!IsPathWithinRoot(fullPath, storageRoot))
            {
                return false;
            }

            var fileName = Path.GetFileName(fullPath);
            if (!IsValidScreenshotFileName(fileName))
            {
                return false;
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                TryDeleteScreenshotDirectory(storageRoot);
                return true;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return false;
    }
}
