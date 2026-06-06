using FluentAssertions;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Smoke tests verifying the test harness and core dependencies are wired up.
/// </summary>
public class ScaffoldingTests
{
    [Fact]
    public void TestHarness_Runs()
    {
        true.Should().BeTrue();
    }
}
