using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit.Sdk;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class InstallerPathSafetyTests
{
    private static void RequireWindowsJunctions()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("Directory junction contract is Windows-specific.");
        }
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
        if (process.ExitCode != 0)
        {
            throw SkipException.ForSkip(
                "Directory junction creation failed: " + process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd());
        }
    }

    private static void CreateHardLinkOrSkip(string hardLinkPath, string existingFilePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("Hardlink contract is Windows-specific.");
        }

        try
        {
            if (!CreateHardLink(hardLinkPath, existingFilePath, nint.Zero))
            {
                throw SkipException.ForSkip("Hardlink creation failed with Win32 error " + Marshal.GetLastWin32Error() + ".");
            }
        }
        catch (DllNotFoundException ex)
        {
            throw SkipException.ForSkip("Hardlink creation is unavailable: " + ex.Message);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw SkipException.ForSkip("Hardlink creation is unavailable: " + ex.Message);
        }
    }
}
