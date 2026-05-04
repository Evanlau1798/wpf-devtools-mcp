namespace WpfDevTools.Tests.Unit.Release;

internal static class InstallerScriptTestSupport
{
    public static (int ExitCode, string Stdout, string Stderr) RunInstaller(
        string tempRoot,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null)
        => ReleaseScriptTestHarness.RunPowerShellScript(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
            arguments,
            CreateInstallerEnvironment(tempRoot, environmentOverrides));

    public static Dictionary<string, string?> CreateInstallerEnvironment(
        string tempRoot,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null)
    {
        var environment = ClientRegistrationArtifactTestSupport.CreateInstallerEnvironment(tempRoot);

        if (environmentOverrides is not null)
        {
            foreach (var pair in environmentOverrides)
            {
                environment[pair.Key] = pair.Value;
            }
        }

        return environment;
    }
}