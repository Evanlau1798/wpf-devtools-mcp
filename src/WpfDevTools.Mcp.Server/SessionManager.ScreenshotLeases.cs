namespace WpfDevTools.Mcp.Server;

public sealed partial class SessionManager
{
    internal bool DetachScreenshotResource(int processId, string screenshotId)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (!_screenshotResources.TryGetValue(screenshotId, out var resource)
                || resource.ProcessId != processId
                || !_screenshotStorageRoots.TryGetValue(processId, out var storageRoot)
                || !SameScreenshotStorageRoot(storageRoot, resource.StorageRoot))
            {
                return false;
            }

            _screenshotResources[screenshotId] = resource with { ProcessId = null };
            _screenshotStorageRoots.Remove(processId);
            return true;
        }
    }

    private void TryDeleteUnreferencedScreenshotStorageRoot(string storageRoot)
    {
        if (_screenshotResources.Values.Any(resource => SameScreenshotStorageRoot(resource.StorageRoot, storageRoot))
            || _screenshotStorageRoots.Values.Any(root => SameScreenshotStorageRoot(root, storageRoot)))
        {
            return;
        }

        TryDeleteScreenshotDirectory(storageRoot);
    }

    private static bool SameScreenshotStorageRoot(string first, string second)
        => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
}
