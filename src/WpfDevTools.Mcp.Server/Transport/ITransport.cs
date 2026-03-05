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
    /// Event raised when a message is received
    /// </summary>
    event EventHandler<MessageReceivedEventArgs>? MessageReceived;
}

/// <summary>
/// Event args for message received event
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
