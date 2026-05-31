using System.IO;
using System.Threading;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Mcp.Server;

public sealed partial class SessionManager
{
    internal const int RetainedScreenshotResourceLimit = 100;
    internal static readonly TimeSpan ScreenshotResourceRetentionWindow = TimeSpan.FromHours(24);

    private const string ScreenshotResourcePrefix = "wpf://screenshots/";
    private const string ScreenshotFileExtension = ".png";
    private static readonly AsyncLocal<Func<string, bool>?> ScreenshotReparsePointChainDetectorOverrideForTestingState = new();
    private readonly Dictionary<string, StoredScreenshotResource> _screenshotResources = new(StringComparer.Ordinal);
    private readonly Queue<string> _screenshotResourceOrder = new();
    private readonly Dictionary<int, string> _screenshotStorageRoots = new();

    internal static Func<string, bool>? ScreenshotReparsePointChainDetectorOverrideForTesting
    {
        get => ScreenshotReparsePointChainDetectorOverrideForTestingState.Value;
        set => ScreenshotReparsePointChainDetectorOverrideForTestingState.Value = value;
    }

    internal string GetOrCreateScreenshotStorageRoot(int processId)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_screenshotStorageRoots.TryGetValue(processId, out var existingRoot))
            {
                PrepareScreenshotStorageRoot(existingRoot);
                return existingRoot;
            }

            var root = CreateScreenshotStorageRootPath(processId);
            PrepareScreenshotStorageRoot(root);
            _screenshotStorageRoots[processId] = root;
            return root;
        }
    }

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

        var fullPath = ResolveAndValidateScreenshotPath(filePath, "Screenshot file");
        var storageRoot = GetOrCreateScreenshotStorageRootForRegistration(processId);
        if (!IsPathWithinRoot(fullPath, storageRoot))
        {
            throw new ArgumentException(
                "Screenshot file must be under the server-owned screenshot storage root.",
                nameof(filePath));
        }

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
            storageRoot,
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

    internal string ResolveScreenshotResourcePathForRead(StoredScreenshotResource resource)
    {
        ThrowIfDisposed();
        var fullPath = ResolveAndValidateScreenshotPath(resource.FilePath, "Screenshot file");
        if (!IsPathWithinRoot(fullPath, resource.StorageRoot))
        {
            throw new InvalidOperationException("Screenshot file is outside the server-owned screenshot storage root.");
        }

        return fullPath;
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

    internal bool TryDeleteUnregisteredScreenshotFile(int processId, string filePath)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        string? storageRoot;
        lock (_lock)
        {
            _screenshotStorageRoots.TryGetValue(processId, out storageRoot);
        }

        if (string.IsNullOrWhiteSpace(storageRoot))
        {
            return false;
        }

        try
        {
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

        if (_screenshotStorageRoots.Remove(processId, out var storageRoot))
        {
            TryDeleteScreenshotDirectory(storageRoot);
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
            TryDeleteScreenshotFile(resource);
        }

        return true;
    }

    private static void TryDeleteScreenshotFile(StoredScreenshotResource resource)
    {
        try
        {
            if (!TryResolveOwnedScreenshotPath(resource, out var filePath))
            {
                return;
            }

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

    private static void TryDeleteScreenshotDirectory(string storageRoot)
    {
        try
        {
            storageRoot = ResolveAndValidateScreenshotPath(storageRoot, "Screenshot storage directory");
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: false);
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
    }

    private static bool TryResolveOwnedScreenshotPath(
        StoredScreenshotResource resource,
        out string fullPath)
    {
        fullPath = string.Empty;
        try
        {
            fullPath = ResolveAndValidateScreenshotPath(resource.FilePath, "Screenshot file");
            return IsPathWithinRoot(fullPath, resource.StorageRoot);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
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

    private static bool IsValidScreenshotFileName(string fileName) =>
        fileName.EndsWith(ScreenshotFileExtension, StringComparison.OrdinalIgnoreCase)
        && IsValidScreenshotId(Path.GetFileNameWithoutExtension(fileName));

    private static string CreateScreenshotStorageRootPath(int processId)
        => ScreenshotLeasePaths.CreateStorageRootPath(
            Path.GetTempPath(),
            processId,
            Guid.NewGuid().ToString("N"));

    private string GetOrCreateScreenshotStorageRootForRegistration(int processId)
    {
        lock (_lock)
        {
            if (_screenshotStorageRoots.TryGetValue(processId, out var existingRoot))
            {
                return existingRoot;
            }

            var root = CreateScreenshotStorageRootPath(processId);
            PrepareScreenshotStorageRoot(root);
            _screenshotStorageRoots[processId] = root;
            return root;
        }
    }

    private static void PrepareScreenshotStorageRoot(string root)
        => CertificateStorageSecurity.PrepareDirectory(
            root,
            "screenshot storage directory",
            reparsePointDetector: ScreenshotReparsePointChainDetectorOverrideForTesting);

    private static string ResolveAndValidateScreenshotPath(string path, string description)
    {
        var fullPath = CertificateStorageSecurity.ResolveAndValidateLocalPath(
            path,
            nameof(path),
            description);
        EnsureNoScreenshotReparsePoint(fullPath, description);
        return fullPath;
    }

    private static void EnsureNoScreenshotReparsePoint(string fullPath, string description)
    {
        var detector = ScreenshotReparsePointChainDetectorOverrideForTesting
            ?? (path => CertificateStorageSecurity.ContainsReparsePointInPathChain(path));
        if (detector(fullPath))
        {
            throw new InvalidOperationException($"{description} must not traverse symbolic links or reparse points.");
        }
    }

    private static bool IsPathWithinRoot(string fullPath, string rootPath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(fullPath);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record StoredScreenshotResource(
    int ProcessId,
    string ScreenshotId,
    string ResourceUri,
    string FilePath,
    string FileName,
    string StorageRoot,
    string? Sha256,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset ExpiresAtUtc);
