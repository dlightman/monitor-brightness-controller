using System;
using Microsoft.Win32;
using MonitorBrightnessController.Interfaces;

namespace MonitorBrightnessController.Infrastructure;

/// <summary>
/// Wraps a <see cref="RegistryKey"/> to implement <see cref="IRegistryKeyWrapper"/>,
/// enabling testability by abstracting direct registry access.
/// </summary>
public sealed class RegistryKeyWrapper : IRegistryKeyWrapper
{
    private readonly RegistryKey _key;
    private readonly bool _ownsKey;

    /// <summary>
    /// Creates a wrapper around the specified registry key.
    /// </summary>
    /// <param name="key">The registry key to wrap.</param>
    /// <param name="ownsKey">
    /// When true, <see cref="Dispose"/> will close the underlying key.
    /// Pass false for root keys like <see cref="Registry.CurrentUser"/> that should not be disposed.
    /// </param>
    public RegistryKeyWrapper(RegistryKey key, bool ownsKey = false)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _ownsKey = ownsKey;
    }

    /// <inheritdoc />
    public IRegistryKeyWrapper? OpenSubKey(string subKey, bool writable)
    {
        var opened = _key.OpenSubKey(subKey, writable);
        return opened is not null ? new RegistryKeyWrapper(opened, ownsKey: true) : null;
    }

    /// <inheritdoc />
    public void SetValue(string name, object value) => _key.SetValue(name, value);

    /// <inheritdoc />
    public void DeleteValue(string name, bool throwOnMissingValue) => _key.DeleteValue(name, throwOnMissingValue);

    /// <inheritdoc />
    public object? GetValue(string name) => _key.GetValue(name);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsKey)
        {
            _key.Dispose();
        }
    }
}
