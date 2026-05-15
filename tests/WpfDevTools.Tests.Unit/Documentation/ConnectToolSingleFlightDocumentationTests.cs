using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class ConnectToolSingleFlightDocumentationTests
{
    [Fact]
    public void SingleFlightBehavior_ShouldBeDocumentedInSourceAndToolReference()
    {
        var source = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Mcp.Server/Tools/ConnectTool.SingleFlight.cs"));
        var englishReference = File.ReadAllText(GetRepoFilePath(
            "docfx/reference/tools/process-and-connection.md"));
        var chineseReference = File.ReadAllText(GetRepoFilePath(
            "docfx/zh-tw/reference/tools/process-and-connection.md"));

        source.Should().Contain("same SessionManager and processId");
        source.Should().Contain("share the in-flight connect operation");
        source.Should().Contain("result or exception");
        source.Should().Contain("Caller cancellation removes only that waiter");
        source.Should().Contain("last waiter cancels");
        source.Should().Contain("not cached");
        source.Should().Contain("AlreadyConnected");
        source.Should().Contain("wait for settlement");
        source.Should().Contain("retry");

        englishReference.Should().Contain("Concurrent `connect` calls for the same `SessionManager` and `processId` share one in-flight operation");
        englishReference.Should().Contain("A caller cancellation stops waiting for that caller only");
        englishReference.Should().Contain("Completed single-flight operations are removed; later calls either return `AlreadyConnected` for an existing connected session or start a fresh connect attempt");

        chineseReference.Should().Contain("同一個 `SessionManager` 與 `processId` 的並行 `connect` 會共享同一個 in-flight operation");
        chineseReference.Should().Contain("單一 caller cancellation 只會停止該 caller 等待");
        chineseReference.Should().Contain("完成後的 single-flight operation 會被移除；後續呼叫若已有 connected session 會回傳 `AlreadyConnected`，否則會開始新的 connect 嘗試");
    }

    private static string GetRepoFilePath(string relativePath)
        => TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}