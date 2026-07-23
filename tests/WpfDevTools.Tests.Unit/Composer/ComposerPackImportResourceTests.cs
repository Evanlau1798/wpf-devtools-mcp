using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPackImportResourceTests
{
    [Fact]
    public void CreateDryRunPlan_WhenCompressedArchiveExceedsLimit_ShouldRejectBeforeParsing()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(tempRoot, "oversized.bin");
            File.WriteAllBytes(archivePath, [0x01, 0x02]);

            var act = () => PackImportService.CreateDryRunPlan(
                archivePath,
                Path.Combine(tempRoot, "packs"),
                new PackImportLimits(MaxArchiveBytes: 1));

            act.Should().Throw<InvalidDataException>()
                .WithMessage("*compressed size*");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateDryRunPlanAsync_WhenHashReadIsCancelled_ShouldStopPromptly()
    {
        await using var archiveStream = new CancellationBlockingStream();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var act = async () => await PackImportService.CreateDryRunPlanAsync(
            archiveStream,
            Path.Combine(Path.GetTempPath(), "unused-packs"),
            new PackImportLimits(MaxArchiveBytes: 1),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-import-resource-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CancellationBlockingStream : MemoryStream
    {
        public CancellationBlockingStream()
            : base([0x01])
        {
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
