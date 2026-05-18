using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

[CollectionDefinition("AnalyzerStaticState", DisableParallelization = true)]
public sealed class AnalyzerStaticStateCollection : ICollectionFixture<AnalyzerStaticStateFixture>
{
}

public sealed class AnalyzerStaticStateFixture : IDisposable
{
    public AnalyzerStaticStateFixture()
    {
        ResetAll();
    }

    public void Dispose()
    {
        ResetAll();
    }

    internal static void ResetAll()
    {
        PerformanceAnalyzer.StopMonitoring();
        PerformanceAnalyzer.ResetMonitoring();
        PerformanceAnalyzer.ClearTrackedBindings();
        PerformanceAnalyzer.ResetForcedGcPathExecutionCount();
        DependencyPropertyAnalyzer.StopAllWatchers();
        DependencyPropertyAnalyzer.ResetMonitoring();
        DispatcherAnalyzerBase.ResetDependencyPropertyLookupDiagnostics();
        ElementFinder.ResetIdsForTests();
    }
}
