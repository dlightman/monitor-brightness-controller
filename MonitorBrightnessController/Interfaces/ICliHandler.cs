namespace MonitorBrightnessController.Interfaces;

/// <summary>
/// Parses command-line arguments and executes the corresponding brightness or profile
/// operations, returning a process exit code.
/// </summary>
public interface ICliHandler
{
    /// <summary>
    /// Parses and executes the supplied command-line arguments.
    /// </summary>
    /// <param name="args">The raw command-line arguments.</param>
    /// <returns>The process exit code: 0 on success, non-zero on failure.</returns>
    int Execute(string[] args);
}
