using System.Text.Json;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Interface for request handlers
/// </summary>
public interface IRequestHandler
{
    /// <summary>
    /// Get the method names this handler supports
    /// </summary>
    IEnumerable<string> GetSupportedMethods();

    /// <summary>
    /// Handle the request
    /// </summary>
    Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken);
}
