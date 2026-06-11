namespace WpfDevTools.Tests.Unit.Release;

internal static class ReleaseScriptHarnessSource
{
    public static string ReadAll()
    {
        var harnessDirectory = ReleaseScriptTestHarness.GetRepoFilePath(
            Path.Combine("tests", "WpfDevTools.Tests.Unit.Release", "Release"));

        var files = Directory
            .EnumerateFiles(harnessDirectory, "ReleaseScriptTestHarness*.cs")
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText);

        return string.Join(Environment.NewLine, files);
    }
}
