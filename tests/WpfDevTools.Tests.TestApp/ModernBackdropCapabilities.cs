namespace WpfDevTools.Tests.TestApp;

public sealed record ModernBackdropCapability(
    bool SupportsMica,
    string BackdropSupportedText,
    string DefaultBackdropMode);

public static class ModernBackdropCapabilities
{
    private const int Windows11Build = 22000;

    public static ModernBackdropCapability Evaluate(Version? version)
    {
        if (version is not null && version.Major >= 10 && version.Build >= Windows11Build)
        {
            return new ModernBackdropCapability(
                SupportsMica: true,
                BackdropSupportedText: "Supported",
                DefaultBackdropMode: "Mica");
        }

        return new ModernBackdropCapability(
            SupportsMica: false,
            BackdropSupportedText: "Not Supported",
            DefaultBackdropMode: "Fallback");
    }
}
