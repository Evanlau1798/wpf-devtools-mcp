using FluentAssertions;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Inspector.Utilities;

#pragma warning disable CS0618 // This contract verifies the obsolete legacy facade.
public sealed class AuditLoggerMigrationContractTests
{
    [Fact]
    public void AuditLoggerStaticFacade_ShouldBeObsolete()
    {
        typeof(AuditLogger)
            .GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false)
            .Should().ContainSingle("the static facade is retained only for backward compatibility");
    }

    [Theory]
    [InlineData("src/WpfDevTools.Inspector/Analyzers/DependencyPropertyAnalyzer.cs")]
    [InlineData("src/WpfDevTools.Inspector/Analyzers/MvvmAnalyzer.cs")]
    [InlineData("src/WpfDevTools.Inspector/Analyzers/StyleAnalyzer.cs")]
    public void MutationAnalyzers_ShouldUseInjectedAuditLoggerService(string relativePath)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));

        content.Should().Contain("IAuditLoggerService");
        content.Should().NotContain("AuditLogger.LogSecurityEvent");
    }
}
#pragma warning restore CS0618