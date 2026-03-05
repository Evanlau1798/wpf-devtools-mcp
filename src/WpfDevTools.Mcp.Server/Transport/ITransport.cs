namespace WpfDevTools.Mcp.Server.Transport;

/// <summary>
/// Interface for MCP transport layer
/// </summary>
public interface ITransport
{
    /// <summary>
    /// Gets whether the transport is running
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Start the transport
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Task representing the async operation</returns>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stop the transport
    /// </summary>
    /// <returns>Task representing the async operation</returns>
    Task StopAsync();

    /// <summary>
    /// Event raised when a request is received, allowing the handler to set a response
    /// </summary>
    event EventHandler<RequestReceivedEventArgs>? RequestReceived;
}

/// <summary>
/// Event args for request received event with response support
/// </summary>
public class RequestReceivedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the received request message
    /// </summary>
    public string RequestJson { get; }

    /// <summary>
    /// Gets or sets the response JSON to send back to the client.
    /// If null, the transport returns a generic error.
    /// </summary>
    public string? ResponseJson { get; set; }

    /// <summary>
    /// Initializes a new instance of the RequestReceivedEventArgs class
    /// </summary>
    /// <param name="requestJson">The received request message</param>
    public RequestReceivedEventArgs(string requestJson)
    {
        RequestJson = requestJson;
    }
}

/// <summary>
/// Event args for message received event (backward compatible, used by SSE)
/// </summary>
public class MessageReceivedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the received message
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Initializes a new instance of the MessageReceivedEventArgs class
    /// </summary>
    /// <param name="message">The received message</param>
    public MessageReceivedEventArgs(string message)
    {
        Message = message;
    }
}
