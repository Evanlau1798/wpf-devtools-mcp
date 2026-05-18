using System.IO;

namespace WpfDevTools.Mcp.Server;

public sealed partial class SessionManager
{
    private const int RetainedScreenshotResourceLimit = 100;
    private const string ScreenshotResourcePrefix = "wpf://screenshots/";
    private const string ScreenshotFileExtension = ".png";
    private readonly Dictionary<string, StoredScreenshotResource> _screenshotResources = new(StringComparer.Ordinal);
    private readonly Queue<string> _screenshotResourceOrder = new();

    internal StoredScreenshotResource RegisterScreenshotResource(
        int processId,
        string screenshotId,
        string filePath,
        string? sha256)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(screenshotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!IsValidScreenshotId(screenshotId))
        {
            throw new ArgumentException("Screenshot ID must match the inspector-generated shot_<32 hex chars> format.", nameof(screenshotId));
        }

        var fullPath = Path.GetFullPath(filePath);
        var expectedFileName = screenshotId + ScreenshotFileExtension;
        var fileName = Path.GetFileName(fullPath);
        if (!string.Equals(fileName, expectedFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Screenshot file name must match screenshotId plus .png.", nameof(filePath));
        }

        var resource = new StoredScreenshotResource(
            processId,
            screenshotId,
            ScreenshotResourcePrefix + screenshotId,
            fullPath,
            fileName,
            sha256);

        lock (_lock)
        {
            if (!_screenshotResources.ContainsKey(screenshotId))
            {
                _screenshotResourceOrder.Enqueue(screenshotId);
            }

            _screenshotResources[screenshotId] = resource;
            TrimScreenshotResources();
        }

        return resource;
    }

    internal bool TryGetScreenshotResource(string screenshotId, out StoredScreenshotResource resource)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            return _screenshotResources.TryGetValue(screenshotId, out resource!);
        }
    }

    private void RemoveScreenshotResources(int processId)
    {
        var removedAny = false;
        foreach (var screenshotId in _screenshotResources
            .Where(entry => entry.Value.ProcessId == processId)
            .Select(entry => entry.Key)
            .ToArray())
        {
            removedAny |= _screenshotResources.Remove(screenshotId);
        }

        if (removedAny)
        {
            CompactScreenshotResourceOrder();
        }
    }

    private void TrimScreenshotResources()
    {
        while (_screenshotResources.Count > RetainedScreenshotResourceLimit &&
            _screenshotResourceOrder.TryDequeue(out var oldestScreenshotId))
        {
            _screenshotResources.Remove(oldestScreenshotId);
        }
    }

    private void CompactScreenshotResourceOrder()
    {
        var retainedOrder = _screenshotResourceOrder
            .Where(_screenshotResources.ContainsKey)
            .ToArray();

        _screenshotResourceOrder.Clear();
        foreach (var screenshotId in retainedOrder)
        {
            _screenshotResourceOrder.Enqueue(screenshotId);
        }
    }

    private static bool IsValidScreenshotId(string screenshotId)
    {
        if (screenshotId.Length != 37 ||
            !screenshotId.StartsWith("shot_", StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 5; index < screenshotId.Length; index++)
        {
            var character = screenshotId[index];
            if ((character < '0' || character > '9') &&
                (character < 'a' || character > 'f') &&
                (character < 'A' || character > 'F'))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed record StoredScreenshotResource(
    int ProcessId,
    string ScreenshotId,
    string ResourceUri,
    string FilePath,
    string FileName,
    string? Sha256);
