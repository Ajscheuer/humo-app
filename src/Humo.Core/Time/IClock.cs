namespace Humo.Core.Time;

/// <summary>
/// The current instant, injected rather than read from the ambient clock.
/// <para>
/// Nothing in Humo calls <c>DateTimeOffset.UtcNow</c> inside logic. Cook
/// durations, stale-cook thresholds and the fire model's learned intervals are
/// all time arithmetic, and none of it is testable against a clock that moves
/// while the test runs.
/// </para>
/// </summary>
public interface IClock
{
    /// <summary>Now, in UTC. Always UTC: every instant Humo stores is UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// The zone the user reads and types in. Storage is UTC, but a date picker
    /// collects wall-clock time, so something has to turn "yesterday 11pm" into
    /// an instant. Injected for the same reason as <see cref="UtcNow"/>: a test
    /// asserting that a back-dated reading lands correctly must not depend on
    /// where the build agent is.
    /// </summary>
    TimeZoneInfo LocalTimeZone { get; }
}

/// <summary>The real clock. Registered in DI; replaced by a fake in tests.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
}
