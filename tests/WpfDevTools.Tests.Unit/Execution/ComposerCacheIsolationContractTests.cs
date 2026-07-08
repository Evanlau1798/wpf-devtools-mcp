using System.Reflection;
using FluentAssertions;
using WpfDevTools.Tests.Unit.Composer;
using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

public sealed class ComposerCacheIsolationContractTests
{
    [Fact]
    public void ComposerPackLoaderCacheTests_ShouldUseIsolatedCollection()
    {
        GetCollectionName(typeof(ComposerPackLoaderCacheTests)).Should().Be("ComposerPackLoaderCache");
        GetCollectionName(typeof(ComposerPackImportTests)).Should().Be("ComposerPackLoaderCache");
    }

    [Fact]
    public void ComposerPackLoaderCacheCollection_ShouldDisableParallelization()
    {
        var collectionType = typeof(ComposerPackLoaderCacheTests).Assembly.GetType(
            "WpfDevTools.Tests.Unit.Composer.ComposerPackLoaderCacheCollection");
        var attribute = collectionType?.GetCustomAttribute<CollectionDefinitionAttribute>();

        collectionType.Should().NotBeNull();
        attribute.Should().NotBeNull();
        attribute!.DisableParallelization.Should().BeTrue();
    }

    private static string? GetCollectionName(Type testClass)
    {
        var attributeData = testClass.GetCustomAttributesData()
            .SingleOrDefault(data => data.AttributeType == typeof(CollectionAttribute));

        attributeData.Should().NotBeNull();
        attributeData!.ConstructorArguments.Should().ContainSingle();
        return attributeData.ConstructorArguments[0].Value as string;
    }
}
