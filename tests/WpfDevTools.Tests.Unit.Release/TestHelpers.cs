namespace WpfDevTools.Tests.Unit.Release;

/// <summary>
/// Shared test helpers duplicated from WpfDevTools.Tests.Unit to keep the
/// Release/InstallerScripts test assembly self-contained.
/// </summary>
public static class TestHelpers
{
    public const string OnlineInstallerDefinitionBoundaryMarker = "# TEST_BOUNDARY_MARKER: definition-only loading stops before the main entrypoint.";

    private static int s_nextSyntheticProcessId = 1_500_000_000;

    public static int NextSyntheticProcessId() =>
        System.Threading.Interlocked.Increment(ref s_nextSyntheticProcessId);
}
