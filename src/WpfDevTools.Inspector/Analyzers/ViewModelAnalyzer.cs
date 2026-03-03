using System.ComponentModel;
using System.Reflection;
using System.Windows;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF ViewModels
/// </summary>
public class ViewModelAnalyzer
{
    /// <summary>
    /// Get ViewModel from element's DataContext
    /// </summary>
    public object GetViewModel(string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetViewModel(elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        // Get DataContext
        if (element is not FrameworkElement fe)
        {
            return new { error = "Element is not a FrameworkElement" };
        }

        var dataContext = fe.DataContext;
        if (dataContext == null)
        {
            return new { error = "DataContext is null" };
        }

        // Analyze ViewModel
        var viewModelType = dataContext.GetType();
        var properties = GetViewModelProperties(dataContext);
        var implementsINotifyPropertyChanged = dataContext is INotifyPropertyChanged;

        return new
        {
            typeName = viewModelType.FullName,
            typeNameShort = viewModelType.Name,
            implementsINotifyPropertyChanged,
            properties
        };
    }

    private DependencyObject? GetRootElement()
    {
        return Application.Current?.MainWindow;
    }

    private DependencyObject? FindElementById(string elementId)
    {
        // TODO: Implement element lookup by ID
        return GetRootElement();
    }

    private List<object> GetViewModelProperties(object viewModel)
    {
        var properties = new List<object>();
        var type = viewModel.GetType();

        // Get all public instance properties
        var propertyInfos = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in propertyInfos)
        {
            try
            {
                var value = prop.GetValue(viewModel);
                properties.Add(new
                {
                    name = prop.Name,
                    typeName = prop.PropertyType.Name,
                    value = value?.ToString(),
                    canRead = prop.CanRead,
                    canWrite = prop.CanWrite
                });
            }
            catch (Exception ex)
            {
                // If property getter throws, include error info
                properties.Add(new
                {
                    name = prop.Name,
                    typeName = prop.PropertyType.Name,
                    error = ex.Message,
                    canRead = prop.CanRead,
                    canWrite = prop.CanWrite
                });
            }
        }

        return properties;
    }
}
