using System;

namespace MonitorBrightnessController.Interfaces;

/// <summary>
/// Wraps Windows Registry key operations to enable testability.
/// Implementations provide access to registry read/write operations
/// without requiring direct dependency on <c>Microsoft.Win32.RegistryKey</c>.
/// </summary>
public interface IRegistryKeyWrapper : IDisposable
{
    /// <summary>
    /// Opens a subkey as read-only or writable.
    /// </summary>
    /// <param name="subKey">The registry subkey path to open.</param>
    /// <param name="writable">True to open with write access; false for read-only.</param>
    /// <returns>A wrapper around the opened subkey, or null if the key does not exist.</returns>
    IRegistryKeyWrapper? OpenSubKey(string subKey, bool writable);

    /// <summary>
    /// Sets a named value in the current registry key.
    /// </summary>
    /// <param name="name">The name of the value to set.</param>
    /// <param name="value">The data to store.</param>
    void SetValue(string name, object value);

    /// <summary>
    /// Deletes a named value from the current registry key.
    /// </summary>
    /// <param name="name">The name of the value to delete.</param>
    /// <param name="throwOnMissingValue">True to throw if the value does not exist; false to silently succeed.</param>
    void DeleteValue(string name, bool throwOnMissingValue);

    /// <summary>
    /// Retrieves the data associated with a named value.
    /// </summary>
    /// <param name="name">The name of the value to retrieve.</param>
    /// <returns>The value data, or null if the value does not exist.</returns>
    object? GetValue(string name);
}
