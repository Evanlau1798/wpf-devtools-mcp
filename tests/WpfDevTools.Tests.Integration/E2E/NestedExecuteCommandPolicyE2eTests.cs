using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Tests.Integration.TestSupport;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class NestedExecuteCommandPolicyE2eTests : IAsyncLifetime, IDisposable
{
    private McpStdioClient? _client;
    private Process? _testApp;
    private string? _serverExePath;
    private string? _testAppExePath;
    private string? _authSecret;
    private string? _certDirectory;

    [Fact]
    public async Task BatchMutate_WithNestedExecuteCommand_WhenViewModelInspectionDisabled_ShouldReturnSecurityError()
    {
        var nameTextBoxId = await FindElementByNameAsync("NameTextBox");
        var controlValue = $"control-{Guid.NewGuid():N}";

        var allowedResult = await Client.CallToolAsync(
            "batch_mutate",
            new
            {
                processId = TestAppProcessId,
                mutations = new[]
                {
                    new
                    {
                        tool = "set_dp_value",
                        args = new
                        {
                            elementId = nameTextBoxId,
                            propertyName = "Text",
                            value = controlValue
                        }
                    }
                },
                navigation = false
            },
            timeoutMs: 10000);

        allowedResult.GetProperty("success").GetBoolean().Should().BeTrue(
            "batch_mutate itself should remain allowed when the nested mutation does not use viewmodel inspection");
        await E2eTestHelpers.WaitForDpValueAsync(
            Client,
            TestAppProcessId,
            nameTextBoxId,
            "Text",
            controlValue,
            TimeSpan.FromSeconds(5));

        var result = await Client.CallToolAsync(
            "batch_mutate",
            new
            {
                processId = TestAppProcessId,
                mutations = new[]
                {
                    new
                    {
                        tool = "execute_command",
                        args = new
                        {
                            elementId = nameTextBoxId,
                            commandName = "ResetStateCommand"
                        }
                    }
                },
                navigation = false
            },
            timeoutMs: 10000);

        AssertViewModelPolicyDenied(result, "batch_mutate");
        await E2eTestHelpers.WaitForDpValueAsync(
            Client,
            TestAppProcessId,
            nameTextBoxId,
            "Text",
            controlValue,
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitForDpChangeAfterMutation_WithNestedExecuteCommand_WhenViewModelInspectionDisabled_ShouldReturnSecurityError()
    {
        var nameTextBoxId = await FindElementByNameAsync("NameTextBox");

        var controlValue = $"control-{Guid.NewGuid():N}";
        var allowedResult = await Client.CallToolAsync(
            "wait_for_dp_change_after_mutation",
            new
            {
                processId = TestAppProcessId,
                elementId = nameTextBoxId,
                propertyName = "Text",
                expectedValue = controlValue,
                timeoutMs = 4000,
                pollIntervalMs = 100,
                triggerMutation = new
                {
                    tool = "set_dp_value",
                    args = new
                    {
                        propertyName = "Text",
                        value = controlValue
                    }
                },
                navigation = false
            },
            timeoutMs: 10000);

        allowedResult.GetProperty("success").GetBoolean().Should().BeTrue(
            "wait_for_dp_change_after_mutation itself should remain allowed when triggerMutation does not use viewmodel inspection");
        allowedResult.GetProperty("completionReason").GetString().Should().Be("ExpectedValueReached");

        var result = await Client.CallToolAsync(
            "wait_for_dp_change_after_mutation",
            new
            {
                processId = TestAppProcessId,
                elementId = nameTextBoxId,
                propertyName = "Text",
                expectedValue = $"policy-blocked-{Guid.NewGuid():N}",
                timeoutMs = 1000,
                pollIntervalMs = 100,
                triggerMutation = new
                {
                    tool = "execute_command",
                    args = new
                    {
                        commandName = "ResetStateCommand"
                    }
                },
                navigation = false
            },
            timeoutMs: 10000);

        AssertViewModelPolicyDenied(result, "wait_for_dp_change_after_mutation");
        await E2eTestHelpers.WaitForDpValueAsync(
            Client,
            TestAppProcessId,
            nameTextBoxId,
            "Text",
            controlValue,
            TimeSpan.FromSeconds(5));
    }

    public async Task InitializeAsync()
    {
        _serverExePath = TryFindServerExe();
        if (_serverExePath == null)
        {
            throw new InvalidOperationException(
                "MCP Server executable not found for the current test configuration. Build src/WpfDevTools.Mcp.Server first.");
        }

        _testAppExePath = TryFindTestAppExe();
        if (_testAppExePath == null)
        {
            throw new InvalidOperationException(
                "TestApp executable not found for the current test configuration. Build tests/WpfDevTools.Tests.TestApp first.");
        }

        if (!BootstrapperArtifactLocator.HasNativeBootstrapper(AppContext.BaseDirectory))
        {
            throw new InvalidOperationException(
                "Native bootstrapper DLLs not found. Build src/WpfDevTools.Bootstrapper first.");
        }

        _authSecret = CreateAuthSecret();
        _certDirectory = CreateCertificateDirectoryPath();

        try
        {
            Directory.CreateDirectory(_certDirectory);
            _testApp = TestAppProcessLauncher.StartAndWaitForMainWindow(
                _testAppExePath,
                TimeSpan.FromSeconds(15),
                CreateIsolatedTempEnvironment(_certDirectory));

            _client = new McpStdioClient();
            await _client.StartAsync(
                _serverExePath,
                CreateLockedDownServerEnvironment(_testAppExePath, _authSecret, _certDirectory));

            var connectResult = await _client.CallToolAsync(
                "connect",
                new { processId = _testApp.Id },
                timeoutMs: 90000);

            connectResult.GetProperty("success").GetBoolean().Should().BeTrue();
        }
        catch (Exception ex)
        {
            var stderr = _client?.ServerStderr ?? string.Empty;
            Dispose();
            throw new InvalidOperationException(
                $"Nested execute_command policy E2E setup failed: {ex.Message}\n---STDERR---\n{stderr}",
                ex);
        }
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;

        if (_testApp != null)
        {
            try
            {
                if (!_testApp.HasExited)
                {
                    _testApp.CloseMainWindow();
                    if (!_testApp.WaitForExit(3000))
                    {
                        _testApp.Kill();
                        _testApp.WaitForExit(3000);
                    }
                }
            }
            catch
            {
                try
                {
                    _testApp.Kill();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
            finally
            {
                _testApp.Dispose();
                _testApp = null;
            }
        }

        DeleteDirectoryBestEffort(_certDirectory);
    }

    private McpStdioClient Client => _client
        ?? throw new InvalidOperationException("MCP client is not initialized.");

    private int TestAppProcessId => _testApp?.Id
        ?? throw new InvalidOperationException("TestApp is not running.");

    private async Task<string> FindElementByNameAsync(string elementName)
    {
        var elementId = await E2eTestHelpers.FindElementByNameAsync(Client, TestAppProcessId, elementName);
        elementId.Should().NotBeNullOrWhiteSpace($"TestApp should expose {elementName}.");
        return elementId!;
    }

    private static void AssertViewModelPolicyDenied(JsonElement result, string toolName)
    {
        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("SecurityError");
        result.GetProperty("error").GetString().Should().Contain(toolName);
        result.GetProperty("hint").GetString().Should().Contain(McpServerConfiguration.AllowViewModelInspectionEnvVar);
        result.GetProperty("suggestedAction").GetString().Should().Contain(McpServerConfiguration.AllowViewModelInspectionEnvVar);
    }

    private static IReadOnlyDictionary<string, string> CreateLockedDownServerEnvironment(
        string testAppExePath,
        string authSecret,
        string certDirectory)
    {
        var environment = new Dictionary<string, string>(McpE2eFixture.CreateServerEnvironment(
            testAppExePath,
            authSecret,
            certDirectory));

        environment[McpServerConfiguration.AllowViewModelInspectionEnvVar] = "false";
        return environment;
    }

    private static IReadOnlyDictionary<string, string> CreateIsolatedTempEnvironment(string tempDirectory)
        => new Dictionary<string, string>
        {
            ["TEMP"] = tempDirectory,
            ["TMP"] = tempDirectory
        };

    private static string CreateAuthSecret()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string CreateCertificateDirectoryPath()
        => Path.Combine(
            ReleasePackagingTestHarness.GetRepoFilePath("tmp"),
            "WpfDevTools.NestedPolicyE2e." + Guid.NewGuid().ToString("N"));

    private static void DeleteDirectoryBestEffort(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                ReleasePackagingTestHarness.DeleteDirectory(path);
            }
        }
        catch
        {
            // Best-effort cleanup; child processes may still be releasing files.
        }
    }

    private static string? TryFindServerExe()
        => IntegrationExecutableLocator.FindExecutable(
            AppContext.BaseDirectory,
            "src",
            "WpfDevTools.Mcp.Server",
            "net8.0",
            "WpfDevTools.Mcp.Server.exe");

    private static string? TryFindTestAppExe()
        => IntegrationExecutableLocator.FindExecutable(
            AppContext.BaseDirectory,
            "tests",
            "WpfDevTools.Tests.TestApp",
            "net8.0-windows",
            "WpfDevTools.Tests.TestApp.exe");
}
