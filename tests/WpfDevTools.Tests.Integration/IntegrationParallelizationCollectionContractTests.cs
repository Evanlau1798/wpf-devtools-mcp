using System.IO;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Integration.E2E;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace WpfDevTools.Tests.Integration;

public sealed class IntegrationParallelizationCollectionContractTests
{
    [Fact]
    public void RuntimeXunitRunnerConfig_ShouldEnableCollectionParallelization()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "xunit.runner.json")));

        document.RootElement.GetProperty("parallelizeAssembly").GetBoolean().Should().BeTrue("the integration suite should run independent collections in parallel; WpfIntegration and LiveBootstrapIntegration share a common serialization lane via merged collection");
        document.RootElement.GetProperty("parallelizeTestCollections").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("maxParallelThreads").GetInt32().Should().Be(0);
    }

    [Fact]
    public void RuntimeAssembly_ShouldRunLiveBootstrapCollectionFirst()
    {
        var attribute = typeof(LiveBootstrapFirstCollectionOrderer).Assembly
            .GetCustomAttribute<TestCollectionOrdererAttribute>();

        attribute.Should().NotBeNull();
        typeof(LiveBootstrapFirstCollectionOrderer)
            .Should()
            .BeAssignableTo<ITestCollectionOrderer>();
    }

    [Fact]
    public void LiveBootstrapFirstCollectionOrderer_ShouldPrioritizeLiveBootstrapCollection()
    {
        var alphaCollection = new StubTestCollection("AlphaCollection");
        var liveBootstrapCollection = new StubTestCollection("WpfAndBootstrapIntegration");
        var zetaCollection = new StubTestCollection("ZetaCollection");

        var orderedCollections = new LiveBootstrapFirstCollectionOrderer()
            .OrderTestCollections(new ITestCollection[]
            {
                alphaCollection,
                zetaCollection,
                liveBootstrapCollection
            })
            .ToArray();

        orderedCollections[0].Should().BeSameAs(liveBootstrapCollection);
        orderedCollections.Skip(1).Select(collection => collection.DisplayName)
            .Should()
            .Equal("AlphaCollection", "ZetaCollection");
    }

    [Fact]
    public void WpfApplicationFixtureBackedTests_ShouldUseWpfAndBootstrapIntegrationCollection()
    {
        var violatingTypes = typeof(WpfAndBootstrapIntegrationCollection).Assembly
            .GetTypes()
            .Where(IsConcreteTestClass)
            .Where(type => UsesFixture(type, typeof(WpfApplicationFixture)))
            .Where(type => !string.Equals(GetCollectionName(type), "WpfAndBootstrapIntegration", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        violatingTypes.Should().BeEmpty(
            "tests backed by WpfApplicationFixture share a single STA WPF Application and must remain in the WpfAndBootstrapIntegration serialization lane");
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
    [InlineData(typeof(McpToolSearchMetadataE2eTests))]
    [InlineData(typeof(ToolErrorContractE2eTests))]
    public void StdioServerOwningE2eTests_ShouldUseMcpE2ECollection(Type testClass)
    {
        GetCollectionName(testClass).Should().Be("McpE2E",
            "tests that start their own MCP server process should not run concurrently with shared McpE2E server lifecycles");
    }

    [Theory]
    [InlineData(typeof(BootstrapInjectionTests))]
    [InlineData(typeof(ConnectToolActiveProcessIntegrationTests))]
    [InlineData(typeof(BootstrapEventTraceIntegrationTests))]
    public void LiveBootstrapTests_ShouldUseWpfAndBootstrapIntegrationCollection(Type testClass)
    {
        GetCollectionName(testClass).Should().Be("WpfAndBootstrapIntegration");
    }

    [Fact]
    public void WpfAndBootstrapIntegrationCollection_ShouldRemainParallelizableWithOtherCollections()
    {
        GetCollectionDefinitionAttribute(typeof(WpfAndBootstrapIntegrationCollection))
            .DisableParallelization
            .Should().BeFalse(
                "tests in the merged WPF/bootstrap collection are already serialized with each other by the shared collection, but the lane should remain eligible to run beside isolated integration collections");
    }

    [Fact]
    public void BootstrapDelayHookTests_ShouldScopeDelayEnvironmentToTargetProcess()
    {
        var sourceRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(
            sourceRoot,
            "tests",
            "WpfDevTools.Tests.Integration",
            "BootstrapInjectionTests.cs"));

        source.Should().NotContain("Environment.SetEnvironmentVariable",
            "Debug bootstrap delay hooks must not mutate process-wide environment while integration collections run in parallel");
        source.Should().NotContain("EnvironmentVariableScope",
            "delay hooks should be scoped to the launched TestApp process instead of the parent test process");
        source.Should().Contain("IReadOnlyDictionary<string, string>? environmentVariables = null");
        source.Should().Contain("environmentVariables: new Dictionary<string, string>");
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

    [Fact]
    public void IntegrationAssembly_ShouldNotEnableSignatureSkipWithModuleInitializer()
    {
        var sourceRoot = FindRepoRoot();
        var offenders = Directory
            .EnumerateFiles(Path.Combine(sourceRoot, "tests", "WpfDevTools.Tests.Integration"), "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("[ModuleInitializer]", StringComparison.Ordinal)
                    && text.Contains("Environment.SetEnvironmentVariable(\"WPFDEVTOOLS_SKIP_SIGNATURE_CHECK\"", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "Release signature skip must be scoped to explicit test validators or child-process environments, not enabled globally for the integration assembly");
    }

    [Fact]
    public void TrustedLocalReleaseSignatureSkip_ShouldNotMutateProcessEnvironment()
    {
        var sourceRoot = FindRepoRoot();
        var helperPath = Path.Combine(
            sourceRoot,
            "tests",
            "WpfDevTools.Tests.Integration",
            "TrustedLocalReleaseSignatureSkip.cs");

        File.ReadAllText(helperPath).Should().NotContain("SetEnvironmentVariable",
            "the live-injection Release signature skip helper must not toggle process-wide environment state while integration tests run in parallel");
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

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class StubTestCollection : LongLivedMarshalByRefObject, ITestCollection
    {
        public StubTestCollection()
            : this(string.Empty)
        {
        }

        public StubTestCollection(string displayName)
        {
            DisplayName = displayName;
        }

        public ITypeInfo CollectionDefinition => null!;

        public string DisplayName { get; }

        public ITestAssembly TestAssembly => null!;

        public Guid UniqueID { get; } = Guid.NewGuid();

        public void Deserialize(IXunitSerializationInfo info)
        {
        }

        public void Serialize(IXunitSerializationInfo info)
        {
        }
    }
}
