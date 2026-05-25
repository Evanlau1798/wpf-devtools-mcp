using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    [Fact]
    public void SandboxProcessCleanup_ShouldSettleScanRootsAfterSlowFirstScanBeforeReturning()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ArtifactPreflight.ps1");
            var probeOutputPath = Path.Combine(tempRoot, "probe-output.txt");
            var command = $$"""
            $ErrorActionPreference = 'Stop'
            try {
                {{GetSmokeTargetFunctionBootstrap(scriptPath)}}
                $script:scanCalls = 0
                $script:stoppedIds = @()
                function Get-DescendantProcessSnapshots {
                    param([int]$ParentProcessId, [long]$CreationCutoffUtcTicks = [long]::MaxValue, [int[]]$VisitedProcessIds = @())
                    if ($ParentProcessId -ne 111) { return @() }
                    $script:scanCalls++
                    if ($script:scanCalls -eq 1) { Start-Sleep -Milliseconds 1200 }
                    if ($script:scanCalls -lt 3) { return @() }
                    return [pscustomobject]@{ ProcessId = 424242; CreationDateUtcTicks = 1; DescendantCutoffUtcTicks = [DateTime]::UtcNow.Ticks }
                }
                function Test-ProcessSnapshotExists {
                    param([object]$Snapshot)
                    return $Snapshot.ProcessId -eq 424242 -and $script:stoppedIds -notcontains 424242
                }
                function Stop-ExistingProcessSnapshots {
                    param([object[]]$Snapshots)
                    foreach ($snapshot in $Snapshots) {
                        if (Test-ProcessSnapshotExists -Snapshot $snapshot) { $script:stoppedIds += [int]$snapshot.ProcessId }
                    }
                }

                $root = [pscustomobject]@{ ProcessId = 111; CreationDateUtcTicks = 1; DescendantCutoffUtcTicks = [DateTime]::UtcNow.Ticks }
                Stop-ProcessSnapshots -Snapshots @() -ScanRoots @($root)
                if ($script:scanCalls -lt 3) { throw "Expected scan root settling, observed $script:scanCalls scan(s)." }
                if ($script:stoppedIds -notcontains 424242) { throw 'Delayed descendant was not stopped.' }
            }
            catch {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', "FAILED: $($_ | Out-String)")
                exit 1
            }

            [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', 'PASS')
            """;
            var probePath = Path.Combine(tempRoot, "probe-scan-root-settle.ps1");
            File.WriteAllText(probePath, command);

            var result = RunPowerShellFileWithoutRedirect(probePath);

            var probeOutput = File.Exists(probeOutputPath) ? File.ReadAllText(probeOutputPath) : "";
            result.ExitCode.Should().Be(0, "PowerShell output: {0}", probeOutput);
        }
        finally
        {
            DeleteTempRootWithRetry(tempRoot);
        }
    }

    [Fact]
    public void SandboxProcessCleanup_ShouldRescanAfterSlowCleanupBeforeSettling()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ArtifactPreflight.ps1");
            var probeOutputPath = Path.Combine(tempRoot, "probe-output.txt");
            var command = $$"""
            $ErrorActionPreference = 'Stop'
            try {
                {{GetSmokeTargetFunctionBootstrap(scriptPath)}}
                $script:scanCalls = 0
                $script:stoppedIds = @()
                function Get-DescendantProcessSnapshots {
                    param([int]$ParentProcessId, [long]$CreationCutoffUtcTicks = [long]::MaxValue, [int[]]$VisitedProcessIds = @())
                    if ($ParentProcessId -ne 111) { return @() }
                    $script:scanCalls++
                    $snapshots = @([pscustomobject]@{ ProcessId = 424242; CreationDateUtcTicks = 1; DescendantCutoffUtcTicks = [DateTime]::UtcNow.Ticks })
                    if ($script:scanCalls -gt 1) { $snapshots += [pscustomobject]@{ ProcessId = 424243; CreationDateUtcTicks = 2; DescendantCutoffUtcTicks = [DateTime]::UtcNow.Ticks } }
                    return $snapshots
                }
                function Stop-ExistingProcessSnapshots {
                    param([object[]]$Snapshots)
                    if ($script:stoppedIds.Count -eq 0) { Start-Sleep -Milliseconds 1200 }
                    foreach ($snapshot in $Snapshots) { $script:stoppedIds += [int]$snapshot.ProcessId }
                }
                function Test-ProcessSnapshotExists {
                    param([object]$Snapshot)
                    return $Snapshot.ProcessId -in @(424242, 424243) -and $script:stoppedIds -notcontains $Snapshot.ProcessId
                }

                $root = [pscustomobject]@{ ProcessId = 111; CreationDateUtcTicks = 1; DescendantCutoffUtcTicks = [DateTime]::UtcNow.Ticks }
                Stop-ProcessSnapshots -Snapshots @() -ScanRoots @($root)
                if ($script:scanCalls -lt 3) { throw "Expected cleanup to rescan after slow stop, observed $script:scanCalls scan(s)." }
                if ($script:stoppedIds -notcontains 424243) { throw 'Post-cleanup descendant was not stopped.' }
            }
            catch {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', "FAILED: $($_ | Out-String)")
                exit 1
            }

            [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', 'PASS')
            """;
            var probePath = Path.Combine(tempRoot, "probe-slow-cleanup-rescan.ps1");
            File.WriteAllText(probePath, command);

            var result = RunPowerShellFileWithoutRedirect(probePath);

            var probeOutput = File.Exists(probeOutputPath) ? File.ReadAllText(probeOutputPath) : "";
            result.ExitCode.Should().Be(0, "PowerShell output: {0}", probeOutput);
        }
        finally
        {
            DeleteTempRootWithRetry(tempRoot);
        }
    }

    [Fact]
    public void SandboxProcessCleanup_ShouldStopOnlyMatchedProcessObjects()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ArtifactPreflight.ps1");
            var probeOutputPath = Path.Combine(tempRoot, "probe-output.txt");
            var command = $$"""
            $ErrorActionPreference = 'Stop'
            try {
                {{GetSmokeTargetFunctionBootstrap(scriptPath)}}
                $script:killed = @()
                function Test-ProcessSnapshotExists {
                    param([object]$Snapshot)
                    return $Snapshot.ProcessId -eq 424242 -and $script:killed -notcontains 424242
                }
                function Get-MatchingProcessFromSnapshot {
                    param([object]$Snapshot)
                    if ($Snapshot.ProcessId -ne 424242) { return $null }
                    $process = [pscustomobject]@{ HasExited = $false; ExitTime = [DateTime]::UtcNow }
                    $process | Add-Member ScriptMethod Kill { $script:killed += 424242; $this.HasExited = $true }
                    $process | Add-Member ScriptMethod WaitForExit { return $true }
                    $process | Add-Member ScriptMethod Dispose { }
                    return $process
                }

                $matchedSnapshot = [pscustomobject]@{
                    ProcessId = 424242
                    CreationDateUtcTicks = 1
                    DescendantCutoffUtcTicks = 1
                }
                Stop-ExistingProcessSnapshots -Snapshots @(
                    $matchedSnapshot,
                    [pscustomobject]@{ ProcessId = 424243; CreationDateUtcTicks = 2 })
                if ($script:killed.Count -ne 1 -or $script:killed[0] -ne 424242) { throw "Unexpected killed IDs: $($script:killed -join ', ')" }
                if ($matchedSnapshot.DescendantCutoffUtcTicks -le 1) { throw 'Matched process cutoff was not refreshed before stopping.' }
            }
            catch {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', "FAILED: $($_ | Out-String)")
                exit 1
            }

            [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', 'PASS')
            """;
            var probePath = Path.Combine(tempRoot, "probe-matched-process-stop.ps1");
            File.WriteAllText(probePath, command);

            var result = RunPowerShellFileWithoutRedirect(probePath);

            var probeOutput = File.Exists(probeOutputPath) ? File.ReadAllText(probeOutputPath) : "";
            result.ExitCode.Should().Be(0, "PowerShell output: {0}", probeOutput);
        }
        finally
        {
            DeleteTempRootWithRetry(tempRoot);
        }
    }

    [Fact]
    public void DocsPagesWorkflow_ShouldBuildDocsWhenPagesIsNotEnabled()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "docs-pages.yml"));

        var buildJob = GetWorkflowJob(workflow, "build");
        var topLevel = workflow[..workflow.IndexOf("\njobs:", StringComparison.Ordinal)];

        buildJob.Should().Contain("pages_enabled: ${{ steps.pages-check.outputs.enabled }}");
        buildJob.Should().Contain("pages: read");
        buildJob.Should().NotContain("pages: write");
        buildJob.Should().NotContain("id-token: write");
        topLevel.Should().NotContain("pages: write");
        topLevel.Should().NotContain("id-token: write");
        workflow.Should().Contain("id: pages-check");
        workflow.Should().Contain("GITHUB_API_URL: ${{ github.api_url }}");
        workflow.Should().Contain("GitHub Pages is not enabled; building docs without deploy.");

        GetWorkflowStep(workflow, "Configure GitHub Pages")
            .Should().Contain("if: steps.pages-check.outputs.enabled == 'true'");
        GetWorkflowStep(workflow, "Upload Pages artifact")
            .Should().Contain("if: steps.pages-check.outputs.enabled == 'true'");
        foreach (var step in new[] { "Restore local tools", "Restore project dependencies", "Build shared assembly for API docs", "Build SDK assembly for API docs", "Build DocFX site" })
        {
            GetWorkflowStep(workflow, step).Should().NotContain("steps.pages-check.outputs.enabled");
        }

        var deployJob = GetWorkflowJob(workflow, "deploy");
        deployJob.Should().Contain("needs: build");
        deployJob.Should().Contain("if: needs.build.outputs.pages_enabled == 'true'");
        deployJob.Should().Contain("pages: write");
        deployJob.Should().Contain("id-token: write");
    }

    [Fact]
    public void DocsWorkflows_ShouldRunDocfxBuildAndValidationInPullRequestCiAndPagesBuilds()
    {
        var ciWorkflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci-cd.yml"));
        var docsWorkflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "docs-pages.yml"));
        var ciTopLevel = ciWorkflow[..ciWorkflow.IndexOf("\njobs:", StringComparison.Ordinal)];

        var ciDocsJob = GetWorkflowJob(ciWorkflow, "docs-validation");
        ciDocsJob.Should().Contain("dotnet tool run docfx docfx/docfx.json");
        ciDocsJob.Should().Contain("scripts/ci/Test-DocFxDocumentation.ps1");
        ciDocsJob.Should().Contain("Validate DocFX links and parity");
        ciTopLevel.Should().Contain("pull_request",
            "DocFX validation should run in the general CI workflow, not only after documentation is merged");

        GetWorkflowStep(docsWorkflow, "Build DocFX site")
            .Should().Contain("dotnet tool run docfx docfx/docfx.json");
        GetWorkflowStep(docsWorkflow, "Validate DocFX links and parity")
            .Should().Contain("scripts/ci/Test-DocFxDocumentation.ps1");
    }

    private static string GetWorkflowStep(string workflow, string name)
    {
        var start = workflow.IndexOf($"      - name: {name}", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"workflow should contain step {name}");
        var next = workflow.IndexOf("\n      - name: ", start + 1, StringComparison.Ordinal);
        return next < 0 ? workflow[start..] : workflow[start..next];
    }

    private static string GetWorkflowJob(string workflow, string name)
    {
        var start = workflow.IndexOf($"  {name}:", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"workflow should contain job {name}");
        var next = workflow.IndexOf("\n  ", start + 1, StringComparison.Ordinal);
        while (next >= 0 && next + 3 < workflow.Length && char.IsWhiteSpace(workflow[next + 3]))
        {
            next = workflow.IndexOf("\n  ", next + 1, StringComparison.Ordinal);
        }

        return next < 0 ? workflow[start..] : workflow[start..next];
    }
}
