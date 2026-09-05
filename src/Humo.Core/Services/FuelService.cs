using Humo.Core.Data;
using Humo.Core.Time;
using Humo.Shared.Entities;
using Humo.Shared.Enums;

namespace Humo.Core.Services;

/// <summary>
/// What the fuel sheet opens with.
/// <para>
/// Everything except <see cref="SizeClass"/> is pre-filled, which is what makes
/// the fast path two taps: open the sheet, tap a size, done.
/// </para>
/// </summary>
public sealed record FuelDefaults
{
    public required WoodType WoodType { get; init; }

    public string? WoodTypeOther { get; init; }

    public required FuelForm Form { get; init; }

    /// <summary>
    /// True when these came from a previous fuel event on this rig rather than
    /// from the cold-start fallback. The sheet uses it to decide whether the
    /// pre-fill is worth trusting silently or worth showing.
    /// </summary>
    public required bool FromPreviousEvent { get; init; }
}

/// <summary>What a fuel event needs. Only the size class has no default.</summary>
public sealed record LogFuelRequest
{
    /// <summary>The rig. Fuel belongs to the fire, never to one cook.</summary>
    public required Guid EquipmentId { get; init; }

    /// <summary>The cook on screen at the time. Display only.</summary>
    public Guid? CookId { get; init; }

    public required WoodType WoodType { get; init; }

    public string? WoodTypeOther { get; init; }

    public required FuelForm Form { get; init; }

    /// <summary>The one thing the fast path asks for.</summary>
    public required SizeClass SizeClass { get; init; }

    /// <summary>Pieces added. Defaults to one.</summary>
    public int Count { get; init; } = 1;

    public double? WeightKg { get; init; }

    /// <summary>Null means now.</summary>
    public DateTimeOffset? RecordedAt { get; init; }

    /// <summary>Set only by an "Added log" notification response.</summary>
    public bool ViaNotification { get; init; }
}

/// <summary>
/// Logging fuel, and remembering enough about the last load that the next one
/// takes two taps.
/// </summary>
public interface IFuelService
{
    /// <summary>
    /// What to open the sheet with, taken from the last fuel event on this rig.
    /// Falls back to oak splits on a rig that has never been fed.
    /// </summary>
    Task<FuelDefaults> GetDefaultsAsync(Guid equipmentId, CancellationToken cancellationToken = default);

    Task<FuelEvent> LogFuelAsync(LogFuelRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fuel history for one rig, oldest first.</summary>
    Task<IReadOnlyList<FuelEvent>> GetForEquipmentAsync(
        Guid equipmentId,
        CancellationToken cancellationToken = default);
}

public sealed class FuelService : IFuelService
{
    /// <summary>
    /// The cold-start pre-fill, used only until the rig has one fuel event of its
    /// own. Oak splits because that is what the primary persona's offset burns;
    /// being wrong here costs one extra tap, while leaving the fields empty costs
    /// the two-tap guarantee outright.
    /// </summary>
    internal const WoodType FallbackWoodType = WoodType.Oak;

    internal const FuelForm FallbackForm = FuelForm.Split;

    private readonly IFuelEventRepository _fuelEvents;
    private readonly IEquipmentRepository _equipment;
    private readonly IClock _clock;

    public FuelService(
        IFuelEventRepository fuelEvents,
        IEquipmentRepository equipment,
        IClock clock)
    {
        _fuelEvents = fuelEvents;
        _equipment = equipment;
        _clock = clock;
    }

    public async Task<FuelDefaults> GetDefaultsAsync(
        Guid equipmentId,
        CancellationToken cancellationToken = default)
    {
        var previous = await _fuelEvents
            .GetMostRecentForEquipmentAsync(equipmentId, cancellationToken)
            .ConfigureAwait(false);

        if (previous is null)
        {
            return new FuelDefaults
            {
                WoodType = FallbackWoodType,
                Form = FallbackForm,
                FromPreviousEvent = false,
            };
        }

        // Deliberately not carried forward: size class and count. Size is the one
        // judgement the cook makes each time, and repeating the last count would
        // silently log three splits when they added one.
        return new FuelDefaults
        {
            WoodType = previous.WoodType,
            WoodTypeOther = previous.WoodType == WoodType.Other ? previous.WoodTypeOther : null,
            Form = previous.Form,
            FromPreviousEvent = true,
        };
    }

    public async Task<FuelEvent> LogFuelAsync(
        LogFuelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _ = await _equipment.GetAsync(request.EquipmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No equipment with id {request.EquipmentId}.");

        if (request.Count < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Count,
                "A fuel event records at least one piece going on the fire.");
        }

        if (request.WeightKg is { } weight && (!double.IsFinite(weight) || weight <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                weight,
                "Weight is optional, but when given it must be a positive number of kilograms.");
        }

        var now = _clock.UtcNow;
        var fuelEvent = new FuelEvent
        {
            EquipmentId = request.EquipmentId,
            CookId = request.CookId,
            RecordedAt = request.RecordedAt ?? now,
            WoodType = request.WoodType,
            WoodTypeOther = request.WoodType == WoodType.Other
                                && !string.IsNullOrWhiteSpace(request.WoodTypeOther)
                ? request.WoodTypeOther.Trim()
                : null,
            Form = request.Form,
            SizeClass = request.SizeClass,
            Count = request.Count,
            WeightKg = request.WeightKg,
            ViaNotification = request.ViaNotification,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _fuelEvents.SaveAsync(fuelEvent, cancellationToken).ConfigureAwait(false);
        return fuelEvent;
    }

    public Task<IReadOnlyList<FuelEvent>> GetForEquipmentAsync(
        Guid equipmentId,
        CancellationToken cancellationToken = default)
        => _fuelEvents.GetForEquipmentAsync(equipmentId, cancellationToken);
}
