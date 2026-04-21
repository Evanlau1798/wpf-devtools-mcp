using System.IO;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Integration.E2E;
using Xunit;

namespace WpfDevTools.Tests.Integration;

public sealed class IntegrationParallelizationCollectionContractTests
{
    [Fact]
    public void RuntimeXunitRunnerConfig_ShouldEnableCollectionParallelization()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "xunit.runner.json")));

        document.RootElement.GetProperty("parallelizeAssembly").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("parallelizeTestCollections").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("maxParallelThreads").GetInt32().Should().Be(0);
    }

    [Fact]
    public void WpfApplicationFixtureBackedTests_ShouldUseWpfIntegrationCollection()
    {
        var violatingTypes = typeof(WpfIntegrationCollection).Assembly
            .GetTypes()
            .Where(IsConcreteTestClass)
            .Where(type => UsesFixture(type, typeof(WpfApplicationFixture)))
            .Where(type => !string.Equals(GetCollectionName(type), "WpfIntegration", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        violatingTypes.Should().BeEmpty(
            "tests backed by WpfApplicationFixture share a single STA WPF Application and must remain in the WpfIntegration collection when collection-level parallelization is enabled");
    }

    [Fact]
    public void McpFixtureBackedE2eTests_ShouldUseMcpE2ECollectionUnlessExplicitlyIsolated()
    {
        var explicitlyIsolatedTypes = new[] { typeof(VisibilityDiagnosisE2eTests) };

        var violatingTypes = typeof(McpE2eCollection).Assembly
            .GetTypes()
            .Where(IsConcreteTestClass)
            .Where(type => UsesFixture(type, typeof(McpE2eFixture)))
            .Where(type => !explicitlyIsolatedTypes.Contains(type))
            .Where(type => !string.Equals(GetCollectionName(type), "McpE2E", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        violatingTypes.Should().BeEmpty(
            "tests backed by McpE2eFixture should stay on the shared McpE2E lane unless they are explicitly isolated onto a dedicated parallelizable collection");
    }

    [Theory]
    [InlineData(typeof(BootstrapInjectionTests))]
    [InlineData(typeof(ConnectToolActiveProcessIntegrationTests))]
    [InlineData(typeof(BootstrapEventTraceIntegrationTests))]
    public void LiveBootstrapTests_ShouldUseLiveBootstrapIntegrationCollection(Type testClass)
    {
        GetCollectionName(testClass).Should().Be("LiveBootstrapIntegration");
    }

    [Fact]
    public void LiveBootstrapCollection_ShouldDisableParallelization()
    {
        GetCollectionDefinitionAttribute(typeof(LiveBootstrapIntegrationCollection))
            .DisableParallelization
            .Should().BeTrue();
    }

    [Fact]
    public void McpE2eCollection_ShouldDisableParallelization()
    {
        GetCollectionDefinitionAttribute(typeof(McpE2eCollection))
            .DisableParallelization
            .Should().BeTrue();
    }

    [Fact]
    public void VisibilityDiagnosisCollection_ShouldRemainParallelizable()
    {
        GetCollectionDefinitionAttribute(typeof(VisibilityDiagnosisE2eCollection))
            .DisableParallelization
            .Should().BeFalse(
                "the visibility E2E lane uses an isolated fixture and should remain eligible for collection-level parallel scheduling");
    }

    [Fact]
    public void VisibilityDiagnosisTests_ShouldUseVisibilityMcpE2ECollection()
    {
        GetCollectionName(typeof(VisibilityDiagnosisE2eTests))
            .Should().Be("VisibilityMcpE2E");
    }

    [Fact]
    public void VisibilityMcpE2ECollection_ShouldContainOnlyVisibilityDiagnosisTests()
    {
        var laneMembers = typeof(McpE2eCollection).Assembly
            .GetTypes()
            .Where(IsConcreteTestClass)
            .Where(type => UsesFixture(type, typeof(McpE2eFixture)))
            .Where(type => string.Equals(GetCollectionName(type), "VisibilityMcpE2E", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        laneMembers.Should().Equal(typeof(VisibilityDiagnosisE2eTests).FullName);
    }

    private static bool IsConcreteTestClass(Type type)
        => type is { IsClass: true, IsAbstract: false }
            && !type.IsGenericTypeDefinition;

    private static bool UsesFixture(Type type, Type fixtureType)
        => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => parameter.ParameterType == fixtureType);

    private static string? GetCollectionName(Type type)
    {
        var attributeData = type.GetCustomAttributesData()
            .SingleOrDefault(data => data.AttributeType == typeof(CollectionAttribute));

        attributeData.Should().NotBeNull($"{type.FullName} should declare an explicit xUnit collection");
        attributeData!.ConstructorArguments.Should().ContainSingle();
        return attributeData.ConstructorArguments[0].Value as string;
    }

    private static CollectionDefinitionAttribute GetCollectionDefinitionAttribute(Type type)
    {
        var attribute = type.GetCustomAttribute<CollectionDefinitionAttribute>();

        attribute.Should().NotBeNull($"{type.FullName} should declare a collection definition attribute");
        return attribute!;
    }
}