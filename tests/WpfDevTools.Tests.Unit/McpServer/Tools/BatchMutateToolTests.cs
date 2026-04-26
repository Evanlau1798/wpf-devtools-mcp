using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class BatchMutateToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithSnapshotAndDiff_ShouldExecuteMutationsSequentiallyAndReturnPerMutationResults()
    {
        var executedTools = new List<string>();
        var tool = new BatchMutateTool(
            new SessionManager(),
            (toolName, args, _) =>
            {
                executedTools.Add(toolName);
                return Task.FromResult<object>(new
                {
                    success = true,
                    tool = toolName,
                    propertyName = args.GetProperty("propertyName").GetString(),
                    newValue = args.GetProperty("value").ToString()
                });
            },
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                snapshotId = "snapshot_batch_1"
            }),
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                snapshotId = "snapshot_batch_1",
                trigger = "batch_mutate",
                propertyChanges = new[] { new { propertyName = "Text" } },
                viewModelChanges = new[] { new { propertyName = "Name" }, new { propertyName = "Age" } },
                newBindingErrors = Array.Empty<object>(),
                resolvedBindingErrors = Array.Empty<object>(),
                validationChanges = Array.Empty<object>(),
                focusChange = (object?)null
            }));

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId = 12345,
                captureSnapshot = new
                {
                    propertyNames = new[] { "Text" },
                    viewModelPropertyNames = new[] { "Name", "Age" }
                },
                includeDiff = true,
                mutations = new object[]
                {
                    new { tool = "modify_viewmodel", args = new { propertyName = "Name", value = "Batch User" } },
                    new { tool = "modify_viewmodel", args = new { propertyName = "Age", value = 31 } }
                }
            }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("snapshotId").GetString().Should().Be("snapshot_batch_1");
        result.GetProperty("mutationCount").GetInt32().Should().Be(2);
        result.GetProperty("executedMutationCount").GetInt32().Should().Be(2);
        result.GetProperty("successfulMutationCount").GetInt32().Should().Be(2);
        result.GetProperty("failedMutationCount").GetInt32().Should().Be(0);
        result.GetProperty("skippedMutationCount").GetInt32().Should().Be(0);
        result.GetProperty("stateDiff").GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("stateDiff").GetProperty("viewModelChanges").GetArrayLength().Should().Be(2);
        result.GetProperty("mutations")[0].GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("mutations")[1].GetProperty("success").GetBoolean().Should().BeTrue();
        executedTools.Should().Equal("modify_viewmodel", "modify_viewmodel");
    }

    [Fact]
    public async Task ExecuteAsync_WhenMutationFails_ShouldStopRemainingMutationsAndReturnRollbackGuidance()
    {
        var executedTools = new List<string>();
        var tool = new BatchMutateTool(
            new SessionManager(),
            (toolName, args, _) =>
            {
                executedTools.Add(toolName);
                var propertyName = args.GetProperty("propertyName").GetString();
                return Task.FromResult<object>(propertyName switch
                {
                    "Name" => new { success = true, propertyName, newValue = "Updated" },
                    "Age" => new { success = false, error = "Setter failed.", errorCode = "OperationFailed" },
                    _ => throw new InvalidOperationException("Skipped mutation should not execute")
                });
            },
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                snapshotId = "snapshot_batch_rollback"
            }),
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                snapshotId = "snapshot_batch_rollback",
                trigger = "batch_mutate",
                propertyChanges = Array.Empty<object>(),
                viewModelChanges = Array.Empty<object>(),
                newBindingErrors = Array.Empty<object>(),
                resolvedBindingErrors = Array.Empty<object>(),
                validationChanges = Array.Empty<object>(),
                focusChange = (object?)null
            }));

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId = 12345,
                captureSnapshot = new
                {
                    viewModelPropertyNames = new[] { "Name", "Age", "Title" }
                },
                mutations = new object[]
                {
                    new { tool = "modify_viewmodel", args = new { propertyName = "Name", value = "Updated" } },
                    new { tool = "modify_viewmodel", args = new { propertyName = "Age", value = 32 } },
                    new { tool = "modify_viewmodel", args = new { propertyName = "Title", value = "Skipped" } }
                }
            }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("executedMutationCount").GetInt32().Should().Be(2);
        result.GetProperty("successfulMutationCount").GetInt32().Should().Be(1);
        result.GetProperty("failedMutationCount").GetInt32().Should().Be(1);
        result.GetProperty("skippedMutationCount").GetInt32().Should().Be(1);
        result.GetProperty("errorCode").GetString().Should().Be("BatchStepFailed");
        result.GetProperty("error").GetString().Should().Contain("Age");
        result.GetProperty("recovery").GetProperty("tool").GetString().Should().Be("restore_state_snapshot");
        result.GetProperty("recovery").GetProperty("params").GetProperty("snapshotId").GetString().Should().Be("snapshot_batch_rollback");
        result.GetProperty("mutations")[2].GetProperty("skipped").GetBoolean().Should().BeTrue();
        result.GetProperty("rollback").GetProperty("available").GetBoolean().Should().BeTrue();
        result.GetProperty("rollback").GetProperty("tool").GetString().Should().Be("restore_state_snapshot");
        result.GetProperty("rollback").GetProperty("params").GetProperty("snapshotId").GetString().Should().Be("snapshot_batch_rollback");
        executedTools.Should().Equal("modify_viewmodel", "modify_viewmodel");
    }

    [Fact]
    public async Task ExecuteAsync_WhenStateDiffFails_ShouldReturnRollbackRecovery()
    {
        var tool = new BatchMutateTool(
            new SessionManager(),
            (_, _, _) => Task.FromResult<object>(new { success = true }),
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                snapshotId = "snapshot_batch_diff_failure"
            }),
            (_, _) => Task.FromResult<object>(new
            {
                success = false,
                error = "Diff collection failed.",
                errorCode = "OperationFailed"
            }));

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId = 12345,
                captureSnapshot = new
                {
                    propertyNames = new[] { "Text" }
                },
                includeDiff = true,
                mutations = new object[]
                {
                    new { tool = "set_dp_value", args = new { propertyName = "Text", value = "Updated" } }
                }
            }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("failedMutationCount").GetInt32().Should().Be(0);
        result.GetProperty("errorCode").GetString().Should().Be("DiffFailed");
        result.GetProperty("error").GetString().Should().Contain("get_state_diff");
        result.GetProperty("rollback").GetProperty("available").GetBoolean().Should().BeTrue();
        result.GetProperty("recovery").GetProperty("tool").GetString().Should().Be("restore_state_snapshot");
        result.GetProperty("recovery").GetProperty("params").GetProperty("snapshotId").GetString().Should().Be("snapshot_batch_diff_failure");
    }

    [Fact]
    public async Task ExecuteAsync_WhenMutationIsCanceledAfterSnapshot_ShouldReturnPartialStateAndRollbackRecovery()
    {
        using var cancellation = new CancellationTokenSource();
        var executedProperties = new List<string?>();
        var tool = new BatchMutateTool(
            new SessionManager(),
            (_, args, token) =>
            {
                var propertyName = args.GetProperty("propertyName").GetString();
                executedProperties.Add(propertyName);
                if (propertyName == "Age")
                {
                    cancellation.Cancel();
                    throw new OperationCanceledException(token);
                }

                return Task.FromResult<object>(new { success = true, propertyName });
            },
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                snapshotId = "snapshot_batch_cancel"
            }),
            null);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId = 12345,
                captureSnapshot = new
                {
                    viewModelPropertyNames = new[] { "Name", "Age", "Title" }
                },
                mutations = new object[]
                {
                    new { tool = "modify_viewmodel", args = new { propertyName = "Name", value = "Updated" } },
                    new { tool = "modify_viewmodel", args = new { propertyName = "Age", value = 32 } },
                    new { tool = "modify_viewmodel", args = new { propertyName = "Title", value = "Skipped" } }
                }
            }),
            cancellation.Token));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("Timeout");
        result.GetProperty("executedMutationCount").GetInt32().Should().Be(2);
        result.GetProperty("successfulMutationCount").GetInt32().Should().Be(1);
        result.GetProperty("failedMutationCount").GetInt32().Should().Be(1);
        result.GetProperty("skippedMutationCount").GetInt32().Should().Be(1);
        result.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
        result.GetProperty("rollback").GetProperty("available").GetBoolean().Should().BeTrue();
        result.GetProperty("recovery").GetProperty("tool").GetString().Should().Be("restore_state_snapshot");
        result.GetProperty("mutations")[1].GetProperty("errorCode").GetString().Should().Be("Timeout");
        result.GetProperty("mutations")[2].GetProperty("skipped").GetBoolean().Should().BeTrue();
        executedProperties.Should().Equal("Name", "Age");
    }

    [Fact]
    public async Task ExecuteAsync_WithIncludeDiffWithoutCaptureSnapshot_ShouldReturnStructuredError()
    {
        var tool = new BatchMutateTool(new SessionManager());

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId = 12345,
                includeDiff = true,
                mutations = new object[]
                {
                    new { tool = "modify_viewmodel", args = new { propertyName = "Name", value = "Batch User" } }
                }
            }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Contain("captureSnapshot");
    }

    [Fact]
    public async Task ExecuteAsync_WithNestedMutationProcessId_ShouldRejectPayloadBeforeExecution()
    {
        var mutationCalled = false;
        var tool = new BatchMutateTool(
            new SessionManager(),
            (_, _, _) =>
            {
                mutationCalled = true;
                return Task.FromResult<object>(new { success = true });
            },
            null,
            null);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId = 12345,
                mutations = new object[]
                {
                    new
                    {
                        tool = "modify_viewmodel",
                        args = new
                        {
                            processId = 54321,
                            propertyName = "Name",
                            value = "Unsafe"
                        }
                    }
                }
            }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        result.GetProperty("error").GetString().Should().Contain("processId");
        result.GetProperty("error").GetString().Should().Contain("mutation");
        mutationCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithNestedCaptureSnapshotProcessId_ShouldRejectPayloadBeforeSnapshot()
    {
        var snapshotCalled = false;
        var tool = new BatchMutateTool(
            new SessionManager(),
            (_, _, _) => Task.FromResult<object>(new { success = true }),
            (_, _) =>
            {
                snapshotCalled = true;
                return Task.FromResult<object>(new { success = true, snapshotId = "unsafe" });
            },
            null);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId = 12345,
                captureSnapshot = new
                {
                    processId = 54321,
                    propertyNames = new[] { "Text" }
                },
                mutations = new object[]
                {
                    new
                    {
                        tool = "modify_viewmodel",
                        args = new
                        {
                            propertyName = "Name",
                            value = "Unsafe"
                        }
                    }
                }
            }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        result.GetProperty("error").GetString().Should().Contain("processId");
        result.GetProperty("error").GetString().Should().Contain("captureSnapshot");
        snapshotCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithStringifiedMutationsArray_ShouldAcceptCompatibilityPayload()
    {
        var executedTools = new List<string>();
        var tool = new BatchMutateTool(
            new SessionManager(),
            (toolName, args, _) =>
            {
                executedTools.Add(toolName);
                return Task.FromResult<object>(new
                {
                    success = true,
                    tool = toolName,
                    elementId = args.GetProperty("elementId").GetString()
                });
            },
            null,
            null);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId = 12345,
                mutations = JsonSerializer.Serialize(new object[]
                {
                    new
                    {
                        tool = "focus_element",
                        args = new
                        {
                            elementId = "TextBox_24"
                        }
                    }
                })
            }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("executedMutationCount").GetInt32().Should().Be(1);
        result.GetProperty("mutations")[0].GetProperty("success").GetBoolean().Should().BeTrue();
        executedTools.Should().Equal("focus_element");
    }

    [Fact]
    public async Task ExecuteAsync_WithStringifiedCaptureSnapshotObject_ShouldAcceptCompatibilityPayload()
    {
        var executedTools = new List<string>();
        var tool = new BatchMutateTool(
            new SessionManager(),
            (toolName, args, _) =>
            {
                executedTools.Add(toolName);
                return Task.FromResult<object>(new
                {
                    success = true,
                    tool = toolName,
                    propertyName = args.GetProperty("propertyName").GetString(),
                    newValue = args.GetProperty("value").ToString()
                });
            },
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                snapshotId = "snapshot_batch_compat"
            }),
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                snapshotId = "snapshot_batch_compat",
                trigger = "batch_mutate",
                propertyChanges = new[] { new { propertyName = "Text" } },
                viewModelChanges = Array.Empty<object>(),
                newBindingErrors = Array.Empty<object>(),
                resolvedBindingErrors = Array.Empty<object>(),
                validationChanges = Array.Empty<object>(),
                focusChange = (object?)null
            }));

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId = 12345,
                captureSnapshot = JsonSerializer.Serialize(new
                {
                    propertyNames = new[] { "Text" }
                }),
                includeDiff = true,
                mutations = new object[]
                {
                    new { tool = "modify_viewmodel", args = new { propertyName = "Name", value = "Batch User" } }
                }
            }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("snapshotId").GetString().Should().Be("snapshot_batch_compat");
        result.GetProperty("stateDiff").GetProperty("success").GetBoolean().Should().BeTrue();
        executedTools.Should().Equal("modify_viewmodel");
    }
}
