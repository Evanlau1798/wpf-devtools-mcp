using System.IO;

namespace WpfDevTools.Mcp.Server;

public sealed partial class SessionManager
{
    internal const int RetainedScreenshotResourceLimit = 100;
    internal static readonly TimeSpan ScreenshotResourceRetentionWindow = TimeSpan.FromHours(24);

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

        var registeredAtUtc = _utcNowProvider();
        var resource = new StoredScreenshotResource(
            processId,
            screenshotId,
            ScreenshotResourcePrefix + screenshotId,
            fullPath,
            fileName,
            sha256,
            registeredAtUtc,
            registeredAtUtc.Add(ScreenshotResourceRetentionWindow));

        lock (_lock)
        {
            TrimExpiredScreenshotResources(registeredAtUtc, retainedScreenshotId: screenshotId);
            if (!_screenshotResources.ContainsKey(screenshotId))
            {
                _screenshotResourceOrder.Enqueue(screenshotId);
            }
            else
            {
                RemoveScreenshotResource(screenshotId, protectedFilePath: fullPath);
            }

            _screenshotResources[screenshotId] = resource;
            TrimScreenshotResources(retainedScreenshotId: screenshotId);
        }

        return resource;
    }

    internal bool TryGetScreenshotResource(string screenshotId, out StoredScreenshotResource resource)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            TrimExpiredScreenshotResources(_utcNowProvider());
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
            removedAny |= RemoveScreenshotResource(screenshotId);
        }

        if (removedAny)
        {
            CompactScreenshotResourceOrder();
        }
    }

    private void TrimScreenshotResources(string? retainedScreenshotId = null)
    {
        while (_screenshotResources.Count > RetainedScreenshotResourceLimit &&
            _screenshotResourceOrder.TryDequeue(out var oldestScreenshotId))
        {
            if (string.Equals(oldestScreenshotId, retainedScreenshotId, StringComparison.Ordinal))
            {
                _screenshotResourceOrder.Enqueue(oldestScreenshotId);
                continue;
            }

            RemoveScreenshotResource(oldestScreenshotId);
        }
    }

    private void TrimExpiredScreenshotResources(
        DateTimeOffset now,
        string? retainedScreenshotId = null)
    {
        var removedAny = false;
        foreach (var screenshotId in _screenshotResources
            .Where(entry => !string.Equals(entry.Key, retainedScreenshotId, StringComparison.Ordinal)
                && entry.Value.ExpiresAtUtc <= now)
            .Select(entry => entry.Key)
            .ToArray())
        {
            removedAny |= RemoveScreenshotResource(screenshotId);
        }

        if (removedAny)
        {
            CompactScreenshotResourceOrder();
        }
    }

    private bool RemoveScreenshotResource(string screenshotId, string? protectedFilePath = null)
    {
        if (!_screenshotResources.Remove(screenshotId, out var resource))
        {
            return false;
        }

        if (!string.Equals(
            Path.GetFullPath(resource.FilePath),
            protectedFilePath is null ? null : Path.GetFullPath(protectedFilePath),
            StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteScreenshotFile(resource.FilePath);
        }

        return true;
    }

    private static void TryDeleteScreenshotFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
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
    string? Sha256,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset ExpiresAtUtc);
