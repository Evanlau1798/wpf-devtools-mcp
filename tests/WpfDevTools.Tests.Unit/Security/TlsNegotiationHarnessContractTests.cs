using System.Xml.Linq;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Security;

public sealed class TlsNegotiationHarnessContractTests
{
    [Fact]
    public void TlsNegotiationScript_ShouldCoverRequiredRuntimePairs()
    {
        var script = File.ReadAllText(GetRepoPath("scripts/tests/Test-TlsNegotiation.ps1"));

        script.Should().Contain("net8-net8");
        script.Should().Contain("net8-net48");
        script.Should().Contain("net48-net8");
        script.Should().Contain("Tls12");
    }

    [Fact]
    public void TlsNegotiationScript_ShouldPreserveArgumentBoundariesOnWindowsPowerShell()
    {
        var script = File.ReadAllText(GetRepoPath("scripts/tests/Test-TlsNegotiation.ps1"));

        script.Should().Contain("ConvertTo-WindowsCommandLineArgument");
        script.Should().Contain("Start-HarnessProcess");
        script.Should().Contain("ConvertTo-WindowsCommandLine");
    }

    [Fact]
    public void TlsNegotiationScript_ShouldRequireOutputRootToStayUnderTmpDirectoryBoundary()
    {
        var script = File.ReadAllText(GetRepoPath("scripts/tests/Test-TlsNegotiation.ps1"));

        script.Should().Contain("tmpRootWithSeparator");
        script.Should().Contain("outputRootFullPath.Equals($tmpRoot");
    }

    [Fact]
    public void TlsNegotiationScript_ShouldPassConnectTimeoutToHarness()
    {
        var script = File.ReadAllText(GetRepoPath("scripts/tests/Test-TlsNegotiation.ps1"));
        var program = File.ReadAllText(GetRepoPath(
            "tests/WpfDevTools.Tests.TlsNegotiationHarness/Program.cs"));

        script.Should().Contain("--connect-timeout-seconds");
        program.Should().Contain("ConnectTimeout");
    }

    [Fact]
    public void TlsNegotiationHarnessProject_ShouldMultiTargetNet8AndNet48()
    {
        var project = XDocument.Load(GetRepoPath(
            "tests/WpfDevTools.Tests.TlsNegotiationHarness/WpfDevTools.Tests.TlsNegotiationHarness.csproj"));
        var targetFrameworks = project.Descendants("TargetFrameworks")
            .Single()
            .Value;

        targetFrameworks.Should().Be("net8.0;net48");
    }

    [Fact]
    public void Solution_ShouldBuildTlsNegotiationHarnessProject()
    {
        var solution = File.ReadAllText(GetRepoPath("WpfDevTools.sln"));

        solution.Should().Contain(
            "tests\\WpfDevTools.Tests.TlsNegotiationHarness\\WpfDevTools.Tests.TlsNegotiationHarness.csproj");
    }

    [Fact]
    public void TlsNegotiationHarness_ShouldUseSharedInspectorTransportPolicy()
    {
        var program = File.ReadAllText(GetRepoPath(
            "tests/WpfDevTools.Tests.TlsNegotiationHarness/Program.cs"));

        program.Should().Contain("SecureTransportProtocols.InspectorTransport");
        program.Should().Contain("CertificateManager");
        program.Should().Contain("SslProtocol");
    }

    private static string GetRepoPath(string relativePath)
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        return Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WpfDevTools.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
