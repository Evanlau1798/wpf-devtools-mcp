using FluentAssertions;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void SandboxCiScripts_ShouldExposeReusableWindowsSandboxWorkflow()
    {
        var scriptRoot = Path.Combine(RepoRoot, "scripts", "ci");
        var launcher = ReadScript(scriptRoot, "Invoke-WindowsSandboxCi.ps1");
        var runner = ReadScript(scriptRoot, "Start-SandboxCi.ps1");
        var hostScheduling = ReadScript(scriptRoot, "SandboxCi.HostScheduling.ps1");
        var process = ReadScript(scriptRoot, "SandboxCi.Process.ps1");
        var native = ReadScript(scriptRoot, "SandboxCi.Native.ps1");
        var managed = ReadScript(scriptRoot, "SandboxCi.Managed.ps1");
        var hosted = ReadScript(scriptRoot, "SandboxCi.Hosted.ps1");
        var cleanup = ReadScript(scriptRoot, "Stop-WindowsSandboxHcs.ps1");

        launcher.Should().Contain("WindowsSandbox.exe");
        launcher.Should().Contain("WaitTimeoutSeconds");
        launcher.Should().Contain("last-result.txt");
        launcher.Should().Contain("-RunId $runId");
        launcher.Should().Contain("SandboxCi.HostScheduling.ps1");

        hostScheduling.Should().Contain("Set-SandboxHostScheduling");
        hostScheduling.Should().Contain("SetProcessInformation");

        runner.Should().Contain("SandboxCi.Native.ps1");
        runner.Should().Contain("SandboxCi.Managed.ps1");
        runner.Should().Contain("NativeFull");
        runner.Should().Contain("NativeSmoke");
        runner.Should().Contain("HostedWindowsX64");
        runner.Should().Contain("-SkipDllLink");
        runner.Should().Contain("WPFDEVTOOLS_TEST_TIMEOUT_SCALE");
        runner.Should().Contain("PASS $RunId");
        runner.Should().Contain("FAIL $RunId");

        process.Should().Contain("RedirectStandardOutput");
        process.Should().Contain("KillProcessTree");
        process.Should().Contain("command.log");
        process.Should().Contain("did not exit within 30 seconds after cleanup");
        process.Should().Contain("redirected output streams did not close within 30 seconds");

        native.Should().Contain("Invoke-NativeFullVerification");
        native.Should().Contain("Invoke-NativeBootstrapperBuild");

        managed.Should().Contain("Install-DotNetSdk");
        managed.Should().Contain("Invoke-FocusedFlakeTests");
        managed.Should().Contain("Invoke-ReleaseUnitTests");

        hosted.Should().Contain("Invoke-HostedWindowsX64Verification");
        hosted.Should().Contain("Invoke-HostedNativeBootstrapperBuild");
        hosted.Should().Contain("WpfDevTools.Bootstrapper.vcxproj");

        cleanup.Should().Contain("hcsdiag.exe");
        cleanup.Should().Contain("WindowsSandbox");
        cleanup.Should().Contain("SupportsShouldProcess");
        cleanup.Should().Contain("ConfirmImpact = 'High'");
        cleanup.Should().Contain("$PSBoundParameters.ContainsKey('Confirm')");
        cleanup.Should().Contain("$ConfirmPreference = 'None'");
        cleanup.Should().Contain("LASTEXITCODE");
        cleanup.Should().Contain("ValidateScript");
        cleanup.Should().Contain("Wait-WindowsSandboxShutdown");
        cleanup.Should().Contain("Wait-WindowsSandboxProcessesExit");
        cleanup.Should().Contain("IsNullOrWhiteSpace($_)");
        cleanup.Should().NotContain("taskkill");
        cleanup.Should().NotContain(RepoRoot);
    }

    [Fact]
    public void SandboxCiScripts_ShouldParseAsPowerShell()
    {
        var command = @"
$ErrorActionPreference = 'Stop'
$scriptRoot = Join-Path $PWD 'scripts\ci'
foreach ($script in Get-ChildItem -LiteralPath $scriptRoot -Filter '*.ps1') {
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -gt 0) {
        throw ""PowerShell parser errors in $($script.Name): $($errors[0].Message)""
    }
}
";

        RunPowerShell(command).ExitCode.Should().Be(0);
    }

    [Fact]
    public void InvokeWindowsSandboxCi_GenerateOnly_ShouldWriteValidSandboxConfigToWorkRoot()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workRoot = Path.Combine(tempRoot, "work root");
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "Invoke-WindowsSandboxCi.ps1");
            var result = RunPowerShellFile(scriptPath, "-Mode", "FocusedFlakes", "-Repeat", "1", "-WorkRoot", workRoot, "-GenerateOnly");

            result.ExitCode.Should().Be(0, result.Output);
            var configPath = Directory.GetFiles(workRoot, "WpfDevTools-LocalCi-*.wsb").Should().ContainSingle().Subject;
            var document = XDocument.Load(configPath);

            document.Root.Should().NotBeNull();
            document.Descendants("MappedFolder").Should().HaveCountGreaterThanOrEqualTo(3);
            document.Descendants("SandboxFolder").Select(element => element.Value)
                .Should().Contain(new[] { @"C:\r", @"C:\w", @"C:\o" });
            document.Descendants("Command").Single().Value.Should().Contain("Start-SandboxCi.ps1");
            document.Descendants("Command").Single().Value.Should().Contain(@"-MappedWorkRoot ""C:\w""");
            File.Exists(Path.Combine(workRoot, "work", "git-tracked-files.txt")).Should().BeTrue();
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void SandboxProcessRunner_ShouldPreserveSpecialCharactersInArguments()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var capturePath = Path.Combine(tempRoot, "captured-args.txt");
            var echoScriptPath = Path.Combine(tempRoot, "echo-args.ps1");
            File.WriteAllText(
                echoScriptPath,
                """
                [System.IO.File]::WriteAllLines($env:ARG_CAPTURE_PATH, $args, [System.Text.Encoding]::UTF8)
                """);

            var processScript = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Process.ps1");
            var command = $"""
            $ErrorActionPreference = 'Stop'
            . '{EscapePowerShellPath(processScript)}'
            $env:ARG_CAPTURE_PATH = '{EscapePowerShellPath(capturePath)}'
            Invoke-ExternalWithTimeout 'argument roundtrip' 'powershell.exe' @(
                '-NoProfile',
                '-ExecutionPolicy',
                'Bypass',
                '-File',
                '{EscapePowerShellPath(echoScriptPath)}',
                '%PATH%',
                'caret^value',
                'pipe|value',
                'amp&value',
                'space value'
            ) -TimeoutSeconds 30 -OutputRoot '{EscapePowerShellPath(tempRoot)}' -Timestamp 'roundtrip'
            """;

            var result = RunPowerShell(command);

            result.ExitCode.Should().Be(0, result.Output);
            File.ReadAllLines(capturePath).Should().Equal("%PATH%", "caret^value", "pipe|value", "amp&value", "space value");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void StartSandboxCi_ShouldExcludeNestedWorktreesFromRepositoryMirror()
    {
        var runner = ReadScript(Path.Combine(RepoRoot, "scripts", "ci"), "Start-SandboxCi.ps1");

        runner.Should().Contain("(Join-Path $SourceRoot '.worktrees')");
        runner.Should().Contain("(Join-Path $SourceRoot 'docs')");
        runner.Should().Contain("(Join-Path $SourceRoot 'plan')");
        runner.Should().Contain("(Join-Path $SourceRoot 'todo')");
        runner.Should().Contain("(Join-Path $SourceRoot '.claude')");
        runner.Should().Contain("(Join-Path $SourceRoot 'coverage')");
        runner.Should().Contain("(Join-Path $SourceRoot 'secrets')");
        runner.Should().Contain("'*.log'");
        runner.Should().Contain("'coverage-report.md'");
        runner.Should().Contain("Clear-DirectoryContents");
        runner.Should().Contain("Assert-SandboxWorkDestination");
        runner.Should().Contain("DestinationRoot must be inside the sandbox work root");
        runner.Should().Contain("DestinationRoot must not be a drive root");
        runner.Should().Contain("DestinationRoot must not equal SourceRoot");
        runner.Should().NotContain("Remove-Item -LiteralPath $DestinationRoot -Recurse -Force");
    }

    [Fact]
    public void StartSandboxCi_ShouldBuildRepositoryFromSandboxLocalDisk()
    {
        var runner = ReadScript(Path.Combine(RepoRoot, "scripts", "ci"), "Start-SandboxCi.ps1");

        runner.Should().Contain("$sandboxLocalWorkRoot = Join-Path $env:SystemDrive 'sandbox-ci-work'");
        runner.Should().Contain("$sandboxRepoWorkRoot = Join-Path $sandboxLocalWorkRoot 'repo'");
        runner.Should().Contain("-WorkRoot $sandboxLocalWorkRoot");
        runner.Should().NotContain("$sandboxRepoWorkRoot = Join-Path $MappedWorkRoot 'repo'",
            "building from a Windows Sandbox mapped folder is less GitHub-like and can break native linker temporary resource processing");
    }

    [Fact]
    public void StartSandboxCi_ShouldUseNativeSmokeForSandboxLinkerSafeMode()
    {
        var runner = ReadScript(Path.Combine(RepoRoot, "scripts", "ci"), "Start-SandboxCi.ps1");

        runner.Should().Contain("'NativeSmoke' {");
        runner.Should().Contain("Invoke-NativeFullVerification -DotNetPath $dotnetPath -OutputRoot $MappedOutputRoot -Timestamp $timestamp -SkipDllLink");
        runner.Should().Contain("'NativeFull' {");
        var nativeFullBlock = runner.Substring(runner.IndexOf("'NativeFull' {", StringComparison.Ordinal));
        nativeFullBlock.Should().Contain("Invoke-NativeFullVerification -DotNetPath $dotnetPath -OutputRoot $MappedOutputRoot -Timestamp $timestamp");
        nativeFullBlock.Should().NotContain("-SkipDllLink");
    }

    [Fact]
    public void StartSandboxCi_ShouldValidateRunIdBeforeUsingResultTempFile()
    {
        var runner = ReadScript(Path.Combine(RepoRoot, "scripts", "ci"), "Start-SandboxCi.ps1");

        runner.Should().Contain("[ValidatePattern('^[A-Za-z0-9_.-]+$')]");
        runner.Should().Contain("\"last-result.{0}.tmp\" -f $RunId");
    }

    [Fact]
    public void StartSandboxCi_ShouldDisableAutocrlfBeforeIndexingEphemeralRepository()
    {
        var runner = ReadScript(Path.Combine(RepoRoot, "scripts", "ci"), "Start-SandboxCi.ps1");

        var autocrlfIndex = runner.IndexOf("'Configure ephemeral git autocrlf'", StringComparison.Ordinal);
        var addIndex = runner.IndexOf("'Index copied sandbox repository files'", StringComparison.Ordinal);

        autocrlfIndex.Should().BeGreaterThan(0);
        autocrlfIndex.Should().BeLessThan(addIndex,
            "sandbox ephemeral Git indexing must not inherit host autocrlf settings that emit stderr warnings");
        runner.Should().Contain("'core.autocrlf'");
        runner.Should().Contain("'false'");
    }

    [Fact]
    public void SandboxCiScripts_ShouldStayUnderSingleFileLineLimit()
    {
        var scriptRoot = Path.Combine(RepoRoot, "scripts", "ci");
        var scriptPaths = Directory.GetFiles(scriptRoot, "*.ps1");

        scriptPaths.Should().NotBeEmpty();
        foreach (var scriptPath in scriptPaths)
        {
            var lineCount = File.ReadAllText(scriptPath).Split('\n').Length;
            lineCount.Should().BeLessThanOrEqualTo(500, $"{Path.GetFileName(scriptPath)} should stay maintainable");
        }
    }

    [Fact]
    public void SandboxNativeScript_ShouldSupportExplicitLinkToolOverride()
    {
        var native = ReadScript(Path.Combine(RepoRoot, "scripts", "ci"), "SandboxCi.Native.ps1");

        native.Should().Contain("Resolve-VCToolsDirectory");
        native.Should().Contain("Resolve-LinkToolOverrideDirectory");
        native.Should().Contain("Resolve-CompatibleLinkToolDirectory");
        native.Should().Contain("WPFDEVTOOLS_SANDBOX_LINK_TOOL_VERSION");
        native.Should().Contain("VCToolsVersion");
        native.Should().Contain("Invoke-NativeBootstrapperBuild");
        native.Should().Contain("Assert-NativeBuildEnvironment");
        native.Should().Contain("link.exe");
        native.Should().Contain("lib.exe");
        native.Should().Contain("cvtres.exe");
        native.Should().Contain("bootstrapper.res.obj");
        native.Should().Contain("Archive native bootstrapper smoke library x64");
        native.Should().Contain("LIBCMT.lib");
        native.Should().Contain("NetFxSdkLibraryDir was not found");
        native.Should().Contain("Remove-Item Env:LINK, Env:_LINK_ -ErrorAction SilentlyContinue");
        native.Should().NotContain("$env:LINK = ''");
        native.Should().NotContain("$env:_LINK_ = ''");
        native.Should().Contain("ProcessResFiles");
        native.Should().Contain("$nativeObjectPaths");
        native.Should().Contain("\"/DEF:$(Join-Path $projectRoot 'exports.def')");
        native.Should().Contain("\"/IMPLIB:$(Join-Path $outputDirectory 'WpfDevTools.Bootstrapper.x64.lib')");
        native.Should().NotContain("'/NOIMPLIB'");
        native.Should().Contain("[switch]$SkipDllLink");
        native.Should().NotContain("WPFDEVTOOLS_SANDBOX_REQUIRE_NATIVE_DLL_LINK");
        native.Should().Contain("link.rsp");
        native.Should().Contain("ConvertTo-ProcessArgument -Argument");
        native.Should().NotContain("WpfDevTools.Bootstrapper.res");
        native.Should().NotContain("link-placeholder.rc");
        native.Should().NotContain("'.res'");
        native.Should().NotContain("WriteAllText($linkPlaceholderScriptPath, '',");
        native.IndexOf("Assert-NativeBuildEnvironment").Should()
            .BeLessThan(native.IndexOf("Invoke-NativeBootstrapperBuild -OutputRoot"));
        native.IndexOf("$resourceObjectPath =").Should()
            .BeLessThan(native.IndexOf("Invoke-ExternalWithTimeout 'Link native bootstrapper Debug x64'"));
        native.IndexOf("$nativeObjectPaths").Should()
            .BeLessThan(native.IndexOf("Invoke-ExternalWithTimeout 'Link native bootstrapper Debug x64'"));
        native.Should().Contain("$repoRoot = $PWD.ProviderPath");
        native.Should().NotContain("[System.IO.Path]::GetFullPath('src\\WpfDevTools.Bootstrapper')");
        native.Should().NotContain("'/TLBID:1'");
        native.Should().NotContain("$linkToolDirectory;$env:PATH");
    }

    [Fact]
    public void BootstrapperProject_ShouldDeclareNativeResourceFileForSandboxLinking()
    {
        var project = File.ReadAllText(Path.Combine(RepoRoot, "src", "WpfDevTools.Bootstrapper", "WpfDevTools.Bootstrapper.vcxproj"));
        var resource = File.ReadAllText(Path.Combine(RepoRoot, "src", "WpfDevTools.Bootstrapper", "bootstrapper.rc"));

        project.Should().Contain("""<ResourceCompile Include="bootstrapper.rc" />""");
        project.Should().Contain("ProgramDatabaseFile");
        resource.Should().Contain("VERSIONINFO");
        resource.Should().Contain("WpfDevTools.Bootstrapper");
    }

    private static string ReadScript(string scriptRoot, string fileName)
    {
        var path = Path.Combine(scriptRoot, fileName);
        File.Exists(path).Should().BeTrue($"{fileName} should be part of the reusable sandbox CI workflow");
        return File.ReadAllText(path);
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

    private static CommandResult RunPowerShell(string command)
    {
        return RunProcess("powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command);
    }

    private static CommandResult RunPowerShellFile(string scriptPath, params string[] arguments)
    {
        var processArguments = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath
        };
        processArguments.AddRange(arguments);
        return RunProcess("powershell.exe", processArguments.ToArray());
    }

    private static CommandResult RunProcess(string fileName, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.WorkingDirectory = RepoRoot;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;

        process.Start().Should().BeTrue();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit(60000).Should().BeTrue("PowerShell verification should finish within 60 seconds");

        return new CommandResult(process.ExitCode, stdout.Result + stderr.Result);
    }

    private static string CreateTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "wpf-devtools-sandbox-ci-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }

    private static void DeleteTempRoot(string tempRoot)
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string EscapePowerShellPath(string path)
        => path.Replace("'", "''");

    private sealed record CommandResult(int ExitCode, string Output);
}
