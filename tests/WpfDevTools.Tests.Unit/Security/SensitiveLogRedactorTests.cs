using FluentAssertions;
using WpfDevTools.Shared.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Security;

public sealed class SensitiveLogRedactorTests
{
    [Fact]
    public void Redact_ShouldRemoveKnownSensitiveRuntimeValues()
    {
        var message =
            "WPFDEVTOOLS_AUTH_SECRET=plain-secret password=cert-password " +
            "\"windowTitle\":\"Payroll - Alice\" " +
            "\"base64Image\":\"AAAA\" " +
            "\"propertyValue\":\"123-45-6789\" " +
            @"C:\Users\dev\AppData\Local\Temp\WpfDevTools_AuthSecret_1234_abcd.txt";

        var redacted = SensitiveLogRedactor.Redact(message);

        redacted.Should().NotContain("plain-secret");
        redacted.Should().NotContain("cert-password");
        redacted.Should().NotContain("Payroll - Alice");
        redacted.Should().NotContain("AAAA");
        redacted.Should().NotContain("123-45-6789");
        redacted.Should().NotContain("WpfDevTools_AuthSecret_1234_abcd.txt");
        redacted.Should().Contain(SensitiveLogRedactor.RedactedValue);
    }
}
