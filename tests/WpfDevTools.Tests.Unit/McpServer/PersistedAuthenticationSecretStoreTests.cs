using System.Security.Cryptography;
using System.Security.AccessControl;
using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class PersistedAuthenticationSecretStoreTests
{
    [Fact]
    public async Task GetOrCreateSecretBase64_WhenCalledConcurrently_ShouldReturnSameSecret()
    {
        var secretFilePath = CreateSecretFilePath();
        var store = new PersistedAuthenticationSecretStore(secretFilePath);

        try
        {
            var results = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => Task.Run(store.GetOrCreateSecretBase64)));

            results.Should().OnlyContain(result => result == results[0]);
            Convert.FromBase64String(results[0]).Should().HaveCount(32);
        }
        finally
        {
            DeleteSecretArtifacts(secretFilePath);
        }
    }

    [Fact]
    public void GetOrCreateSecretBase64_WithCorruptBlob_ShouldQuarantineAndRegenerate()
    {
        var secretFilePath = CreateSecretFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(secretFilePath)!);
        File.WriteAllBytes(secretFilePath, RandomNumberGenerator.GetBytes(19));
        var store = new PersistedAuthenticationSecretStore(secretFilePath);

        try
        {
            var secretBase64 = store.GetOrCreateSecretBase64();

            Convert.FromBase64String(secretBase64).Should().HaveCount(32);
            Directory.GetFiles(
                    Path.GetDirectoryName(secretFilePath)!,
                    Path.GetFileName(secretFilePath) + ".corrupt-*")
                .Should().ContainSingle();
        }
        finally
        {
            DeleteSecretArtifacts(secretFilePath);
        }
    }

    [Fact]
    public void GetOrCreateSecretBase64_WithDecryptableInvalidLengthBlob_ShouldQuarantineAndRegenerate()
    {
        var secretFilePath = CreateSecretFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(secretFilePath)!);
        var invalidSecret = new byte[] { 0x42 };
        var protectedBytes = ProtectedData.Protect(invalidSecret, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(secretFilePath, protectedBytes);
        var store = new PersistedAuthenticationSecretStore(secretFilePath);

        try
        {
            var secretBase64 = store.GetOrCreateSecretBase64();

            Convert.FromBase64String(secretBase64).Should().HaveCount(32);
            Directory.GetFiles(
                    Path.GetDirectoryName(secretFilePath)!,
                    Path.GetFileName(secretFilePath) + ".corrupt-*")
                .Should().ContainSingle();
        }
        finally
        {
            DeleteSecretArtifacts(secretFilePath);
        }
    }

    [Fact]
    public void GetOrCreateSecretBase64_WhenSecretFileIsLocked_ShouldPreserveExistingSecret()
    {
        var secretFilePath = CreateSecretFilePath();
        var expectedSecretBase64 = WriteProtectedSecret(secretFilePath, RandomNumberGenerator.GetBytes(32));
        var store = new PersistedAuthenticationSecretStore(secretFilePath);

        try
        {
            using (new FileStream(secretFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var act = () => store.GetOrCreateSecretBase64();

                act.Should().Throw<IOException>();
            }

            store.GetOrCreateSecretBase64().Should().Be(expectedSecretBase64);
            Directory.GetFiles(
                    Path.GetDirectoryName(secretFilePath)!,
                    Path.GetFileName(secretFilePath) + ".corrupt-*")
                .Should().BeEmpty();
        }
        finally
        {
            DeleteSecretArtifacts(secretFilePath);
        }
    }

    [Fact]
    public void GetOrCreateSecretBase64_WithAbandonedMutex_ShouldRecoverAndReturnSecret()
    {
        var secretFilePath = CreateSecretFilePath();
        var store = new PersistedAuthenticationSecretStore(secretFilePath);

        var thread = new Thread(() =>
        {
            using var mutex = new Mutex(false, store.MutexName);
            mutex.WaitOne();
        });

        thread.Start();
        thread.Join();

        try
        {
            var secretBase64 = store.GetOrCreateSecretBase64();

            Convert.FromBase64String(secretBase64).Should().HaveCount(32);
        }
        finally
        {
            DeleteSecretArtifacts(secretFilePath);
        }
    }

    [Fact]
    public void Constructor_WithRelativePath_ShouldThrowClearError()
    {
        var act = () => new PersistedAuthenticationSecretStore(Path.Combine("relative", "secret.bin"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must be absolute*");
    }

    [Fact]
    public void Constructor_WithDriveRelativePath_ShouldThrowClearError()
    {
        var driveRelativePath = GetDriveRelativePath("secret.bin");

        var act = () => new PersistedAuthenticationSecretStore(driveRelativePath);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must be absolute*");
    }

    [Fact]
    public void Constructor_WithUncPath_ShouldThrowLocalPathError()
    {
        var act = () => new PersistedAuthenticationSecretStore(@"\\server\share\wpf-devtools\shared-secret.bin");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*local path*");
    }

    [Fact]
    public void GetOrCreateSecretBase64_WhenSecretIsCreated_ShouldProtectDirectoryAndFileAcl()
    {
        var secretFilePath = CreateSecretFilePath();
        var store = new PersistedAuthenticationSecretStore(secretFilePath);

        try
        {
            store.GetOrCreateSecretBase64();

            var directorySecurity = new DirectoryInfo(Path.GetDirectoryName(secretFilePath)!).GetAccessControl();
            var fileSecurity = new FileInfo(secretFilePath).GetAccessControl();

            directorySecurity.AreAccessRulesProtected.Should().BeTrue();
            fileSecurity.AreAccessRulesProtected.Should().BeTrue();
        }
        finally
        {
            DeleteSecretArtifacts(secretFilePath);
        }
    }

    [Fact]
    public void GetOrCreateSecretBase64_WhenMutexTimesOut_ShouldThrowTimeoutException()
    {
        var secretFilePath = CreateSecretFilePath();
        var store = new PersistedAuthenticationSecretStore(secretFilePath, TimeSpan.FromMilliseconds(10));
        using var mutexAcquired = new ManualResetEventSlim(false);
        using var releaseMutex = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            using var mutex = new Mutex(false, store.MutexName);
            mutex.WaitOne();
            mutexAcquired.Set();
            releaseMutex.Wait();
            mutex.ReleaseMutex();
        });

        thread.Start();
        mutexAcquired.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();

        try
        {
            var act = () => store.GetOrCreateSecretBase64();

            act.Should().Throw<TimeoutException>();
        }
        finally
        {
            releaseMutex.Set();
            thread.Join();
            DeleteSecretArtifacts(secretFilePath);
        }
    }

    private static string WriteProtectedSecret(string secretFilePath, byte[] secretBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(secretFilePath)!);
        var protectedBytes = ProtectedData.Protect(secretBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(secretFilePath, protectedBytes);
        return Convert.ToBase64String(secretBytes);
    }

    private static string CreateSecretFilePath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "wpf-devtools-auth-tests",
            Guid.NewGuid().ToString("N"),
            "shared-secret.bin");
    }

    private static string GetDriveRelativePath(string fileName)
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        return root[..2] + fileName;
    }

    private static void DeleteSecretArtifacts(string secretFilePath)
    {
        if (File.Exists(secretFilePath))
        {
            File.Delete(secretFilePath);
        }

        var directory = Path.GetDirectoryName(secretFilePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var artifact in Directory.GetFiles(directory, Path.GetFileName(secretFilePath) + ".*") )
        {
            File.Delete(artifact);
        }

        var parent = Directory.GetParent(directory);
        if (parent?.Name == "wpf-devtools-auth-tests")
        {
            Directory.Delete(directory, recursive: true);
            if (!Directory.EnumerateFileSystemEntries(parent.FullName).Any())
            {
                Directory.Delete(parent.FullName);
            }
        }
    }
}
