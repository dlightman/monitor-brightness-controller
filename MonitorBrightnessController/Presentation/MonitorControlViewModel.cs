using System;
using System.Threading;
using System.Threading.Tasks;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// View model backing a single <see cref="MonitorControlGroup"/>. Exposes the bindable
/// state for one monitor (label, slider value, text value, validation/error messages) and
/// coordinates committing brightness and gamma changes via injected callbacks.
/// </summary>
/// <remarks>
/// The slider binds to <see cref="Brightness"/> and the text input binds to
/// <see cref="BrightnessText"/>. The two are kept in sync per design Property 4: moving the
/// slider updates the text (Requirement 2.4) and committing valid text updates the slider
/// (Requirement 2.5). The pure synchronization rules live in <see cref="BrightnessSync"/>.
/// Gamma follows a parallel pattern with independent transitions per Requirement 4.
/// </remarks>
public sealed class MonitorControlViewModel : ViewModelBase
{
    private readonly Func<int, int, Result<Unit>> _commit;
    private readonly Func<int, int, Result<Unit>>? _commitGamma;
    private readonly TransitionCoordinator? _transitionCoordinator;

    private int _brightness;
    private string _brightnessText = string.Empty;
    private int _gamma;
    private string _gammaText = string.Empty;
    private string? _validationError;
    private string? _gammaValidationError;
    private string? _errorMessage;
    private string? _gammaErrorMessage;

    /// <summary>The last brightness value that was successfully applied to the monitor.</summary>
    private int _lastKnownGood;

    /// <summary>The last gamma value that was successfully applied to the monitor.</summary>
    private int _lastKnownGoodGamma;

    /// <summary>Cancellation source for any in-progress smooth brightness transition.</summary>
    private CancellationTokenSource? _transitionCts;

    /// <summary>Whether smooth transitions are enabled for this monitor.</summary>
    public bool SmoothTransitionEnabled { get; set; }

    /// <summary>Duration of smooth transitions in milliseconds.</summary>
    public int TransitionDurationMs { get; set; } = 500;

    /// <summary>
    /// Creates a view model for the supplied monitor state.
    /// </summary>
    /// <param name="state">The detected monitor state used to seed the view model.</param>
    /// <param name="commit">
    /// Callback invoked to apply a brightness value to the monitor. Receives the monitor
    /// index and brightness value, and returns the result of the DDC/CI operation.
    /// </param>
    /// <param name="commitGamma">
    /// Optional callback invoked to apply a gamma value to the monitor. Receives the monitor
    /// index and gamma value, and returns the result of the DDC/CI operation.
    /// </param>
    /// <param name="transitionCoordinator">
    /// Optional coordinator that manages per-(monitor, setting) smooth transitions.
    /// When provided, gamma transitions use this coordinator for independent per-setting transitions.
    /// </param>
    public MonitorControlViewModel(
        MonitorState state,
        Func<int, int, Result<Unit>> commit,
        Func<int, int, Result<Unit>>? commitGamma = null,
        TransitionCoordinator? transitionCoordinator = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        _commit = commit ?? throw new ArgumentNullException(nameof(commit));
        _commitGamma = commitGamma;
        _transitionCoordinator = transitionCoordinator;

        MonitorIndex = state.MonitorIndex;
        MonitorName = state.MonitorName;
        DevicePath = state.DevicePath;

        // A monitor is only controllable from the UI when it supports DDC/CI and we were
        // able to read a current brightness value. A failed read leaves brightness unknown
        // and the controls disabled (Requirement 1.4).
        IsControllable = state.IsControllable && state.CurrentBrightness.HasValue;

        // Requirement 1.4: DDC/CI read failure defaults slider to midpoint (50)
        int initial = state.CurrentBrightness ?? 50;
        _brightness = initial;
        _lastKnownGood = initial;
        _brightnessText = IsControllable ? BrightnessSync.ToText(initial) : "unknown";

        // Show error indicator when DDC/CI read failed (Requirement 1.4)
        HasDdcReadError = !state.CurrentBrightness.HasValue && state.IsControllable;
        _errorMessage = state.ErrorMessage ?? (HasDdcReadError ? "DDC/CI read failed" : null);

        // Seed gamma from detected state
        // Requirement 1.4: DDC/CI read failure defaults gamma slider to midpoint (50)
        int initialGamma = state.CurrentGamma ?? 50;
        _gamma = initialGamma;
        _lastKnownGoodGamma = initialGamma;
        _gammaText = IsControllable && state.CurrentGamma.HasValue
            ? BrightnessSync.ToText(initialGamma)
            : "unknown";

        // Seed the transition coordinator with initial values
        if (_transitionCoordinator is not null)
        {
            _transitionCoordinator.SetLastAppliedValue(MonitorIndex, SettingType.Brightness, initial);
            _transitionCoordinator.SetLastAppliedValue(MonitorIndex, SettingType.Gamma, initialGamma);
        }
    }

