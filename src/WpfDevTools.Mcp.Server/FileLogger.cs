namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Simple file-based logger for MCP Server
/// Logs to file instead of stdout to avoid interfering with JSON-RPC communication
/// </summary>
public class FileLogger
{
    private readonly string _logFilePath;
    private readonly object _lock = new();
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public FileLogger(string? logFilePath = null)
    {
        _logFilePath = logFilePath ?? Path.Combine(
            Path.GetTempPath(),
            $"WpfDevTools_McpServer_{DateTime.Now:yyyyMMdd_HHmmss}.log");
    }

    public void LogInfo(string message)
    {
        Log("INFO", message);
    }

    public void LogError(string message)
    {
        Log("ERROR", message);
    }

    public void LogDebug(string message)
    {
        Log("DEBUG", message);
    }

    private void Log(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                RotateIfNeeded();
                var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, logEntry);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Logging failed: {ex.Message}");
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_logFilePath))
            {
                return;
            }

            var fileInfo = new FileInfo(_logFilePath);
            if (fileInfo.Length >= MaxFileSizeBytes)
            {
                var oldPath = _logFilePath + ".old";
                // Delete existing .old file if present
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }
                File.Move(_logFilePath, oldPath);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Logging failed: {ex.Message}");
        }
    }

    public string LogFilePath => _logFilePath;
}
