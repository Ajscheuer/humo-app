using Humo.Core.Time;

namespace Humo.Core.Tests.Support;

/// <summary>
/// A clock the test drives. Nothing in Humo reads the ambient clock inside
/// logic, so every duration and threshold is exercised at an instant the test
/// chose rather than whenever it happened to run.
/// </summary>
internal sealed class TestClock : IClock
{
    /// <summary>
    /// A fixed UTC-06:00 zone, no daylight saving. Built rather than looked up by
    /// id so the tests behave identically on a machine with no tzdata, and fixed
    /// rather than <see cref="TimeZoneInfo.Local"/> so "what the user typed" means
    /// the same thing on every build agent.
    /// </summary>
    public static readonly TimeZoneInfo CentralStandardTime = TimeZoneInfo.CreateCustomTimeZone(
        "Test/UTC-6", TimeSpan.FromHours(-6), "Test UTC-6", "Test UTC-6");

    public TestClock(DateTimeOffset? start = null, TimeZoneInfo? localTimeZone = null)
    {
        UtcNow = start ?? new DateTimeOffset(2026, 3, 14, 6, 0, 0, TimeSpan.Zero);
        LocalTimeZone = localTimeZone ?? CentralStandardTime;
    }

    public DateTimeOffset UtcNow { get; set; }

    public TimeZoneInfo LocalTimeZone { get; set; }

    public void Advance(TimeSpan by) => UtcNow += by;
}
