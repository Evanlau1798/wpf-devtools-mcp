using System.Windows;
using System.Windows.Threading;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class DependencyPropertyAnalyzer
{
    private T InvokeSnapshotRead<T>(DependencyObject depObj, bool settleBindings, Func<T> action)
    {
        if (!settleBindings)
        {
            return InvokeOnDispatcher(depObj.Dispatcher, action);
        }

        var dispatcher = depObj.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            return action();
        }

        var cancellationToken = DispatcherRequestContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();

        return dispatcher.Invoke(
            action,
            DispatcherPriority.Background,
            cancellationToken,
            InspectorConfig.UIThreadTimeout);
    }
}
