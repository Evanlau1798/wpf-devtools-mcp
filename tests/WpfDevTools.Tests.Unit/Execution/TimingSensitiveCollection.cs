using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

/// <summary>
/// Serializes timing-budget tests whose assertions assume limited workstation contention.
/// </summary>
[CollectionDefinition("TimingSensitive")]
public sealed class TimingSensitiveCollection
{
}
