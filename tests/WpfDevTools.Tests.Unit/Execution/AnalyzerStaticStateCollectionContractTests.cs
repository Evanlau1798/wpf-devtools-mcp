using System.Reflection;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Tests.Unit.Inspector.Analyzers;
using WpfDevTools.Tests.Unit.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

public sealed class AnalyzerStaticStateCollectionContractTests
{
    private const string CollectionName = "AnalyzerStaticState";

    public static TheoryData<Type> StaticStateTestTypes => new()
    {
        typeof(DependencyPropertyAnalyzerCleanupTests),
        typeof(DependencyPropertyAnalyzerTests),
        typeof(DependencyPropertyAnalyzerCancellationTests),
        typeof(DependencyPropertyAnalyzerWatchEventTests),
        typeof(DependencyPropertyAnalyzerWatcherCleanupTests),
        typeof(DependencyPropertyAnalyzerWatchRegistrationRaceTests),
        typeof(PerformanceAnalyzerTests),
        typeof(PerformanceAnalyzerGapTests),
        typeof(PerformanceAnalyzerContractTests),
        typeof(PerformanceAnalyzerCircularBufferTests),
        typeof(ElementFinderCleanupTests),
        typeof(ElementFinderTests)
    };

    [Fact]
    public void AnalyzerStaticStateCollection_ShouldResetKnownStaticAnalyzers()
    {
        var collectionType = typeof(AnalyzerStaticStateCollection);

        GetCollectionDefinitionName(collectionType).Should().Be(CollectionName);
        collectionType.GetCustomAttribute<CollectionDefinitionAttribute>()!
            .DisableParallelization.Should().BeTrue();

        collectionType.GetInterfaces()
            .Should().Contain(interfaceType =>
                interfaceType.IsGenericType &&
                interfaceType.GetGenericTypeDefinition() == typeof(ICollectionFixture<>) &&
                interfaceType.GetGenericArguments()[0] == typeof(AnalyzerStaticStateFixture));
    }

    [Theory]
    [MemberData(nameof(StaticStateTestTypes))]
    public void StaticStateTests_ShouldUseAnalyzerStaticStateCollection(Type testType)
    {
        GetCollectionName(testType).Should().Be(CollectionName);
    }

    [StaFact]
    public void AnalyzerStaticStateFixture_ShouldResetElementFinderIds()
    {
        using var firstFinder = new ElementFinder();
        firstFinder.GenerateElementId(new System.Windows.Controls.Button())
            .Should().StartWith("Button_");

        AnalyzerStaticStateFixture.ResetAll();

        using var secondFinder = new ElementFinder();
        secondFinder.GenerateElementId(new System.Windows.Controls.Button())
            .Should().Be("Button_1");
    }

    private static string? GetCollectionDefinitionName(Type collectionType)
    {
        return GetAttributeConstructorString(collectionType, typeof(CollectionDefinitionAttribute));
    }

    private static string? GetCollectionName(Type testType)
    {
        return GetAttributeConstructorString(testType, typeof(CollectionAttribute));
    }

    private static string? GetAttributeConstructorString(Type type, Type attributeType)
    {
        return type.GetCustomAttributesData()
            .SingleOrDefault(attribute => attribute.AttributeType == attributeType)?
            .ConstructorArguments
            .FirstOrDefault()
            .Value as string;
    }
}
