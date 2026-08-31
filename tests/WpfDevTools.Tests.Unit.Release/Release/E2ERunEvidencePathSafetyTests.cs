using System.Diagnostics;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class E2ERunEvidencePathSafetyTests
{
    [Fact]
    public void PreJudge_ShouldRejectManifestPathThroughDirectoryLink()
    {
        using var fixture = new E2ERunEvidenceFixture();
        var target = Path.Combine(fixture.Root, "manifest-target");
        Directory.CreateDirectory(target);
        var linkedManifest = Path.Combine(target, "e2e-run-evidence.json");
        File.Copy(fixture.ManifestPath, linkedManifest);
        var link = Path.Combine(fixture.Root, "manifest-link");
        if (!TryCreateDirectoryLink(link, target))
        {
            return;
        }

        var result = Run(fixture, "PreJudge", Path.Combine(link, "e2e-run-evidence.json"), null);

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().ContainEquivalentOf("reparse point");
    }

    [Fact]
    public void Final_ShouldRejectDecisionPathThroughDirectoryLink()
    {
        using var fixture = new E2ERunEvidenceFixture();
        E2ERunEvidenceFixture.Run(fixture, "PreJudge").ExitCode.Should().Be(0);
        var target = Path.Combine(fixture.Root, "decision-target");
        Directory.CreateDirectory(target);
        var link = Path.Combine(fixture.Root, "decision-link");
        if (!TryCreateDirectoryLink(link, target))
        {
            return;
        }

        var decisionPath = Path.Combine(link, "final-decision.json");
        var result = Run(fixture, "Final", fixture.ManifestPath, decisionPath);

        result.ExitCode.Should().NotBe(0);
        File.Exists(Path.Combine(target, "final-decision.json")).Should().BeFalse();
    }

    [Fact]
    public void PreJudge_ShouldRejectReparseEvidenceRoot()
    {
        using var fixture = new E2ERunEvidenceFixture();
        var link = Path.Combine(fixture.Root, "root-link");
        if (!TryCreateDirectoryLink(link, fixture.Root))
        {
            return;
        }

        try
        {
            var result = Run(
                fixture,
                "PreJudge",
                Path.Combine(link, "e2e-run-evidence.json"),
                null,
                link);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().ContainEquivalentOf("reparse point");
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(
        E2ERunEvidenceFixture fixture,
        string phase,
        string manifestPath,
        string? decisionPath,
        string? evidenceRoot = null)
    {
        var arguments = new List<string>
        {
            "-Phase", phase,
            "-EvidenceRoot", evidenceRoot ?? fixture.Root,
            "-ManifestPath", manifestPath
        };
        if (decisionPath is not null)
        {
            arguments.AddRange(["-DecisionPath", decisionPath]);
        }
        return E2ERunEvidenceFixture.RunPwshScript(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/e2e/Test-E2ERunEvidence.ps1"),
            arguments);
    }

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/d /c mklink /J \"{link}\" \"{target}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process!.WaitForExit();
            return process.ExitCode == 0;
        }
    }
}
