namespace WpfDevTools.Tests.Unit.TestSupport;

public static class DocumentationMarkdown
{
    public static string ExtractVariableContext(string content, string variableName)
    {
        var heading = "### " + variableName;
        var headingIndex = content.IndexOf(heading, StringComparison.Ordinal);
        if (headingIndex >= 0)
        {
            var nextHeadingIndex = content.IndexOf(
                Environment.NewLine + "### ",
                headingIndex + heading.Length,
                StringComparison.Ordinal);

            return nextHeadingIndex > headingIndex
                ? content[headingIndex..nextHeadingIndex]
                : content[headingIndex..];
        }

        return string.Join(
            Environment.NewLine,
            content.Split('\n')
                .Where(line => line.Contains(variableName, StringComparison.Ordinal)));
    }
}
