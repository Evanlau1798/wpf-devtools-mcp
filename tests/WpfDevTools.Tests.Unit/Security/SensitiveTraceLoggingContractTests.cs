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

    [Fact]
    public void ProductionTraceDiagnostics_WithDynamicFailureText_ShouldUseCentralRedactor()
    {
        var violations = Directory
            .EnumerateFiles(GetRepoFilePath("src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(ExtractDiagnosticStatements)
            .Where(statement => ContainsDynamicFailureText(statement.Text))
            .Where(statement => !statement.Text.Contains("SensitiveLogRedactor.Redact", StringComparison.Ordinal))
            .Select(statement => $"{Path.GetRelativePath(GetRepoFilePath("."), statement.Path).Replace('\\', '/')}:{statement.Line}")
            .ToArray();

        violations.Should().BeEmpty(
            "production Trace/Debug diagnostics that include dynamic failure text can carry local paths, target metadata, or sensitive values");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

    private static IEnumerable<DiagnosticStatement> ExtractDiagnosticStatements(string path)
    {
        var lines = File.ReadAllLines(path);

        for (var index = 0; index < lines.Length; index++)
        {
            if (!ContainsDiagnosticCall(lines[index]))
            {
                continue;
            }

            var statementLines = new List<string> { lines[index] };
            var cursor = index;
            while (!lines[cursor].Contains(';', StringComparison.Ordinal) && cursor + 1 < lines.Length)
            {
                cursor++;
                statementLines.Add(lines[cursor]);
            }

            yield return new DiagnosticStatement(path, index + 1, string.Join(Environment.NewLine, statementLines));
        }
    }

    private static bool ContainsDiagnosticCall(string line)
        => line.Contains("Trace.WriteLine", StringComparison.Ordinal)
           || line.Contains("Trace.TraceWarning", StringComparison.Ordinal)
           || line.Contains("Trace.TraceError", StringComparison.Ordinal)
           || line.Contains("Debug.WriteLine", StringComparison.Ordinal);

    private static bool ContainsDynamicFailureText(string statement)
        => statement.Contains(".Message", StringComparison.Ordinal)
           || statement.Contains(".ToString()", StringComparison.Ordinal)
           || statement.Contains("{ex", StringComparison.Ordinal)
           || statement.Contains("{exception", StringComparison.Ordinal)
           || statement.Contains("{cleanup", StringComparison.Ordinal)
           || statement.Contains("{message}", StringComparison.Ordinal)
           || statement.Contains("{diagnostic", StringComparison.Ordinal);

    private sealed record DiagnosticStatement(string Path, int Line, string Text);
}
