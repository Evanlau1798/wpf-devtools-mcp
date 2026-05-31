using System.Text.Json;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles aggregated element snapshot requests.
/// </summary>
public sealed class ElementSnapshotHandlers : IRequestHandler
{
    private readonly IElementSnapshotAggregator _aggregator;

    internal ElementSnapshotHandlers(IElementSnapshotAggregator aggregator)
    {
        _aggregator = aggregator;
    }

    /// <summary>
    /// Gets the inspector method names supported by this handler.
    /// </summary>
    public IEnumerable<string> GetSupportedMethods() => ["get_element_snapshot"];

    /// <summary>
    /// Handles an aggregated element snapshot request.
    /// </summary>
    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "get_element_snapshot" => await _aggregator.GetElementSnapshotAsync(@params, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }
}
