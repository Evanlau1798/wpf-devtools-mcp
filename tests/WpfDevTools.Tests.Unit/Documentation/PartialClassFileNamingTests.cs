using System.Text.RegularExpressions;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class PartialClassFileNamingTests
{
    [Fact]
    public void ProductionPartialClassFiles_ShouldUseClassNameFeatureNaming()
    {
        var violations = EnumerateSourceFiles()
            .SelectMany(path => FindPartialClassNames(path)
                .Select(className => new
                {
                    Path = path,
                    ClassName = className,
                    FileName = Path.GetFileNameWithoutExtension(path)
                }))
            .Where(entry => entry.FileName != entry.ClassName
                            && !entry.FileName.StartsWith(entry.ClassName + ".", StringComparison.Ordinal))
            .Select(entry => $"{entry.Path}: partial class {entry.ClassName}")
            .ToArray();

        violations.Should().BeEmpty(
            "production partial-class files should use <ClassName>.cs or <ClassName>.<Feature>.cs for ownership discovery");
    }

    private static IEnumerable<string> EnumerateSourceFiles()
    {
        var root = GetRepoFilePath("src");
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static IEnumerable<string> FindPartialClassNames(string path)
    {
        var content = File.ReadAllText(path);
        return Regex.Matches(
                content,
                @"\b(?:public|internal)\s+(?:static\s+|sealed\s+|abstract\s+)*partial\s+class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.Multiline)
            .Select(match => match.Groups["name"].Value);
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
