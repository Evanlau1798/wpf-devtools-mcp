using System.Windows;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public class MvvmAnalyzer
{
    private readonly ElementFinder _elementFinder;

    public MvvmAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    public object GetViewModel(string? elementId)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetViewModel(elementId));
        }

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        if (element is not FrameworkElement fe)
        {
            return new { error = "Element is not a FrameworkElement" };
        }

        var dataContext = fe.DataContext;
        if (dataContext == null)
        {
            return new { error = "Element has no DataContext" };
        }

        return new
        {
            success = true,
            type = dataContext.GetType().Name,
            data = dataContext
        };
    }

    public object GetCommands(string? elementId)
    {
        // TODO: Implement
        return new { commands = new object[] { } };
    }

    public object ExecuteCommand(string? elementId, string commandName, object? parameter)
    {
        // TODO: Implement
        return new { success = false, error = "Not implemented" };
    }

    public object GetValidationErrors(string? elementId)
    {
        // TODO: Implement
        return new { errors = new object[] { } };
    }
}
