using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class MvvmAnalyzerSecurityPatternTests
{
    [StaTheory]
    [InlineData("Password")]
    [InlineData("UserPassword")]
    [InlineData("ApiToken")]
    [InlineData("SecretKey")]
    [InlineData("ConnectionString")]
    [InlineData("SessionCookie")]
    [InlineData("AuthHeader")]
    public void ModifyViewModel_WithCompoundSensitivePropertyName_ShouldBlockMutation(string propertyName)
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new SensitiveNamesViewModel();
        var property = typeof(SensitiveNamesViewModel).GetProperty(propertyName)!;
        property.SetValue(viewModel, "original");
        var element = new TextBox { DataContext = viewModel };
        var elementId = finder.GenerateElementId(element);

        var result = JsonSerializer.SerializeToElement(
            analyzer.ModifyViewModel(elementId, propertyName, "modified"));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Contain("security reasons");
        property.GetValue(viewModel).Should().Be("original");
    }

    private sealed class SensitiveNamesViewModel
    {
        public string Password { get; set; } = string.Empty;
        public string UserPassword { get; set; } = string.Empty;
        public string ApiToken { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
        public string SessionCookie { get; set; } = string.Empty;
        public string AuthHeader { get; set; } = string.Empty;
    }
}
