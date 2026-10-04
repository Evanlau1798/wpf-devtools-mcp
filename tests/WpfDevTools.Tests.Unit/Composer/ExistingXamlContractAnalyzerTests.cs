using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Apply;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ExistingXamlContractAnalyzerTests
{
    private const string Namespace = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    [Fact]
    public void Analyze_ShouldExposeExistingContractsAndReportChangedBinding()
    {
        var existing = $"<TextBox {Namespace} x:Name=\"Query\" Text=\"{{Binding SearchTerm, Mode=TwoWay}}\"/>";
        var proposed = $"<TextBox {Namespace} x:Name=\"Query\" Text=\"{{Binding OtherTerm}}\"/>";

        var result = ExistingXamlContractAnalyzer.Analyze(existing, proposed, null);

        result.Contracts.Should().ContainSingle().Which.ElementName.Should().Be("Query");
        result.Contracts[0].Bindings["Text"].Should().Be("{Binding SearchTerm, Mode=TwoWay}");
        result.Changes.Should().ContainSingle(change => change.Code == "ExistingBindingChanged");
    }

    [Theory]
    [InlineData("{Binding Items}")]
    [InlineData("{MultiBinding Converter={StaticResource Join}}")]
    [InlineData("{PriorityBinding}")]
    public void Analyze_RemovedBinding_ShouldRequireContractReview(string binding)
    {
        var existing = $"<TextBox {Namespace} x:Name=\"Query\" Text=\"{binding}\"/>";
        var proposed = $"<TextBox {Namespace} x:Name=\"Query\"/>";

        var result = ExistingXamlContractAnalyzer.Analyze(existing, proposed, null);

        result.Changes.Should().ContainSingle(change => change.Code == "ExistingBindingChanged");
    }

    [Fact]
    public void Analyze_ShouldReportRemovedAndRetypedNamedElements()
    {
        var existing = $"<Grid {Namespace}><Button x:Name=\"Save\"/><TextBlock x:Name=\"Title\"/></Grid>";
        var proposed = $"<Grid {Namespace}><Border x:Name=\"Save\"/></Grid>";

        var result = ExistingXamlContractAnalyzer.Analyze(existing, proposed, codeBehind: null);

        result.Changes.Should().Contain(change =>
            change.Code == "ExistingNamedElementTypeChanged" && change.ElementName == "Save");
        result.Changes.Should().Contain(change =>
            change.Code == "ExistingNamedElementRemoved" && change.ElementName == "Title");
    }

    [Fact]
    public void Analyze_ShouldReportRemovedCodeBehindEventContract()
    {
        var existing = $"<Button {Namespace} x:Name=\"Save\" Click=\"Save_Click\"/>";
        var proposed = $"<Button {Namespace} x:Name=\"Save\"/>";

        var result = ExistingXamlContractAnalyzer.Analyze(
            existing,
            proposed,
            "private void Save_Click(object sender, RoutedEventArgs e) { }");

        result.Changes.Should().ContainSingle(change =>
            change.Code == "ExistingEventHandlerRemoved"
            && change.EventName == "Click"
            && change.HandlerName == "Save_Click");
    }

    [Fact]
    public void Analyze_WhenContractsRemain_ShouldReturnNoChanges()
    {
        var xaml = $"<Button {Namespace} x:Name=\"Save\" Click=\"Save_Click\"/>";

        var result = ExistingXamlContractAnalyzer.Analyze(
            xaml,
            xaml,
            "private void Save_Click(object sender, RoutedEventArgs e) { }");

        result.Changes.Should().BeEmpty();
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public void Analyze_ShouldBoundReportedChanges()
    {
        var existingChildren = string.Concat(Enumerable.Range(0, 140)
            .Select(index => $"<Border x:Name=\"Item{index}\"/>"));
        var existing = $"<Grid {Namespace}>{existingChildren}</Grid>";
        var proposed = $"<Grid {Namespace}/>";

        var result = ExistingXamlContractAnalyzer.Analyze(existing, proposed, null);

        result.Changes.Should().HaveCount(128);
        result.Truncated.Should().BeTrue();
    }
}
