using Humo.Core.Data;
using Humo.Core.Time;
using Humo.Shared.Entities;
using Humo.Shared.Enums;

namespace Humo.Core.Services;

/// <summary>What a rig needs. Volumes are litres; nothing here is a display string.</summary>
public sealed record SaveEquipmentRequest
{
    /// <summary>Null creates a new rig; a value updates the existing one.</summary>
    public Guid? Id { get; init; }

    public required string Name { get; init; }

    public required EquipmentType Type { get; init; }

    public InsulationLevel Insulation { get; init; } = InsulationLevel.None;

    /// <summary>Litres. Optional; feeds the fire model as a capacity hint.</summary>
    public double? FireboxVolumeL { get; init; }

    /// <summary>Litres. Optional.</summary>
    public double? CookChamberVolumeL { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// Managing rigs.
/// <para>
/// Equipment is the unit the fire model learns against — a burn cadence learned
/// on a 275-gallon offset means nothing on a kamado — so a rig's identity has to
/// survive edits, and deleting one has to not orphan the cooks that ran on it.
/// </para>
/// </summary>
public interface IEquipmentService
{
    Task<IReadOnlyList<Equipment>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Equipment?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Equipment> SaveAsync(SaveEquipmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a rig. Refused while a cook is running on it: the cook holds
    /// the rig's id, and its whole fuel and pit history hangs off that id.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class EquipmentService : IEquipmentService
{
    private readonly IEquipmentRepository _equipment;
    private readonly ICookRepository _cooks;
    private readonly IClock _clock;

    public EquipmentService(IEquipmentRepository equipment, ICookRepository cooks, IClock clock)
    {
        _equipment = equipment;
        _cooks = cooks;
        _clock = clock;
    }

    public Task<IReadOnlyList<Equipment>> GetAllAsync(CancellationToken cancellationToken = default)
        => _equipment.GetAllAsync(cancellationToken);

    public Task<Equipment?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _equipment.GetAsync(id, cancellationToken);

    public async Task<Equipment> SaveAsync(
        SaveEquipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException(
                "A rig needs a name. It is how the user tells two smokers apart in every "
                + "picker and every chart from here on.",
                nameof(request));
        }

        RequireNonNegativeVolume(request.FireboxVolumeL, nameof(request.FireboxVolumeL));
        RequireNonNegativeVolume(request.CookChamberVolumeL, nameof(request.CookChamberVolumeL));

        var now = _clock.UtcNow;

        var equipment = request.Id is { } id
            ? await _equipment.GetAsync(id, cancellationToken).ConfigureAwait(false)
              ?? throw new InvalidOperationException($"No equipment with id {id}.")
            : new Equipment { CreatedAt = now };

        equipment.Name = name;
        equipment.Type = request.Type;
        equipment.Insulation = request.Insulation;
        equipment.FireboxVolumeL = request.FireboxVolumeL;
        equipment.CookChamberVolumeL = request.CookChamberVolumeL;
        equipment.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        equipment.UpdatedAt = now;

        await _equipment.SaveAsync(equipment, cancellationToken).ConfigureAwait(false);
        return equipment;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var equipment = await _equipment.GetAsync(id, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"No equipment with id {id}.");

        var unfinished = await _cooks.GetUnfinishedAsync(cancellationToken).ConfigureAwait(false);
        if (unfinished.Any(c => c.EquipmentId == id))
        {
            throw new InvalidOperationException(
                "This rig has a cook running on it. Finish the cook first — deleting the rig "
                + "mid-cook would strand the readings and fuel events that hang off its id.");
        }

        // Soft delete. Finished cooks keep pointing at this rig, and their history
        // stays readable; a hard delete would silently blank the pit type and fuel
        // series on every cook the user ever ran here.
        var now = _clock.UtcNow;
        equipment.DeletedAt = now;
        equipment.UpdatedAt = now;

        await _equipment.SaveAsync(equipment, cancellationToken).ConfigureAwait(false);
    }

    private static void RequireNonNegativeVolume(double? volume, string parameterName)
    {
        if (volume is { } value && (!double.IsFinite(value) || value <= 0))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A volume is litres and must be a positive number when given. Leave it blank "
                + "rather than entering zero — the fire model treats absent and zero differently.");
        }
    }
}
