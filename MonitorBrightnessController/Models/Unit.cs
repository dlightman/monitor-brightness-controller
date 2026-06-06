namespace MonitorBrightnessController.Models;

/// <summary>
/// Represents the absence of a meaningful return value, used with
/// <see cref="Result{T}"/> for operations that either succeed or fail
/// without producing a value (e.g. <c>Result&lt;Unit&gt;</c>).
/// </summary>
public readonly struct Unit
{
    /// <summary>The single shared instance of <see cref="Unit"/>.</summary>
    public static readonly Unit Value = default;
}
