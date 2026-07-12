namespace WpfDevTools.Tests.Unit.Release;

internal static class ClientRegistrationArtifactTestSupport
{
    public static Dictionary<string, string?> CreateInstallerEnvironment(string tempRoot)
        => new()
        {
            ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
            ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile")
        };
}
