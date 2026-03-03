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
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stop the transport
    /// </summary>
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
    public string Message { get; }

    public MessageReceivedEventArgs(string message)
    {
        Message = message;
    }
}
