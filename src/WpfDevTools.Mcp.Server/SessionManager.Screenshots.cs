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

    internal string GetOrCreateScreenshotStorageRoot(int processId) =>
        GetOrCreateScreenshotStorageRootCore(processId, expectedSessionGeneration: null);

    internal string GetOrCreateScreenshotStorageRoot(int processId, long expectedSessionGeneration) =>
        GetOrCreateScreenshotStorageRootCore(processId, expectedSessionGeneration);

    private string GetOrCreateScreenshotStorageRootCore(int processId, long? expectedSessionGeneration)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (expectedSessionGeneration.HasValue &&
                !IsCurrentSessionGenerationLocked(processId, expectedSessionGeneration.Value))
            {
                throw new InvalidOperationException("Screenshot storage root requires the original active session.");
            }

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
        string? sha256) =>
        RegisterScreenshotResourceCore(processId, null, screenshotId, filePath, sha256);

    internal StoredScreenshotResource RegisterScreenshotResource(
        int processId,
        long expectedSessionGeneration,
        string screenshotId,
        string filePath,
        string? sha256) =>
        RegisterScreenshotResourceCore(processId, expectedSessionGeneration, screenshotId, filePath, sha256);

    private StoredScreenshotResource RegisterScreenshotResourceCore(
        int processId,
        long? expectedSessionGeneration,
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
        var storageRoot = expectedSessionGeneration.HasValue
            ? GetExistingScreenshotStorageRootForRegistration(processId, expectedSessionGeneration.Value)
            : GetOrCreateScreenshotStorageRootForRegistration(processId);
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

        ScreenshotResourceReader reader;
        try
        {
            reader = OpenVerifiedScreenshotFile(fullPath, sha256);
        }
        catch
        {
            TryDeleteUnregisteredScreenshotFile(processId, fullPath, storageRoot);
            throw;
        }

        var registeredAtUtc = _utcNowProvider();
        var resource = new StoredScreenshotResource(
            processId,
            screenshotId,
            ScreenshotResourcePrefix + screenshotId,
            fullPath,
            fileName,
            storageRoot,
            reader.Sha256,
            registeredAtUtc,
            registeredAtUtc.Add(ScreenshotResourceRetentionWindow),
            reader);

        try
        {
            lock (_lock)
            {
                if (expectedSessionGeneration.HasValue &&
                    (!IsCurrentSessionGenerationLocked(processId, expectedSessionGeneration.Value) ||
                     !_screenshotStorageRoots.TryGetValue(processId, out var currentRoot) ||
                     !string.Equals(Path.GetFullPath(currentRoot), Path.GetFullPath(storageRoot), StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("Screenshot resource registration requires the original active session.");
                }

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
        }
        catch
        {
            reader.Dispose();
            TryDeleteUnregisteredScreenshotFile(processId, fullPath, storageRoot);
            throw;
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

    internal bool TryDeleteUnregisteredScreenshotFile(
        int processId,
        string filePath,
        string? storageRootOverride = null)
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

        storageRoot ??= storageRootOverride;
        if (string.IsNullOrWhiteSpace(storageRoot))
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

    private bool IsCurrentSessionGenerationLocked(int processId, long sessionGeneration) =>
        _sessionGenerations.TryGetValue(processId, out var currentGeneration)
        && currentGeneration == sessionGeneration;

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

        resource.Reader.Dispose();
        if (!string.Equals(
            Path.GetFullPath(resource.FilePath),
            protectedFilePath is null ? null : Path.GetFullPath(protectedFilePath),
            StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteScreenshotFile(resource);
        }

        TryDeleteUnreferencedScreenshotStorageRoot(resource.StorageRoot);

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

    private string GetExistingScreenshotStorageRootForRegistration(int processId, long expectedSessionGeneration)
    {
        lock (_lock)
        {
            if (!IsCurrentSessionGenerationLocked(processId, expectedSessionGeneration))
            {
                throw new InvalidOperationException("Screenshot resource registration requires the original active session.");
            }

            if (!_screenshotStorageRoots.TryGetValue(processId, out var existingRoot))
            {
                throw new InvalidOperationException("Screenshot storage root is not available for the active session.");
            }

            return existingRoot;
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
    int? ProcessId,
    string ScreenshotId,
    string ResourceUri,
    string FilePath,
    string FileName,
    string StorageRoot,
    string? Sha256,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset ExpiresAtUtc,
    ScreenshotResourceReader Reader);
