using System;
using System.Windows.Input;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// A minimal <see cref="ICommand"/> implementation that delegates execution to an <see cref="Action"/>
/// and optionally evaluates a <see cref="Func{Boolean}"/> to determine availability.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>
    /// Creates a new relay command.
    /// </summary>
    /// <param name="execute">The action to execute when the command is invoked.</param>
    /// <param name="canExecute">An optional predicate that determines whether the command can execute.</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <inheritdoc />
    public void Execute(object? parameter) => _execute();

    /// <summary>
    /// Raises <see cref="CanExecuteChanged"/> to signal the UI to re-evaluate <see cref="CanExecute"/>.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
