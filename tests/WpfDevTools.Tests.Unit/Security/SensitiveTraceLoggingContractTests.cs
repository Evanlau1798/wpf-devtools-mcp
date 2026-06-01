using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Security;

public sealed class SensitiveTraceLoggingContractTests
{
    [Theory]
    [InlineData(
        "src/WpfDevTools.Mcp.Server/Tools/ConnectTool.Execution.cs",
        "SensitiveLogRedactor.Redact(processInfo.ExecutablePath)",
        "SensitiveLogRedactor.Redact(ex.ToString())")]
    [InlineData(
        "src/WpfDevTools.Mcp.Server/Tools/ConnectTool.RawInjectionIdentity.cs",
        "SensitiveLogRedactor.Redact(context.ProcessInfo.ExecutablePath)",
        "SensitiveLogRedactor.Redact(currentProcessInfo.ExecutablePath)")]
    [InlineData(
        "src/WpfDevTools.Mcp.Server/Tools/ConnectTool.Response.cs",
        "SensitiveLogRedactor.Redact(exception.ToString())",
        "SensitiveLogRedactor.Redact(injectionResult.ErrorMessage)")]
    [InlineData(
        "src/WpfDevTools.Mcp.Server/Tools/ConnectTool.AutoDiscovery.cs",
        "SensitiveLogRedactor.Redact(ex.Message)",
        "ConnectTool SDK-only packaging heuristic failed")]
    [InlineData(
        "src/WpfDevTools.Mcp.Server/Tools/RawInjectionTargetPolicy.cs",
        "SensitiveLogRedactor.Redact(ex.Message)",
        "RawInjectionTargetPolicy path normalization failed")]
    [InlineData(
        "src/WpfDevTools.Inspector.Sdk/InspectorSdk.cs",
        "SensitiveLogRedactor.Redact(ex.ToString())",
        "SensitiveLogRedactor.Redact(cleanupError.ToString())")]
    [InlineData(
        "src/WpfDevTools.Injector/Injection/DllInjector.cs",
        "SensitiveLogRedactor.Redact(diagnosticMessage)",
        "SensitiveLogRedactor.Redact(exception.ToString())")]
    [InlineData(
        "src/WpfDevTools.Shared/Utilities/FileLogger.cs",
        "SensitiveLogRedactor.Redact(ex.Message)",
        "SensitiveLogRedactor.Redact(shutdownError.Message)")]
    public void ProductionTraceDiagnostics_ShouldRedactSensitiveMessages(
        string relativePath,
        string firstExpectedRedaction,
        string secondExpectedRedaction)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(firstExpectedRedaction);
        content.Should().Contain(secondExpectedRedaction);
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
