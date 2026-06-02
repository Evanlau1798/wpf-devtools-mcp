using FluentAssertions;
using System.Diagnostics;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class HostedCiTestEntryPointContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Theory]
    [InlineData(
        "Invoke-HostedWindowsX64FastCi.ps1",
        "HostedWindowsX64Fast",
        "tmp\\hosted-ci-fast")]
    [InlineData(
        "Invoke-HostedWindowsX64Ci.ps1",
        "HostedWindowsX64",
        "tmp\\hosted-ci")]
    public void ScriptsTests_ShouldExposeNoVmHostedCiEntryPoints(
        string scriptName,
        string mode,
        string defaultWorkRoot)
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "tests", scriptName));

        script.Should().Contain("scripts\\ci\\Invoke-HostedCi.ps1");
        script.Should().Contain($"-Mode {mode}");
        script.Should().Contain(defaultWorkRoot);
        script.Should().Contain("[int]$MaxParallelLanes = 8");
        script.Should().Contain("[int]$ReleaseUnitShardCount = 8");
        script.Should().Contain("[int]$UnitDebugShardCount = 1");
        script.Should().NotContain("WindowsSandbox.exe");
        script.Should().NotContain(".wsb");
        script.Should().NotContain("Invoke-WindowsSandboxCi.ps1");
    }

    [Fact]
    public void ScriptsTests_ShouldParseAsPowerShell()
    {
        var command = """
        $ErrorActionPreference = 'Stop'
        $scriptRoot = Join-Path $PWD 'scripts\tests'
        foreach ($script in Get-ChildItem -LiteralPath $scriptRoot -Filter '*.ps1') {
            $tokens = $null
            $errors = $null
            [System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors) | Out-Null
            if ($errors.Count -gt 0) {
                throw "PowerShell parser errors in $($script.Name): $($errors[0].Message)"
            }
        }
        """;

        var result = RunPowerShell(command);

        result.ExitCode.Should().Be(0, result.Output);
    }

    [Fact]
    public void DocfxTestingGuides_ShouldDescribeNoVmHostedCiEntryPoints()
    {
        var english = File.ReadAllText(Path.Combine(RepoRoot, "docfx", "contributors", "testing-and-tdd.md"));
        var zhTw = File.ReadAllText(Path.Combine(RepoRoot, "docfx", "zh-tw", "contributors", "testing-and-tdd.md"));

        foreach (var document in new[] { english, zhTw })
        {
            document.Should().Contain("Invoke-HostedWindowsX64FastCi.ps1");
            document.Should().Contain("Invoke-HostedWindowsX64Ci.ps1");
            document.Should().Contain("HostedWindowsX64Fast");
            document.Should().Contain("HostedWindowsX64");
            document.Should().Contain("tmp/hosted-ci");
            document.Should().Contain("Windows Sandbox");
            document.Should().Contain("GitHub");
        }
    }

    private static (int ExitCode, string Output) RunPowerShell(string command)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = RepoRoot,
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(command);

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30000).Should().BeTrue("PowerShell parser verification should be bounded");

        return (process.ExitCode, stdout + stderr);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
