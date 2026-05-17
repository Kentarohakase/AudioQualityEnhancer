using System.Windows.Input;

namespace AudioQualityEnhancer.ViewModels;

public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Predicate<T?>? _canExecute;

    public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return parameter is T value
            ? _canExecute?.Invoke(value) ?? true
            : parameter is null && (_canExecute?.Invoke(default) ?? true);
    }

    public void Execute(object? parameter)
    {
        _execute(parameter is T value ? value : default);
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
