using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace WpfDevTools.Tests.TestApp;

/// <summary>
/// Test ViewModel with intentional binding errors and validation
/// </summary>
public class TestViewModel : INotifyPropertyChanged, IDataErrorInfo
{
    private string _firstName = "Ada";
    private string _lastName = "Lovelace";
    private string _name = "";
    private string _searchText = "";
    private int _age;
    private bool _isEnabled = true;
    private bool _isGhostVisible;
    private bool _useBrokenDetailContext;
    private string _lastActionMessage = "Ready";
    private readonly RelayCommand _saveCommand;
    private readonly ValidDetailContext _validDetailContext = new("Detail ready");
    private readonly BrokenDetailContext _brokenDetailContext = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSave));
            _saveCommand.RaiseCanExecuteChanged();
        }
    }

    public string FirstName
    {
        get => _firstName;
        set
        {
            _firstName = value;
            OnPropertyChanged();
        }
    }

    public string LastName
    {
        get => _lastName;
        set
        {
            _lastName = value;
            OnPropertyChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
        }
    }

    public int Age
    {
        get => _age;
        set
        {
            _age = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSave));
            _saveCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsGhostVisible
    {
        get => _isGhostVisible;
        set
        {
            _isGhostVisible = value;
            OnPropertyChanged();
        }
    }

    public bool UseBrokenDetailContext
    {
        get => _useBrokenDetailContext;
        set
        {
            _useBrokenDetailContext = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentDetailContext));
        }
    }

    public object CurrentDetailContext => UseBrokenDetailContext
        ? _brokenDetailContext
        : _validDetailContext;

    public bool CanSave => !string.IsNullOrWhiteSpace(Name) && Age > 0;

    public string LastActionMessage
    {
        get => _lastActionMessage;
        private set
        {
            _lastActionMessage = value;
            OnPropertyChanged();
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand ClearCommand { get; }

    public TestViewModel()
    {
        _saveCommand = new RelayCommand(
            execute: _ => RecordActionMessage($"Saved: {Name}, {Age}"),
            canExecute: _ => CanSave);
        SaveCommand = _saveCommand;

        ClearCommand = new RelayCommand(
            execute: _ =>
            {
                Name = "";
                Age = 0;
                RecordActionMessage("Form cleared");
            },
            canExecute: _ => true);
    }

    public void RecordActionMessage(string message)
    {
        LastActionMessage = message;
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // IDataErrorInfo implementation for validation
    public string Error => string.Empty;

    public string this[string columnName]
    {
        get
        {
            return columnName switch
            {
                nameof(Name) when string.IsNullOrWhiteSpace(Name) => "Name is required",
                nameof(Age) when Age <= 0 => "Age must be greater than 0",
                nameof(Age) when Age > 150 => "Age must be less than 150",
                _ => string.Empty
            };
        }
    }
}

public sealed class ValidDetailContext
{
    public ValidDetailContext(string detailName)
    {
        DetailName = detailName;
        Nested = new ValidNestedDetail($"{detailName} nested", $"{detailName} secondary");
    }

    public string DetailName { get; set; }

    public ValidNestedDetail Nested { get; }
}

public sealed class BrokenDetailContext;

public sealed record ValidNestedDetail(string DetailText, string DetailSecondary);

/// <summary>
/// Simple RelayCommand implementation
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool> _canExecute;

    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Action<object?> execute, Func<object?, bool> canExecute)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute ?? throw new ArgumentNullException(nameof(canExecute));
    }

    public bool CanExecute(object? parameter) => _canExecute(parameter);

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        CommandManager.InvalidateRequerySuggested();
    }
}
