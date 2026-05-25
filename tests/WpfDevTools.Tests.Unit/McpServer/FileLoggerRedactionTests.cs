using FluentAssertions;
using WpfDevTools.Shared.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("TimingSensitive")]
public sealed class FileLoggerRedactionTests : IDisposable
{
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), $"redaction_{Guid.NewGuid():N}.log");

    public void Dispose()
    {
        TryDelete(_logPath);
        TryDelete(_logPath + ".old");
    }

    [Fact]
    public async Task LogStructured_ShouldRedactSensitiveContextValues()
    {
        await using var logger = new FileLogger(_logPath);

        logger.LogStructured("INFO", "windowTitle=Payroll - Alice", new
        {
            authSecret = "plain-secret",
            base64Image = "AAAA",
            propertyValue = "123-45-6789",
            authSecretFile = @"C:\Temp\WpfDevTools_AuthSecret_1234_abcd.txt"
        });

        await logger.DisposeAsync();
        var content = File.ReadAllText(_logPath);

        content.Should().NotContain("Payroll - Alice");
        content.Should().NotContain("plain-secret");
        content.Should().NotContain("AAAA");
        content.Should().NotContain("123-45-6789");
        content.Should().NotContain("WpfDevTools_AuthSecret_1234_abcd.txt");
        content.Should().Contain(SensitiveLogRedactor.RedactedValue);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
