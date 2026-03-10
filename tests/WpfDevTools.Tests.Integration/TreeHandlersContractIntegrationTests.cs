using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfIntegration")]
public sealed class TreeHandlersContractIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public TreeHandlersContractIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleGetWindows_ShouldReturnNormalizedCamelCaseWindowFields()
    {
        var payload = await _fixture.RunOnUIThreadAsync(async () =>
        {
            var mainWindow = System.Windows.Application.Current.MainWindow;
            var childWindow = new System.Windows.Window { Title = "Child Window" };

            try
            {
                childWindow.Show();
                childWindow.UpdateLayout();

                var handler = new TreeHandlers(
                    new VisualTreeAnalyzer(new ElementFinder()),
                    new LogicalTreeAnalyzer(new ElementFinder()),
                    new XamlSerializer(),
                    new ElementFinder());

                var result = await handler.HandleAsync("get_windows", null, CancellationToken.None);

                return JsonSerializer.SerializeToElement(result);
            }
            finally
            {
                childWindow.Close();
                mainWindow?.Activate();
            }
        });

        payload.GetProperty("windowCount").GetInt32().Should().BeGreaterThan(0);
        var firstWindow = payload.GetProperty("windows")[0];
        firstWindow.TryGetProperty("index", out _).Should().BeTrue();
        firstWindow.TryGetProperty("title", out _).Should().BeTrue();
        firstWindow.TryGetProperty("type", out _).Should().BeTrue();
        firstWindow.TryGetProperty("isActive", out _).Should().BeTrue();
        firstWindow.TryGetProperty("isVisible", out _).Should().BeTrue();
        firstWindow.TryGetProperty("isMainWindow", out _).Should().BeTrue();
        firstWindow.TryGetProperty("elementId", out _).Should().BeTrue();
        firstWindow.TryGetProperty("Index", out _).Should().BeFalse();
        firstWindow.TryGetProperty("Title", out _).Should().BeFalse();
        firstWindow.TryGetProperty("Type", out _).Should().BeFalse();
        firstWindow.TryGetProperty("IsActive", out _).Should().BeFalse();
        firstWindow.TryGetProperty("IsVisible", out _).Should().BeFalse();
        firstWindow.TryGetProperty("IsMainWindow", out _).Should().BeFalse();
        firstWindow.TryGetProperty("ElementId", out _).Should().BeFalse();
    }
}
