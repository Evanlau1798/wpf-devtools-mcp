namespace WpfDevTools.Tests.Unit.TestSupport;

internal static class TestPackageFixture
{
    public static string CreateUnsignedInspectorPackage(string tempRoot)
    {
        var packageRoot = Path.Combine(tempRoot, "package");
        var inspectorPath = Path.Combine(
            packageRoot,
            "bin",
            "inspectors",
            "net8.0-windows",
            "WpfDevTools.Inspector.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(inspectorPath)!);
        File.WriteAllText(inspectorPath, "unsigned test payload");
        return packageRoot;
    }
}