    /// <summary>The stable monitor index assigned during detection.</summary>
    public int MonitorIndex { get; }

    /// <summary>The resolved monitor display name.</summary>
    public string MonitorName { get; }

    /// <summary>The Windows device path used for profile mapping.</summary>
    public string DevicePath { get; }

    /// <summary>Label combining the index and name, e.g. "1: DELL U2723QE".</summary>
    public string Label => $"{MonitorIndex}: {MonitorName}";

    /// <summary>True when the slider and text input should be enabled.</summary>
    public bool IsControllable { get; }

    /// <summary>
    /// True when the initial DDC/CI hardware read failed for this monitor during startup
    /// synchronization. When true, sliders default to midpoint (50) and an error indicator
    /// is shown on the monitor's panel (Requirement 1.4).
    /// </summary>
    public bool HasDdcReadError { get; }

    /// <summary>
    /// The current brightness value bound to the slider. Setting this (e.g. by dragging the
    /// slider) reflects the new value into <see cref="BrightnessText"/> (Requirement 2.4)
    /// and clears any prior validation error.
    /// </summary>
    public int Brightness
    {
        get => _brightness;
        set
        {
            if (_brightness == value)
            {
                return;
            }

            _brightness = value;
            OnPropertyChanged();

            // Requirement 2.4: keep the text input in sync with the slider position.
            _brightnessText = BrightnessSync.ToText(value);
            OnPropertyChanged(nameof(BrightnessText));

            ValidationError = null;
        }
    }

    /// <summary>
    /// The raw text bound to the numeric input. Free-form while the user types; it is only
    /// validated and reconciled with <see cref="Brightness"/> when committed via
    /// <see cref="CommitFromText"/>.
    /// </summary>
    public string BrightnessText
    {
        get => _brightnessText;
        set
        {
            if (_brightnessText == value)
            {
                return;
            }

            _brightnessText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Validation message shown adjacent to the control when invalid text was entered
    /// (Requirement 2.7). Null when there is no validation error.
    /// </summary>
    public string? ValidationError
    {
        get => _validationError;
        private set
        {
            if (_validationError == value)
            {
                return;
            }

            _validationError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasValidationError));
        }
    }

    /// <summary>True when a validation error is currently displayed.</summary>
    public bool HasValidationError => !string.IsNullOrEmpty(_validationError);

    /// <summary>
    /// Error message shown when a DDC/CI set operation fails (Requirement 2.8). Null when
    /// the most recent operation succeeded.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value)
            {
                return;
            }

            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    /// <summary>True when a DDC/CI error message is currently displayed.</summary>
    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    // ------------------------------------------------------------------
    // Gamma properties (parallel to brightness)
    // ------------------------------------------------------------------

    /// <summary>
    /// The current gamma value bound to the gamma slider. Setting this reflects the new
    /// value into <see cref="GammaText"/> and clears any prior gamma validation error.
    /// </summary>
    public int Gamma
    {
        get => _gamma;
        set
        {
            if (_gamma == value)
            {
                return;
            }

            _gamma = value;
            OnPropertyChanged();

            _gammaText = BrightnessSync.ToText(value);
            OnPropertyChanged(nameof(GammaText));

            GammaValidationError = null;
        }
    }

