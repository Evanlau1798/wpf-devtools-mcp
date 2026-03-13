using Xunit;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Serializes release-packaging tests because they invoke shared build scripts
/// that write into common repo output directories.
/// </summary>
[CollectionDefinition("PackagingIntegration", DisableParallelization = true)]
public sealed class PackagingIntegrationCollection
{
}
