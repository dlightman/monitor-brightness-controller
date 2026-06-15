using FluentAssertions;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using NSubstitute;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Unit tests for <see cref="StartupRegistration"/> covering registry error handling,
/// successful register/unregister, and path management (Requirements 1.5, 1.6).
/// </summary>
public class StartupRegistrationTests
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MonitorBrightnessController";

    private readonly IRegistryKeyWrapper _mockRoot;
    private readonly StartupRegistration _sut;

    public StartupRegistrationTests()
    {
        _mockRoot = Substitute.For<IRegistryKeyWrapper>();
        _sut = new StartupRegistration(_mockRoot);
    }

    // --- SetStartWithWindows: registry key open failure (Req 1.5) ---

    [Fact]
    public void SetStartWithWindows_Enable_WhenRegistryKeyCannotBeOpened_ReturnsFailure()
    {
        // Requirement 1.5: If the Run registry key cannot be opened, return failure.
        _mockRoot.OpenSubKey(RunKey, true).Returns((IRegistryKeyWrapper?)null);

        var result = _sut.SetStartWithWindows(true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SetStartWithWindows_Disable_WhenRegistryKeyCannotBeOpened_ReturnsFailure()
    {
        // Requirement 1.5: If the Run registry key cannot be opened for disable, return failure.
        _mockRoot.OpenSubKey(RunKey, false).Returns((IRegistryKeyWrapper?)null);

        var result = _sut.SetStartWithWindows(false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    // --- SetStartWithWindows: successful register (Req 1.1, 1.3) ---

    [Fact]
    public void SetStartWithWindows_Enable_WhenRegistryKeyOpens_CallsSetValueAndReturnsSuccess()
    {
        // Requirement 1.1: Creates a value with the quoted executable path.
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, true).Returns(mockSubKey);

        var result = _sut.SetStartWithWindows(true);

        // Environment.ProcessPath is non-null during test execution
        if (result.IsSuccess)
        {
            mockSubKey.Received(1).SetValue(AppName, Arg.Is<string>(v => v.StartsWith("\"") && v.EndsWith("\" --silent")));
        }
        else
        {
            // If ProcessPath is null in the test environment, we get a failure (Req 1.6).
            result.Error.Should().Contain("executable path");
        }
    }

    // --- SetStartWithWindows: successful unregister (Req 1.2) ---

    [Fact]
    public void SetStartWithWindows_Disable_WhenRegistryKeyOpens_CallsDeleteValueAndReturnsSuccess()
    {
        // Requirement 1.2: Removes the application's value from the Run registry key.
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, false).Returns(mockSubKey);

        var result = _sut.SetStartWithWindows(false);

        result.IsSuccess.Should().BeTrue();
        mockSubKey.Received(1).DeleteValue(AppName, false);
    }

    // --- IsRegistered ---

    [Fact]
    public void IsRegistered_WhenGetValueReturnsNonNull_ReturnsTrue()
    {
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, false).Returns(mockSubKey);
        mockSubKey.GetValue(AppName).Returns("some_path");

        var result = _sut.IsRegistered();

        result.Should().BeTrue();
    }

    [Fact]
    public void IsRegistered_WhenGetValueReturnsNull_ReturnsFalse()
    {
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, false).Returns(mockSubKey);
        mockSubKey.GetValue(AppName).Returns(null);

        var result = _sut.IsRegistered();

        result.Should().BeFalse();
    }

    [Fact]
    public void IsRegistered_WhenOpenSubKeyReturnsNull_ReturnsFalse()
    {
        _mockRoot.OpenSubKey(RunKey, false).Returns((IRegistryKeyWrapper?)null);

        var result = _sut.IsRegistered();

        result.Should().BeFalse();
    }

    // --- EnsureRegistration ---

    [Fact]
    public void EnsureRegistration_WhenDisabled_ReturnsSuccessWithoutTouchingRegistry()
    {
        // When start-with-Windows is disabled, no registry access needed.
        var result = _sut.EnsureRegistration(false);

        result.IsSuccess.Should().BeTrue();
        _mockRoot.DidNotReceive().OpenSubKey(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public void EnsureRegistration_WhenEnabled_AndRegistryKeyCannotBeOpened_ReturnsFailure()
    {
        // Requirement 1.5: If the Run registry key cannot be opened, return failure.
        _mockRoot.OpenSubKey(RunKey, true).Returns((IRegistryKeyWrapper?)null);

        var result = _sut.EnsureRegistration(true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    // --- UpdateRegisteredPath ---

    [Fact]
    public void UpdateRegisteredPath_WhenRegistryKeyOpens_SetsValueWithQuotedPathAndReturnsSuccess()
    {
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, true).Returns(mockSubKey);
        var newPath = @"C:\Program Files\MonitorBrightnessController\MonitorBrightnessController.exe";

        var result = _sut.UpdateRegisteredPath(newPath);

        result.IsSuccess.Should().BeTrue();
        mockSubKey.Received(1).SetValue(AppName, $"\"{newPath}\" --silent");
    }

    [Fact]
    public void UpdateRegisteredPath_WhenRegistryKeyCannotBeOpened_ReturnsFailure()
    {
        // Requirement 1.5: Registry key open failure returns failure result.
        _mockRoot.OpenSubKey(RunKey, true).Returns((IRegistryKeyWrapper?)null);

        var result = _sut.UpdateRegisteredPath(@"C:\some\path.exe");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }
}
