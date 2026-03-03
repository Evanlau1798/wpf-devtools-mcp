using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Commands
/// </summary>
public class CommandAnalyzer
{
    /// <summary>
    /// Get all commands from element's DataContext
    /// </summary>
    public object GetCommands(string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetCommands(elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (element == null)
        {
            return new { success = false, error = "Element not found" };
        }

        // Get DataContext
        if (element is not FrameworkElement fe)
        {
            return new { success = false, error = "Element is not a FrameworkElement" };
        }

        var dataContext = fe.DataContext;
        if (dataContext == null)
        {
            return new { success = false, error = "DataContext is null" };
        }

        // Find all ICommand properties
        var commands = GetCommandProperties(dataContext);

        return new { commands };
    }

    /// <summary>
    /// Execute a command by name
    /// </summary>
    public object ExecuteCommand(string commandName, object? parameter = null, string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => ExecuteCommand(commandName, parameter, elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (element == null)
        {
            return new { success = false, error = "Element not found" };
        }

        // Get DataContext
        if (element is not FrameworkElement fe)
        {
            return new { success = false, error = "Element is not a FrameworkElement" };
        }

        var dataContext = fe.DataContext;
        if (dataContext == null)
        {
            return new { success = false, error = "DataContext is null" };
        }

        // Find command property
        var type = dataContext.GetType();
        var property = type.GetProperty(commandName, BindingFlags.Public | BindingFlags.Instance);

        if (property == null)
        {
            return new { success = false, error = $"Command '{commandName}' not found" };
        }

        var command = property.GetValue(dataContext) as ICommand;
        if (command == null)
        {
            return new { success = false, error = $"Property '{commandName}' is not an ICommand" };
        }

        // Check CanExecute
        if (!command.CanExecute(parameter))
        {
            return new { success = false, error = "Command.CanExecute returned false" };
        }

        // Execute command
        try
        {
            command.Execute(parameter);
            return new { success = true, message = $"Command '{commandName}' executed successfully" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Command execution failed: {ex.Message}" };
        }
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

    private List<object> GetCommandProperties(object dataContext)
    {
        var commands = new List<object>();
        var type = dataContext.GetType();

        // Get all public instance properties of type ICommand
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (typeof(ICommand).IsAssignableFrom(prop.PropertyType))
            {
                try
                {
                    var command = prop.GetValue(dataContext) as ICommand;
                    if (command != null)
                    {
                        commands.Add(new
                        {
                            name = prop.Name,
                            canExecute = command.CanExecute(null),
                            typeName = prop.PropertyType.Name
                        });
                    }
                }
                catch (Exception ex)
                {
                    commands.Add(new
                    {
                        name = prop.Name,
                        error = ex.Message,
                        typeName = prop.PropertyType.Name
                    });
                }
            }
        }

        return commands;
    }
}
