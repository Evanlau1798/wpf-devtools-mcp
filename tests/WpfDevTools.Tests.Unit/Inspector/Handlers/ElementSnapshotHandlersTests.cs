using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Host.Handlers;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

public sealed class ElementSnapshotHandlersTests
{
    [Fact]
    public async Task HandleAsync_ShouldDelegateToAggregatorWithoutRequestHandlerFanOut()
    {
        var observedParams = default(JsonElement?);
        var handler = new ElementSnapshotHandlers(new DelegateElementSnapshotAggregator((parameters, _) =>
        {
            observedParams = parameters;
            return Task.FromResult<object>(new
            {
                success = true,
                elementId = "TextBox_1",
                elementType = "TextBox"
            });
        }));

        var result = JsonSerializer.SerializeToElement(await handler.HandleAsync(
            "get_element_snapshot",
            JsonSerializer.SerializeToElement(new { elementId = "TextBox_1" }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("elementId").GetString().Should().Be("TextBox_1");
        observedParams!.Value.GetProperty("elementId").GetString().Should().Be("TextBox_1");
    }

    [Fact]
    public void ElementSnapshotHandlerSource_ShouldNotDependOnRequestHandlerFanOut()
    {
        var source = File.ReadAllText(Path.Combine(
            FindSourceRoot(),
            "src",
            "WpfDevTools.Inspector",
            "Host",
            "Handlers",
            "ElementSnapshotHandlers.cs"));

        source.Should().NotContain("IRequestHandler treeHandlers");
        source.Should().NotContain("ExecuteStepAsync(");
        source.Should().NotContain("JsonSerializer.SerializeToElement(parameters)");
    }

    [Fact]
    public async Task HandleAsync_WhenSubstepReturnsFailure_ShouldPreserveFailedStepContext()
    {
        var aggregator = ElementSnapshotTestAggregator.Create(
            new DelegateRequestHandler((_, _, _) => Task.FromResult<object>(new
            {
                success = false,
                error = "Element not found: 'TextBox_1'",
                errorCode = "ElementNotFound",
                hint = "Call get_visual_tree first to confirm the target elementId."
            })),
            new DelegateRequestHandler(static (_, _, _) => Task.FromResult<object>(new { success = true })),
            new DelegateRequestHandler(static (_, _, _) => Task.FromResult<object>(new { success = true })),
            new DelegateRequestHandler(static (_, _, _) => Task.FromResult<object>(new { success = true })),
            new DelegateRequestHandler(static (_, _, _) => Task.FromResult<object>(new { success = true })),
            new DelegateRequestHandler(static (_, _, _) => Task.FromResult<object>(new { success = true })));
        var handler = new ElementSnapshotHandlers(aggregator);

        var result = JsonSerializer.SerializeToElement(await handler.HandleAsync(
            "get_element_snapshot",
            JsonSerializer.SerializeToElement(new { elementId = "TextBox_1", propertyNames = new[] { "Text" } }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("failedStep").GetString().Should().Be("get_visual_tree");
        result.GetProperty("error").GetString().Should().Be("Failed during get_visual_tree while building element snapshot. Element not found: 'TextBox_1'");
        result.GetProperty("errorCode").GetString().Should().Be("ElementNotFound");
    }

    [Fact]
    public async Task HandleAsync_WhenSubstepThrows_ShouldPreserveFailedStepContext()
    {
        var aggregator = ElementSnapshotTestAggregator.Create(
            new DelegateRequestHandler(static (_, _, _) => Task.FromResult<object>(new
            {
                success = true,
                tree = new { type = "TextBox", name = "NameTextBox" }
            })),
            new DelegateRequestHandler(static (method, _, _) => Task.FromResult<object>(method switch
            {
                "get_datacontext_chain" => new
                {
                    success = true,
                    chain = new[] { new { dataContextType = "TestViewModel" } }
                },
                "get_bindings" => new { success = true, bindings = Array.Empty<object>() },
                _ => new { success = true }
            })),
            new DelegateRequestHandler(static (_, _, _) => Task.FromResult<object>(new { success = true, errors = Array.Empty<object>() })),
            new DelegateRequestHandler(static (_, _, _) => Task.FromResult<object>(new { success = true, hasStyle = false, styleCount = 0, styles = Array.Empty<object>() })),
            new DelegateRequestHandler(static (_, _, _) => throw new InvalidOperationException("boom")),
            new DelegateRequestHandler(static (_, _, _) => Task.FromResult<object>(new { success = true, currentValue = "Alice", baseValueSource = "LocalValue" })));
        var handler = new ElementSnapshotHandlers(aggregator);

        var result = JsonSerializer.SerializeToElement(await handler.HandleAsync(
            "get_element_snapshot",
            JsonSerializer.SerializeToElement(new { elementId = "TextBox_1", propertyNames = new[] { "Text" } }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("failedStep").GetString().Should().Be("get_layout_info");
        result.GetProperty("error").GetString().Should().Be("Failed during get_layout_info while building element snapshot. Internal inspector error occurred");
        result.GetProperty("errorCode").GetString().Should().Be("InternalError");
    }

    [Fact]
    public async Task HandleAsync_WhenPropertyLookupReturnsFailure_ShouldPreserveFailedStepContext()
    {
        var aggregator = ElementSnapshotTestAggregator.Create(
            new DelegateRequestHandler(static (_, _, _) => Task.FromResult<object>(new
            {
                success = true,
                tree = new { type = "TextBox", name = "NameTextBox" }
            })),
            new DelegateRequestHandler(static (method, _, _) => Task.FromResult<object>(method switch
            {
                "get_datacontext_chain" => new
                {
                    success = true,
                    chain = new[] { new { dataContextType = "TestViewModel" } }
                },
                "get_bindings" => new { success = true, bindings = Array.Empty<object>() },
                _ => new { success = true }
            })),
            new DelegateRequestHandler(static (_, _, _) => Task.FromResult<object>(new { success = true, errors = Array.Empty<object>() })),
            new DelegateRequestHandler(static (_, _, _) => Task.FromResult<object>(new { success = true, hasStyle = false, styleCount = 0, styles = Array.Empty<object>() })),
            new DelegateRequestHandler(static (_, _, _) => Task.FromResult<object>(new { success = true, actualWidth = 120.0, actualHeight = 24.0 })),
            new DelegateRequestHandler(static (_, _, _) => Task.FromResult<object>(new
            {
                success = false,
                error = "Element not found: 'TextBox_1'",
                errorCode = "ElementNotFound",
                hint = "Refresh the tree and retry the property lookup."
            })));
        var handler = new ElementSnapshotHandlers(aggregator);

        var result = JsonSerializer.SerializeToElement(await handler.HandleAsync(
            "get_element_snapshot",
            JsonSerializer.SerializeToElement(new { elementId = "TextBox_1", propertyNames = new[] { "Text" } }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("failedStep").GetString().Should().Be("get_dp_value_source");
        result.GetProperty("error").GetString().Should().Be("Failed during get_dp_value_source while building element snapshot. Element not found: 'TextBox_1'");
        result.GetProperty("errorCode").GetString().Should().Be("ElementNotFound");
    }

    private sealed class DelegateRequestHandler(
        Func<string, JsonElement?, CancellationToken, Task<object>> handleAsync) : IRequestHandler
    {
        public IEnumerable<string> GetSupportedMethods() => [];

        public Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken) =>
            handleAsync(method, @params, cancellationToken);
    }

    private sealed class DelegateElementSnapshotAggregator(
        Func<JsonElement?, CancellationToken, Task<object>> getSnapshotAsync) : IElementSnapshotAggregator
    {
        public Task<object> GetElementSnapshotAsync(JsonElement? @params, CancellationToken cancellationToken) =>
            getSnapshotAsync(@params, cancellationToken);
    }

    private static class ElementSnapshotTestAggregator
    {
        public static IElementSnapshotAggregator Create(
            IRequestHandler treeHandlers,
            IRequestHandler bindingHandlers,
            IRequestHandler mvvmHandlers,
            IRequestHandler styleHandlers,
            IRequestHandler layoutHandlers,
            IRequestHandler dependencyPropertyHandlers)
        {
            return new ElementSnapshotAggregator(async (method, elementId, propertyName, cancellationToken) =>
            {
                var handler = method switch
                {
                    "get_visual_tree" => treeHandlers,
                    "get_datacontext_chain" or "get_bindings" => bindingHandlers,
                    "get_validation_errors" => mvvmHandlers,
                    "get_applied_styles" => styleHandlers,
                    "get_layout_info" => layoutHandlers,
                    "get_dp_value_source" => dependencyPropertyHandlers,
                    _ => throw new InvalidOperationException($"Unsupported method: {method}")
                };

                var parameters = method switch
                {
                    "get_visual_tree" => (object)new { elementId, depth = 0, compact = true },
                    "get_applied_styles" => new { elementId, compact = true },
                    "get_dp_value_source" => new { elementId, propertyName },
                    _ => new { elementId }
                };

                return await handler.HandleAsync(
                    method,
                    JsonSerializer.SerializeToElement(parameters),
                    cancellationToken).ConfigureAwait(false);
            });
        }
    }

    private static string FindSourceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
