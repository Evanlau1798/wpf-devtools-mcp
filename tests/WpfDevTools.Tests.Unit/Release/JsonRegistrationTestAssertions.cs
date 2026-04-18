using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;

namespace WpfDevTools.Tests.Unit.Release;

internal static class JsonRegistrationTestAssertions
{
    public static void SeedRegistrationFile(string path, string containerPropertyName, string registrationName, string command)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var payload = new Dictionary<string, object?>
        {
            [containerPropertyName] = new Dictionary<string, object?>
            {
                [registrationName] = new Dictionary<string, object?>
                {
                    ["type"] = "stdio",
                    ["command"] = command,
                    ["args"] = Array.Empty<string>()
                }
            }
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload));
    }

    public static void AssertRegistrationAbsent(string path, string containerPropertyName, string registrationName)
    {
        var raw = File.ReadAllText(path);
        using var document = JsonDocument.Parse(raw);
        document.RootElement.TryGetProperty(containerPropertyName, out var registrations).Should().BeTrue(
            $"{path} should keep the {containerPropertyName} container after removing {registrationName}. Actual content: {raw}");

        registrations.TryGetProperty(registrationName, out _).Should().BeFalse(
            $"{path} should not keep the removed {registrationName} registration");
    }

    public static void AssertRegistrationPresent(string path, string containerPropertyName, string registrationName)
    {
        var raw = File.ReadAllText(path);
        using var document = JsonDocument.Parse(raw);
        document.RootElement.TryGetProperty(containerPropertyName, out var registrations).Should().BeTrue(
            $"{path} should expose the {containerPropertyName} container. Actual content: {raw}");

        registrations.TryGetProperty(registrationName, out _).Should().BeTrue(
            $"{path} should still contain the {registrationName} registration");
    }

    public static void AssertRegistrationCommand(string path, string containerPropertyName, string registrationName, string expectedCommand)
    {
        var raw = File.ReadAllText(path);
        using var document = JsonDocument.Parse(raw);
        if (!document.RootElement.TryGetProperty(containerPropertyName, out var registrations))
        {
            Execute.Assertion.FailWith(
                "Expected {0} to expose the {1} container, but the file content was {2}.",
                path,
                containerPropertyName,
                raw);
        }

        registrations.GetProperty(registrationName).GetProperty("command").GetString().Should().Be(expectedCommand);
    }
}