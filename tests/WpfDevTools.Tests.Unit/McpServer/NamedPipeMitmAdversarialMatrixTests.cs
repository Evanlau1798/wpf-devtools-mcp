using System.Reflection;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class NamedPipeMitmAdversarialMatrixTests
{
    private static readonly string[] RequiredScenarios =
    [
        "wrong-server-pid",
        "wrong-hmac-secret",
        "wrong-certificate-thumbprint",
        "stale-build-fingerprint",
        "custom-pipe-name-bypass",
        "sdk-host-mismatched-auth-or-cert",
        "rejected-fake-host-no-raw-injection-fallback"
    ];

    [Fact]
    public void CoverageMatrix_ShouldListEveryRequiredMitmScenario()
    {
        var coveredScenarios = DiscoverScenarioCoverage();

        coveredScenarios.Select(item => item.Id)
            .Should()
            .Contain(RequiredScenarios,
                "the named-pipe MITM regression suite should expose one auditable matrix for every release-blocking adversarial scenario");
    }

    [Theory]
    [MemberData(nameof(ScenarioCoverage))]
    public void CoverageMatrixEntries_ShouldPointToExecutableTests(
        string scenarioId,
        string requirement,
        MethodInfo testMethod)
    {
        scenarioId.Should().NotBeNullOrWhiteSpace();
        requirement.Should().NotBeNullOrWhiteSpace();
        testMethod.GetCustomAttributes<FactAttribute>()
            .Should()
            .NotBeEmpty($"scenario {scenarioId} must point at a real executable xUnit test");
    }

    public static TheoryData<string, string, MethodInfo> ScenarioCoverage()
    {
        var data = new TheoryData<string, string, MethodInfo>();
        foreach (var coverage in DiscoverScenarioCoverage())
        {
            data.Add(coverage.Id, coverage.Requirement, coverage.TestMethod);
        }

        return data;
    }

    private static IReadOnlyList<ScenarioCoverageEntry> DiscoverScenarioCoverage()
    {
        return typeof(NamedPipeMitmAdversarialMatrixTests)
            .Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic)
                .SelectMany(method => method
                    .GetCustomAttributes<NamedPipeMitmScenarioAttribute>()
                    .Select(attribute => new ScenarioCoverageEntry(
                        attribute.Id,
                        attribute.Requirement,
                        method))))
            .ToArray();
    }

    private sealed record ScenarioCoverageEntry(
        string Id,
        string Requirement,
        MethodInfo TestMethod);
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class NamedPipeMitmScenarioAttribute(
    string id,
    string requirement) : Attribute
{
    public string Id { get; } = id;

    public string Requirement { get; } = requirement;
}
