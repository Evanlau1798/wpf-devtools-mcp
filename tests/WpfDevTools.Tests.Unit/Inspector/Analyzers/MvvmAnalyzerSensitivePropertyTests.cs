using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class MvvmAnalyzerSensitivePropertyTests
{
    [StaFact]
    public void ModifyViewModel_WithSearchKeywordProperty_ShouldNotTreatKeywordAsSecretKey()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new SearchViewModel { SearchKeyword = "old" };
        var element = new TextBox { DataContext = viewModel };
        var elementId = finder.GenerateElementId(element);

        var result = JsonSerializer.SerializeToElement(
            analyzer.ModifyViewModel(elementId, nameof(SearchViewModel.SearchKeyword), "focused"));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        viewModel.SearchKeyword.Should().Be("focused");
    }

    [StaFact]
    public void ModifyViewModel_WithApiKeyProperty_ShouldStillBlockSecretKeyToken()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new SensitiveViewModel { ApiKey = "old" };
        var element = new TextBox { DataContext = viewModel };
        var elementId = finder.GenerateElementId(element);

        var result = JsonSerializer.SerializeToElement(
            analyzer.ModifyViewModel(elementId, nameof(SensitiveViewModel.ApiKey), "new"));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Contain("security reasons");
        viewModel.ApiKey.Should().Be("old");
    }

    private sealed class SearchViewModel
    {
        public string SearchKeyword { get; set; } = string.Empty;
    }

    private sealed class SensitiveViewModel
    {
        public string ApiKey { get; set; } = string.Empty;
    }
}
