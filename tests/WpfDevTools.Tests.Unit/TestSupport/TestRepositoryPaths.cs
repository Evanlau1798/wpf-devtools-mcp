using FluentAssertions;
using WpfDevTools.Shared.IO;

namespace WpfDevTools.Tests.Unit.TestSupport;

internal static class TestRepositoryPaths
{
    private static readonly Lazy<string> RepoRoot = new(() => ResolveRepoRoot(AppContext.BaseDirectory));

    public static string GetRepoFilePath(string relativePath)
        => ResolveFromBaseDirectory(AppContext.BaseDirectory, relativePath);

    public static string GetPrimaryRepoFilePath(string relativePath)
        => ResolveFromPrimaryRoot(AppContext.BaseDirectory, relativePath);

    internal static string ResolveFromBaseDirectory(string baseDirectory, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(relativePath);

        var repoRoot = ResolveRepoRoot(baseDirectory);
        return Path.GetFullPath(Path.Combine(repoRoot, relativePath));
    }

    internal static string ResolveFromPrimaryRoot(string baseDirectory, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(relativePath);

        var roots = RepositoryLayoutLocator.EnumerateSolutionRoots(baseDirectory);
        roots.Should().NotBeEmpty(
            $"at least one solution root should be discoverable from '{baseDirectory}' when resolving '{relativePath}'");

        var primaryRoot = roots[^1];
        var candidate = Path.GetFullPath(Path.Combine(primaryRoot, relativePath));
        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' under the explicit primary solution root '{primaryRoot}'.");
    }

    internal static string ResolveRepoRoot(string baseDirectory)
    {
        var roots = RepositoryLayoutLocator.EnumerateSolutionRoots(baseDirectory);
        roots.Should().NotBeEmpty(
            $"a solution root should be discoverable from '{baseDirectory}' when unit tests run inside the repository");

        return roots[0];
    }
}
