using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class InstallerPathSafetyTests
{
    private static void RequireWindowsJunctions()
    {
        OperatingSystem.IsWindows().Should().BeTrue(
            "release installer path-safety tests require a Windows runner with junction support");
    }

    private static void CreateDirectoryJunctionOrSkip(string junctionPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c mklink /J \"" + junctionPath + "\" \"" + targetPath + "\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        process.Should().NotBeNull();
        process!.WaitForExit(5000).Should().BeTrue("mklink should complete promptly");
        process.ExitCode.Should().Be(0,
            "directory junction creation must be available for release path-safety verification. stderr/stdout: {0}",
            process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd());
    }

    private static void CreateHardLinkOrSkip(string hardLinkPath, string existingFilePath)
    {
        OperatingSystem.IsWindows().Should().BeTrue(
            "release installer path-safety tests require a Windows runner with hardlink support");

        try
        {
            CreateHardLink(hardLinkPath, existingFilePath, nint.Zero)
                .Should().BeTrue("hardlink creation must be available; Win32 error {0}", Marshal.GetLastWin32Error());
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException("Hardlink creation is unavailable.", ex);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new InvalidOperationException("Hardlink creation is unavailable.", ex);
        }
    }
}
