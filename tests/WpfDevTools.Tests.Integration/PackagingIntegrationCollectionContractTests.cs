using System.Reflection;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Integration;

public sealed class PackagingIntegrationCollectionContractTests
{
    [Theory]
    [InlineData(typeof(ReleasePackagingIntegrationTests))]
    [InlineData(typeof(OnlineInstallerIntegrationTests))]
    public void PackagingTests_ShouldUsePackagingIntegrationCollection(Type testClass)
    {
        var attributeData = testClass.GetCustomAttributesData()
            .SingleOrDefault(data => data.AttributeType == typeof(CollectionAttribute));

        attributeData.Should().NotBeNull();
        attributeData!.ConstructorArguments.Should().ContainSingle();
        attributeData.ConstructorArguments[0].Value.Should().Be("PackagingIntegration");
    }

    [Fact]
    public void PackagingCollection_ShouldDisableParallelization()
    {
        var collectionType = typeof(ReleasePackagingIntegrationTests).Assembly
            .GetType("WpfDevTools.Tests.Integration.PackagingIntegrationCollection");
        var attribute = collectionType?.GetCustomAttribute<CollectionDefinitionAttribute>();

        collectionType.Should().NotBeNull();
        attribute.Should().NotBeNull();
        attribute!.DisableParallelization.Should().BeTrue();
    }
}
