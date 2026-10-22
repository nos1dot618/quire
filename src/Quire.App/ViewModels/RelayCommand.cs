using System.Windows.Input;

namespace Quire.ViewModels;

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();
}

/// <summary>A command whose parameter comes from the binding, such as the element a context menu was opened on.</summary>
public sealed class RelayCommand<T>(Action<T> execute, Func<T, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => TryGet(parameter, out var value) && (canExecute?.Invoke(value) ?? true);

    public void Execute(object? parameter)
    {
        if (TryGet(parameter, out var value))
            execute(value);
    }

    // A null parameter is accepted when T allows null, such as object? or a reference type.
    private static bool TryGet(object? parameter, out T value)
    {
        if (parameter is T typed)
        {
            value = typed;
            return true;
        }
        value = default!;
        return parameter is null && default(T) is null;
    }
}
