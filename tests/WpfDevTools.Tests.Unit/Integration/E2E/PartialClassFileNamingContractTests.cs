using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Integration.E2E;

public sealed class PartialClassFileNamingContractTests
{
    [Fact]
    public void InteractionE2eDpMutationPartial_ShouldUseClassDotFeatureFileName()
    {
        File.Exists(TestRepositoryPaths.GetRepoFilePath(
                "tests/WpfDevTools.Tests.Integration/E2E/InteractionE2eTests.DpMutation.cs"))
            .Should().BeTrue("partial class files should follow <ClassName>.<Feature>.cs naming");

        File.Exists(TestRepositoryPaths.GetRepoFilePath(
                "tests/WpfDevTools.Tests.Integration/E2E/InteractionDpMutationE2eTests.cs"))
            .Should().BeFalse("the old file name hides that it contributes to InteractionE2eTests");
    }
}
