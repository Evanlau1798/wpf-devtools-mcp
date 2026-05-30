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

    [Fact]
    public void Redact_ShouldRemoveDefaultDiagnosticIdentifiersAndLocalSecurityPaths()
    {
        var message =
            "WPFDEVTOOLS_CERT_DIR=C:\\Users\\dev\\AppData\\Roaming\\WpfDevTools\\certs " +
            "certDirectory=C:\\Users\\dev\\AppData\\Local\\WpfDevTools\\certs " +
            "targetProcessPath=C:\\Sensitive\\ForzaMusicOverlay.exe " +
            "pipeName=WpfDevTools_Inspector_12345 " +
            "\"screenshotId\":\"screenshot_abcdef\" " +
            "\"resourceUri\":\"wpf://screenshots/screenshot_abcdef\"";

        var redacted = SensitiveLogRedactor.Redact(message);

        redacted.Should().NotContain("WPFDEVTOOLS_CERT_DIR=C:");
        redacted.Should().NotContain("certDirectory=C:");
        redacted.Should().NotContain("ForzaMusicOverlay.exe");
        redacted.Should().NotContain("WpfDevTools_Inspector_12345");
        redacted.Should().NotContain("screenshot_abcdef");
        redacted.Should().Contain(SensitiveLogRedactor.RedactedValue);
    }
}