    /// <summary>
    /// The raw text bound to the gamma numeric input. Validated on commit.
    /// </summary>
    public string GammaText
    {
        get => _gammaText;
        set
        {
            if (_gammaText == value)
            {
                return;
            }

            _gammaText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Validation message shown for gamma text input when invalid.
    /// </summary>
    public string? GammaValidationError
    {
        get => _gammaValidationError;
        private set
        {
            if (_gammaValidationError == value)
            {
                return;
            }

            _gammaValidationError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasGammaValidationError));
        }
    }

    /// <summary>True when a gamma validation error is currently displayed.</summary>
    public bool HasGammaValidationError => !string.IsNullOrEmpty(_gammaValidationError);

    /// <summary>
    /// Error message shown when a gamma DDC/CI set operation fails.
    /// </summary>
    public string? GammaErrorMessage
    {
        get => _gammaErrorMessage;
        private set
        {
            if (_gammaErrorMessage == value)
            {
                return;
            }

            _gammaErrorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasGammaError));
        }
    }

    /// <summary>True when a gamma DDC/CI error message is currently displayed.</summary>
    public bool HasGammaError => !string.IsNullOrEmpty(_gammaErrorMessage);

    /// <summary>
    /// Commits the current gamma slider value to the monitor. Invoked when the user
    /// releases the gamma slider thumb.
    /// </summary>
    public void CommitGammaFromSlider()
    {
        if (!IsControllable || _commitGamma is null)
        {
            return;
        }

        GammaValidationError = null;
        ApplyGamma(_gamma);
    }

    /// <summary>
    /// Validates and commits the current gamma text value. On valid input the gamma slider
    /// is updated and the value is applied. On invalid input the previous valid value is
    /// retained and a validation error is shown.
    /// </summary>
    public void CommitGammaFromText()
    {
        if (!IsControllable || _commitGamma is null)
        {
            return;
        }

        if (BrightnessSync.TryParseText(_gammaText, out int parsed))
        {
            GammaValidationError = null;

            if (_gamma != parsed)
            {
                _gamma = parsed;
                OnPropertyChanged(nameof(Gamma));
            }

            string normalized = BrightnessSync.ToText(parsed);
            if (_gammaText != normalized)
            {
                _gammaText = normalized;
                OnPropertyChanged(nameof(GammaText));
            }

            ApplyGamma(parsed);
        }
        else
        {
            GammaValidationError =
                $"Enter a whole number between {MonitorService.MinBrightness} and {MonitorService.MaxBrightness}.";
            RevertGammaTo(_lastKnownGoodGamma);
        }
    }

    /// <summary>
    /// Commits the current slider value to the monitor. Invoked when the user releases the
    /// slider thumb (Requirement 2.6).
    /// </summary>
    public void CommitFromSlider()
    {
        if (!IsControllable)
        {
            return;
        }

        ValidationError = null;
        Apply(_brightness);
    }

    /// <summary>
    /// Validates and commits the current text value. On valid input the slider is updated to
    /// match (Requirement 2.5) and the value is applied to the monitor (Requirement 2.6). On
    /// invalid input the previous valid value is retained in both the text and slider and a
    /// validation error is shown (Requirement 2.7).
    /// </summary>
    public void CommitFromText()
    {
        if (!IsControllable)
        {
            return;
        }

        if (BrightnessSync.TryParseText(_brightnessText, out int parsed))
        {
            ValidationError = null;

            // Requirement 2.5: reflect the committed value into the slider.
            if (_brightness != parsed)
            {
                _brightness = parsed;
                OnPropertyChanged(nameof(Brightness));
            }

            // Normalize the displayed text (e.g. trim whitespace / leading zeros).
            string normalized = BrightnessSync.ToText(parsed);
            if (_brightnessText != normalized)
            {
                _brightnessText = normalized;
                OnPropertyChanged(nameof(BrightnessText));
            }

            Apply(parsed);
        }
        else
        {
            // Requirement 2.7: reject the entry, retain the previous valid value, show error.
            ValidationError =
                $"Enter a whole number between {MonitorService.MinBrightness} and {MonitorService.MaxBrightness}.";
            RevertTo(_lastKnownGood);
        }
    }

    private void Apply(int value)
    {
        if (SmoothTransitionEnabled && value != _lastKnownGood)
        {
            // Cancel any in-progress transition for this monitor
            _transitionCts?.Cancel();
            _transitionCts?.Dispose();
            _transitionCts = new CancellationTokenSource();
            var ct = _transitionCts.Token;

            int from = _lastKnownGood;
            int duration = TransitionDurationMs;

            // Fire-and-forget: don't block the UI
            _ = Task.Run(async () =>
            {
                await BrightnessTransition.TransitionAsync(
                    step => _commit(MonitorIndex, step),
                    from,
                    value,
                    duration,
                    ct).ConfigureAwait(false);
            });

            // Optimistically track the target as last known good
            _lastKnownGood = value;
            ErrorMessage = null;
        }
        else
        {
            Result<Unit> result = _commit(MonitorIndex, value);

            if (result.IsSuccess)
            {
                _lastKnownGood = value;
                ErrorMessage = null;
            }
            else
            {
                // Requirement 2.8: surface the failure and revert to the last applied value.
                ErrorMessage =
                    $"Failed to set brightness for monitor {MonitorIndex} ({MonitorName}): {result.Error}";
                RevertTo(_lastKnownGood);
            }
        }
    }

    private void RevertTo(int value)
    {
        if (_brightness != value)
        {
            _brightness = value;
            OnPropertyChanged(nameof(Brightness));
        }

        string text = BrightnessSync.ToText(value);
        if (_brightnessText != text)
        {
            _brightnessText = text;
            OnPropertyChanged(nameof(BrightnessText));
        }
    }

    /// <summary>
    /// Applies a gamma value to the monitor. When smooth transitions are enabled and a
    /// <see cref="TransitionCoordinator"/> is available, uses it to animate from the last
    /// applied value to the target. Otherwise applies directly in a single DDC/CI call.
    /// Transitions run independently from brightness (Requirement 4).
    /// </summary>
    private void ApplyGamma(int value)
    {
        if (_commitGamma is null)
        {
            return;
        }

        if (SmoothTransitionEnabled && value != _lastKnownGoodGamma && _transitionCoordinator is not null)
        {
            // Use TransitionCoordinator for smooth gamma transition.
            // The coordinator handles cancellation of any in-progress transition for this
            // (monitorIndex, Gamma) key, and starts a new transition from the last applied value.
            _transitionCoordinator.StartTransition(
                MonitorIndex,
                SettingType.Gamma,
                value,
                TransitionDurationMs,
                step => _commitGamma(MonitorIndex, step),
                (lastApplied, error) =>
                {
                    if (error is not null)
                    {
                        // DDC/CI failure during transition: retain last applied, surface error
                        _lastKnownGoodGamma = lastApplied;
                        GammaErrorMessage =
                            $"Failed to set gamma for monitor {MonitorIndex} ({MonitorName}): {error}";
                        RevertGammaTo(lastApplied);
                    }
                    else
                    {
                        _lastKnownGoodGamma = lastApplied;
                        GammaErrorMessage = null;
                    }
                });

            // Optimistically track the target as last known good for UI responsiveness
            _lastKnownGoodGamma = value;
            GammaErrorMessage = null;
        }
        else
        {
            // No smooth transition: single DDC/CI call
            Result<Unit> result;
            if (_transitionCoordinator is not null)
            {
                result = _transitionCoordinator.ApplyDirect(
                    MonitorIndex,
                    SettingType.Gamma,
                    value,
                    step => _commitGamma(MonitorIndex, step));
            }
            else
            {
                result = _commitGamma(MonitorIndex, value);
            }

            if (result.IsSuccess)
            {
                _lastKnownGoodGamma = value;
                GammaErrorMessage = null;
            }
            else
            {
                GammaErrorMessage =
                    $"Failed to set gamma for monitor {MonitorIndex} ({MonitorName}): {result.Error}";
                RevertGammaTo(_lastKnownGoodGamma);
            }
        }
    }

    private void RevertGammaTo(int value)
    {
        if (_gamma != value)
        {
            _gamma = value;
            OnPropertyChanged(nameof(Gamma));
        }

        string text = BrightnessSync.ToText(value);
        if (_gammaText != text)
        {
            _gammaText = text;
            OnPropertyChanged(nameof(GammaText));
        }
    }
}
