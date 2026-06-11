namespace WpfDevTools.Tests.Unit.Release;

internal static class PublishReleaseScriptSource
{
    private static readonly string[] ScriptFileNames =
    [
        "Publish-Release.ps1",
        "Publish-Release.Core.ps1",
        "Publish-Release.Signing.ps1",
        "Publish-Release.Native.ps1"
    ];

    public static string ReadAll()
    {
        var files = ScriptFileNames
            .Select(fileName => ReleaseScriptTestHarness.GetRepoFilePath(
                Path.Combine("scripts", "tools", "packaging", fileName)))
            .Select(File.ReadAllText);

        return string.Join(Environment.NewLine, files);
    }

    public static void CopyTo(string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var fileName in ScriptFileNames)
        {
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath(Path.Combine("scripts", "tools", "packaging", fileName)),
                Path.Combine(destinationDirectory, fileName),
                overwrite: true);
        }
    }
}
