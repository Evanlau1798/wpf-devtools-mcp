using System.Text;

namespace WpfDevTools.Tests.Unit.Release;

internal static partial class ReleaseScriptTestHarness
{
    public static string GetOnlineInstallerSourceBundle()
    {
        var builder = new StringBuilder(File.ReadAllText(
            GetRepoFilePath("scripts/online-installer.ps1")));
        var helperRoot = GetRepoFilePath("scripts/installer");
        foreach (var runtimeName in GetOnlineInstallerRuntimeNames())
        {
            builder.AppendLine();
            builder.Append(File.ReadAllText(Path.Combine(helperRoot, runtimeName)));
        }

        return builder.ToString();
    }

    public static void CopyOnlineInstallerRuntimeBundle(string destinationRoot)
    {
        var destination = Path.Combine(destinationRoot, "installer");
        Directory.CreateDirectory(destination);
        var helperRoot = GetRepoFilePath("scripts/installer");
        File.Copy(
            Path.Combine(helperRoot, "installer-helpers.manifest.json"),
            Path.Combine(destination, "installer-helpers.manifest.json"),
            overwrite: true);
        foreach (var runtimeName in GetOnlineInstallerRuntimeNames())
        {
            File.Copy(
                Path.Combine(helperRoot, runtimeName),
                Path.Combine(destination, runtimeName),
                overwrite: true);
        }
    }

    private static string[] GetOnlineInstallerRuntimeNames()
        => GetInstallerHelperFiles()
            .Where(name => name.StartsWith("OnlineInstaller.Runtime.", StringComparison.Ordinal)
                && name.EndsWith(".ps1", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
}
