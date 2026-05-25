using FluentAssertions;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class RawInjectionTargetPolicyTests
{
    [Theory]
    [InlineData(@"\\server\share\Target.exe")]
    [InlineData(@"\\?\UNC\server\share\Target.exe")]
    [InlineData(@"\\?\GLOBALROOT\Device\Mup\server\share\Target.exe")]
    [InlineData(@"\\.\GLOBALROOT\Device\Mup\server\share\Target.exe")]
    public void Authorize_WhenRawInjectionTargetPathIsNetworkPath_ShouldFailClosed(string targetPath)
    {
        var authorization = RawInjectionTargetPolicy.Authorize(
            CreateProcessInfo(targetPath),
            AppContext.BaseDirectory,
            configuredAllowedTargets: null,
            tryResolvePhysicalPath: path => path);

        authorization.IsAllowed.Should().BeFalse();
        authorization.Error.Should().Contain("local absolute path");
    }

    [Theory]
    [InlineData(@"\\server\share\Target.exe")]
    [InlineData(@"\\?\UNC\server\share\Target.exe")]
    [InlineData(@"\\?\GLOBALROOT\Device\Mup\server\share\Target.exe")]
    [InlineData(@"\\.\GLOBALROOT\Device\Mup\server\share\Target.exe")]
    public void Authorize_WhenRawInjectionAllowlistContainsNetworkPath_ShouldFailClosed(string configuredAllowedTarget)
    {
        var authorization = RawInjectionTargetPolicy.Authorize(
            CreateProcessInfo(@"C:\Allowed\Target.exe"),
            AppContext.BaseDirectory,
            configuredAllowedTargets: configuredAllowedTarget,
            tryResolvePhysicalPath: path => path);

        authorization.IsAllowed.Should().BeFalse();
        authorization.Error.Should().Contain("Invalid raw injection allowlist configuration");
        authorization.Error.Should().Contain("exact local absolute executable path");
    }

    [Fact]
    public void Authorize_WhenRawInjectionTargetUsesUnclassifiedDrive_ShouldFailClosed()
    {
        var targetPath = Path.Combine(GetUnusedDriveRoot(), "Target.exe");

        var authorization = RawInjectionTargetPolicy.Authorize(
            CreateProcessInfo(targetPath),
            AppContext.BaseDirectory,
            configuredAllowedTargets: targetPath,
            tryResolvePhysicalPath: path => path);

        authorization.IsAllowed.Should().BeFalse();
        authorization.Error.Should().Contain("local absolute path");
    }

    [Fact]
    public void Authorize_WhenResolvedTargetPathIsNetworkPath_ShouldFailClosed()
    {
        var authorization = RawInjectionTargetPolicy.Authorize(
            CreateProcessInfo(@"C:\Allowed\Target.exe"),
            AppContext.BaseDirectory,
            configuredAllowedTargets: @"C:\Allowed\Target.exe",
            tryResolvePhysicalPath: _ => @"\\server\share\Target.exe");

        authorization.IsAllowed.Should().BeFalse();
        authorization.Error.Should().Contain("local absolute path");
    }

    [Theory]
    [InlineData(@"\\?\GLOBALROOT\Device\Mup\server\share\Target.exe")]
    [InlineData(@"\\.\GLOBALROOT\Device\Mup\server\share\Target.exe")]
    public void TryNormalizeFinalPathName_WhenPathUsesDeviceNamespace_ShouldFailClosed(string finalPathName)
    {
        var normalized = RawInjectionTargetPolicy.TryNormalizeFinalPathName(
            finalPathName,
            out _);

        normalized.Should().BeFalse();
    }

    [Fact]
    public void TryNormalizeAbsolutePath_WhenPhysicalResolverRejectsPath_ShouldFailClosed()
    {
        var normalized = RawInjectionTargetPolicy.TryNormalizeAbsolutePath(
            @"C:\Allowed\Target.exe",
            _ => PhysicalPathResolution.Rejected(),
            out _);

        normalized.Should().BeFalse();
    }

    private static WpfProcessInfo CreateProcessInfo(string executablePath)
    {
        return new WpfProcessInfo
        {
            ProcessId = 12345,
            ProcessName = "TargetApp",
            WindowTitle = "TargetApp",
            Architecture = ProcessArchitecture.X64,
            Runtime = TargetRuntime.NetCore,
            IsWpfApplication = true,
            ExecutablePath = executablePath
        };
    }

    private static string GetUnusedDriveRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Path.GetPathRoot(Path.GetFullPath("Target.exe")) ?? "/";
        }

        var usedRoots = DriveInfo.GetDrives()
            .Select(drive => drive.Name[..2])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var driveLetter = 'Z'; driveLetter >= 'D'; driveLetter--)
        {
            var root = driveLetter + @":\";
            if (!usedRoots.Contains(root[..2]))
            {
                return root;
            }
        }

        return @"A:\";
    }
}
