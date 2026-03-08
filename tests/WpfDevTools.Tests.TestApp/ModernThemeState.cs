namespace WpfDevTools.Tests.TestApp;

public enum ModernThemeMode
{
    Light,
    Dark,
    System
}

public enum ModernAccentPreset
{
    Blue,
    Purple,
    Emerald
}

public sealed class ModernThemeState
{
    private readonly ModernBackdropCapability _capability;

    public ModernThemeState(ModernBackdropCapability capability)
    {
        _capability = capability;
    }

    public ModernThemeMode ThemeMode { get; private set; } = ModernThemeMode.System;

    public ModernAccentPreset AccentPreset { get; private set; } = ModernAccentPreset.Blue;

    public string ThemeModeText => ThemeMode.ToString();

    public string AccentText => AccentPreset.ToString();

    public string AccentHex => AccentPreset switch
    {
        ModernAccentPreset.Purple => "#FF7C3AED",
        ModernAccentPreset.Emerald => "#FF059669",
        _ => "#FF2563EB"
    };

    public string SurfaceHex => ThemeMode == ModernThemeMode.Dark
        ? "#FF0F172A"
        : "#FFF3F4F6";

    public string CardHex => ThemeMode == ModernThemeMode.Dark
        ? "#FF111827"
        : "#FFFFFFFF";

    public string ForegroundHex => ThemeMode == ModernThemeMode.Dark
        ? "#FFF8FAFC"
        : "#FF0F172A";

    public string MutedForegroundHex => ThemeMode == ModernThemeMode.Dark
        ? "#FFCBD5E1"
        : "#FF475569";

    public double CornerRadius => 18;

    public string CornerRadiusText => CornerRadius.ToString("0");

    public string BackdropSupportedText => _capability.BackdropSupportedText;

    public string BackdropModeText => _capability.SupportsMica
        ? _capability.DefaultBackdropMode
        : "Fallback";

    public void ApplyThemeMode(ModernThemeMode mode)
    {
        ThemeMode = mode;
    }

    public void ApplyAccent(ModernAccentPreset preset)
    {
        AccentPreset = preset;
    }
}
