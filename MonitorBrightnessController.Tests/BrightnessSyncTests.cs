using System.Globalization;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Arbitrary that produces integers constrained to the valid brightness range [0, 100],
/// wrapped in a dedicated marker type so it does not collide with the default int
/// arbitrary used elsewhere.
/// </summary>
public readonly record struct SyncBrightness(int Value);

/// <summary>
/// Custom FsCheck arbitraries for the bidirectional brightness sync property test (Property 4).
/// </summary>
public static class BrightnessSyncArbitraries
{
    /// <summary>Generates brightness values in the inclusive range [0, 100].</summary>
    public static Arbitrary<SyncBrightness> SyncBrightness() =>
        Arb.From(Gen.Choose(0, 100).Select(v => new SyncBrightness(v)));
}

/// <summary>
/// Property and example tests for bidirectional slider <-> text input synchronization
/// (design Property 4). Exercises both the pure <see cref="BrightnessSync"/> logic and the
/// end-to-end behaviour of <see cref="MonitorControlViewModel"/>.
/// </summary>
public class BrightnessSyncTests
{
    private static MonitorControlViewModel CreateControllableViewModel(int initialBrightness)
    {
        var state = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Test Monitor",
            DevicePath = @"\\?\DISPLAY#TEST#1",
            IsControllable = true,
            CurrentBrightness = initialBrightness,
        };

        // Commit callback always succeeds so the view model retains committed values.
        return new MonitorControlViewModel(state, (_, _) => Result<Unit>.Success(Unit.Value));
    }

    // Feature: monitor-brightness-controller, Property 4: Bidirectional Brightness Control Sync
    // Validates: Requirements 2.4, 2.5
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(BrightnessSyncArbitraries) })]
    public void BrightnessSync_IsBidirectional_PureLogic(SyncBrightness brightness)
    {
        int value = brightness.Value;

        // Slider -> text (Requirement 2.4): the formatted text round-trips back to the
        // exact slider value when committed (Requirement 2.5).
        string text = BrightnessSync.ToText(value);
        bool parsed = BrightnessSync.TryParseText(text, out int roundTripped);

        parsed.Should().BeTrue("'{0}' is the canonical text form of a valid brightness", text);
        roundTripped.Should().Be(value,
            "formatting V then parsing it back must yield V (slider->text->slider round-trip)");
        text.Should().Be(value.ToString(CultureInfo.InvariantCulture));
    }

    // Feature: monitor-brightness-controller, Property 4: Bidirectional Brightness Control Sync
    // Validates: Requirements 2.4, 2.5
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(BrightnessSyncArbitraries) })]
    public void BrightnessSync_IsBidirectional_ViewModel(SyncBrightness sliderValue, SyncBrightness textValue)
    {
        MonitorControlViewModel vm = CreateControllableViewModel(0);

        // Requirement 2.4: moving the slider updates the text input to match.
        vm.Brightness = sliderValue.Value;
        vm.BrightnessText.Should().Be(sliderValue.Value.ToString(CultureInfo.InvariantCulture),
            "setting the slider must reflect the value into the text input");

        // Requirement 2.5: committing a valid value via the text input updates the slider.
        vm.BrightnessText = textValue.Value.ToString(CultureInfo.InvariantCulture);
        vm.CommitFromText();
        vm.Brightness.Should().Be(textValue.Value,
            "committing valid text must reflect the value into the slider");
        vm.BrightnessText.Should().Be(textValue.Value.ToString(CultureInfo.InvariantCulture),
            "after commit the text is normalized to match the slider");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void SliderChange_UpdatesText(int value)
    {
        MonitorControlViewModel vm = CreateControllableViewModel(0);

        vm.Brightness = value;

        vm.BrightnessText.Should().Be(value.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("50", 50)]
    [InlineData("100", 100)]
    public void TextCommit_UpdatesSlider(string text, int expected)
    {
        MonitorControlViewModel vm = CreateControllableViewModel(0);

        vm.BrightnessText = text;
        vm.CommitFromText();

        vm.Brightness.Should().Be(expected);
    }
}
