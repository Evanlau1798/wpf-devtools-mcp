using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class SandboxCiHostSchedulingContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void InvokeWindowsSandboxCi_ShouldTuneHostSchedulingForHybridCpuSystems()
    {
        var scriptRoot = Path.Combine(RepoRoot, "scripts", "ci");
        var launcher = File.ReadAllText(Path.Combine(scriptRoot, "Invoke-WindowsSandboxCi.ps1"));
        var hostScheduling = File.ReadAllText(Path.Combine(scriptRoot, "SandboxCi.HostScheduling.ps1"));

        launcher.Should().Contain("[string]$SandboxHostPriority = 'AboveNormal'");
        launcher.Should().Contain("[string]$SandboxHostProcessorAffinityHex = ''");
        launcher.Should().Contain("[switch]$SkipSandboxHostScheduling");
        launcher.Should().Contain("SandboxCi.HostScheduling.ps1");
        launcher.Should().Contain("Set-SandboxHostScheduling");
        launcher.Should().Contain("Start-Process -FilePath $sandboxPath");
        launcher.Should().NotContain("taskkill");

        hostScheduling.Should().Contain("SetProcessInformation");
        hostScheduling.Should().Contain("PROCESS_POWER_THROTTLING_EXECUTION_SPEED");
        hostScheduling.Should().Contain("WindowsSandboxClient");
        hostScheduling.Should().Contain("vmwp.exe");
        hostScheduling.Should().Contain("ProcessorAffinity");
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
