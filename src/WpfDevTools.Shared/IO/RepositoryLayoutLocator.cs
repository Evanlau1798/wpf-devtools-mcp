namespace WpfDevTools.Shared.IO;

/// <summary>
/// Resolves solution roots in both the current worktree and the primary repository root.
/// </summary>
public static class RepositoryLayoutLocator
{
    private const string SolutionFileName = "WpfDevTools.sln";

    /// <summary>
    /// Enumerate the nearest solution root and, when running inside a git worktree folder,
    /// the primary repository root that may contain shared build artifacts.
    /// </summary>
    public static IReadOnlyList<string> EnumerateSolutionRoots(string startDirectory)
    {
        var roots = new List<string>();
        var nearest = FindNearestSolutionRoot(startDirectory);
        AddIfUnique(roots, nearest);
        AddIfUnique(roots, TryGetPrimaryRepositoryRoot(nearest));
        return roots;
    }

    private static string? FindNearestSolutionRoot(string startDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, SolutionFileName)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string? TryGetPrimaryRepositoryRoot(string? solutionRoot)
    {
        if (string.IsNullOrWhiteSpace(solutionRoot))
        {
            return null;
        }

        var dir = new DirectoryInfo(Path.GetFullPath(solutionRoot));
        var worktreeContainer = dir.Parent;
        if (worktreeContainer == null)
        {
            return null;
        }

        var isWorktreeRoot =
            string.Equals(worktreeContainer.Name, ".worktree", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(worktreeContainer.Name, ".worktrees", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(worktreeContainer.Name, "worktrees", StringComparison.OrdinalIgnoreCase);
        if (!isWorktreeRoot)
        {
            return null;
        }

        var primaryRoot = worktreeContainer.Parent?.FullName;
        if (primaryRoot == null)
        {
            return null;
        }

        return File.Exists(Path.Combine(primaryRoot, SolutionFileName))
            ? primaryRoot
            : null;
    }

    private static void AddIfUnique(ICollection<string> roots, string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var normalized = Path.GetFullPath(root);
        if (!roots.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            roots.Add(normalized);
        }
    }
}
