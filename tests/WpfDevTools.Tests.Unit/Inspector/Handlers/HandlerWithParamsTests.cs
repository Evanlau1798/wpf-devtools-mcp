using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

public sealed class HandlerWithParamsTests
{
    [Fact]
    public async Task MvvmHandlers_ModifyViewModel_WithJsonNullValue_ShouldReachAnalyzer()
    {
        var finder = new ElementFinder();
        var handler = new MvvmHandlers(new MvvmAnalyzer(finder));
        var parameters = JsonSerializer.SerializeToElement(new
        {
            elementId = "missing-element",
            propertyName = "Title",
            value = (string?)null
        });

        var result = JsonSerializer.SerializeToElement(
            await handler.HandleAsync("modify_viewmodel", parameters, CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("ElementNotFound",
            "an explicitly present JSON null must reach the analyzer instead of being treated as a missing value");
    }
}
