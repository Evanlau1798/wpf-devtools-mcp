using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

/// <summary>
/// Serializes installer script tests in the Release test assembly.
/// These tests run PowerShell processes that share common repo output directories
/// and installer state, so they must not run concurrently with each other.
/// </summary>
[CollectionDefinition("InstallerScripts")]
public sealed class InstallerScriptsCollection
{
}
