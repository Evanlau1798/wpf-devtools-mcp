using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

/// <summary>
/// Serializes installer script tests while allowing them to run beside unrelated timing-sensitive lanes.
/// </summary>
[CollectionDefinition("InstallerScripts")]
public sealed class InstallerScriptsCollection
{
}
