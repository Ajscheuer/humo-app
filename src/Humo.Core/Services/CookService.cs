using Humo.Core.Data;
using Humo.Core.Time;
using Humo.Shared;
using Humo.Shared.Entities;
using Humo.Shared.Enums;

namespace Humo.Core.Services;

/// <summary>What a new cook needs. Temperatures are Celsius, weight is kilograms.</summary>
public sealed record StartCookRequest
{
    public required MeatType MeatType { get; init; }

    /// <summary>Only meaningful when <see cref="MeatType"/> is Other.</summary>
    public string? MeatTypeOther { get; init; }

    public required double WeightKg { get; init; }

    /// <summary>Optional: a parrilla cook working by feel has no target.</summary>
    public double? TargetInternalTempC { get; init; }

    public double? AmbientTempC { get; init; }

    public string? Notes { get; init; }

    /// <summary>Defaults to the app's single implicit rig when not given.</summary>
    public Guid? EquipmentId { get; init; }
}

/// <summary>
/// Starting, logging and finishing a cook.
/// <para>
/// Every temperature crossing this interface is Celsius. Conversion to whatever
/// the user reads happens once, at display; no degrees-F value ever reaches
/// storage.
/// </para>
/// </summary>
public interface ICookService
{
    /// <summary>
    /// The rig to cook on. Slice 1 has no equipment management, so this returns
    /// a single default rig, creating it on first use.
    /// </summary>
    Task<Equipment> GetOrCreateDefaultEquipmentAsync(CancellationToken cancellationToken = default);

    Task<Cook> StartCookAsync(StartCookRequest request, CancellationToken cancellationToken = default);

    /// <summary>The most recently started unfinished cook, or null.</summary>
    Task<Cook?> GetActiveCookAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a temperature reading. The meat reading belongs to the cook; the
    /// pit reading, when given, belongs to the rig — one fire has one
    /// temperature, so two cooks sharing a rig cannot contradict each other.
    /// This writes up to two records from one call, which is what keeps the
    /// logging screen a single sheet.
    /// </summary>
    Task<TempEntry> LogTemperatureAsync(
        LogTemperatureRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TempEntry>> GetTemperaturesAsync(
        Guid cookId,
        CancellationToken cancellationToken = default);

    Task<Cook> FinishCookAsync(
        Guid cookId,
        int? rating = null,
        string? notes = null,
        CancellationToken cancellationToken = default);
}

/// <summary>One temperature reading, as taken at the smoker.</summary>
public sealed record LogTemperatureRequest
{
    public required Guid CookId { get; init; }

    public required double MeatTempC { get; init; }

    /// <summary>Optional. Recorded against the rig, not the cook.</summary>
    public double? PitTempC { get; init; }

    /// <summary>Optional, and only stored alongside a pit reading.</summary>
    public double? AmbientTempC { get; init; }

    /// <summary>
    /// When the reading was taken. Defaults to now, but is editable: cooks
    /// routinely log a reading minutes after taking it, and a wrong timestamp
    /// distorts both stall detection and the fire model.
    /// </summary>
    public DateTimeOffset? RecordedAt { get; init; }

    public string? Note { get; init; }
}

public sealed class CookService : ICookService
{
    /// <summary>
    /// Name of the implicit rig created for slice 1. Not localized: it is a
    /// user-editable record value once equipment management exists, not UI text,
    /// and translating it would rename an existing user's rig when they switch
    /// language.
    /// </summary>
    internal const string DefaultEquipmentName = "My smoker";

    private readonly IEquipmentRepository _equipment;
    private readonly ICookRepository _cooks;
    private readonly ITempEntryRepository _tempEntries;
    private readonly IPitTempEntryRepository _pitTempEntries;
    private readonly IClock _clock;

    public CookService(
        IEquipmentRepository equipment,
        ICookRepository cooks,
        ITempEntryRepository tempEntries,
        IPitTempEntryRepository pitTempEntries,
        IClock clock)
    {
        _equipment = equipment;
        _cooks = cooks;
        _tempEntries = tempEntries;
        _pitTempEntries = pitTempEntries;
        _clock = clock;
    }

    public async Task<Equipment> GetOrCreateDefaultEquipmentAsync(
        CancellationToken cancellationToken = default)
    {
        var existing = await _equipment.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return existing[0];
        }

        var now = _clock.UtcNow;
        var equipment = new Equipment
        {
            Name = DefaultEquipmentName,
            Type = EquipmentType.Offset,
            Insulation = InsulationLevel.None,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _equipment.SaveAsync(equipment, cancellationToken).ConfigureAwait(false);
        return equipment;
    }

    public async Task<Cook> StartCookAsync(
        StartCookRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!double.IsFinite(request.WeightKg) || request.WeightKg <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.WeightKg,
                "Weight must be a positive number of kilograms. It feeds the fire model's "
                + "thermal load for the whole rig, so a missing or nonsensical value would "
                + "corrupt predictions for every cook sharing that fire.");
        }

        var equipment = request.EquipmentId is { } id
            ? await _equipment.GetAsync(id, cancellationToken).ConfigureAwait(false)
              ?? throw new InvalidOperationException($"No equipment with id {id}.")
            : await GetOrCreateDefaultEquipmentAsync(cancellationToken).ConfigureAwait(false);

        var now = _clock.UtcNow;
        var cook = new Cook
        {
            EquipmentId = equipment.Id,

            // Snapshot, not a lookup: editing or deleting the rig later must not
            // rewrite what this cook was run on.
            PitType = equipment.Type,

            MeatType = request.MeatType,
            MeatTypeOther = request.MeatType == MeatType.Other ? request.MeatTypeOther : null,
            WeightKg = request.WeightKg,
            TargetInternalTempC = request.TargetInternalTempC,
            AmbientTempC = request.AmbientTempC,
            Notes = request.Notes,
            StartedAt = now,
            LastActivityAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _cooks.SaveAsync(cook, cancellationToken).ConfigureAwait(false);
        return cook;
    }

    public async Task<Cook?> GetActiveCookAsync(CancellationToken cancellationToken = default)
    {
        var unfinished = await _cooks.GetUnfinishedAsync(cancellationToken).ConfigureAwait(false);
        return unfinished.Count > 0 ? unfinished[0] : null;
    }

    public async Task<TempEntry> LogTemperatureAsync(
        LogTemperatureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cook = await _cooks.GetAsync(request.CookId, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException($"No cook with id {request.CookId}.");

        if (cook.IsFinished)
        {
            throw new InvalidOperationException(
                "Cannot log a reading against a finished cook. Reopening a finished cook is a "
                + "deliberate action, not something a stray tap should do.");
        }

        if (!double.IsFinite(request.MeatTempC))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request), request.MeatTempC, "Meat temperature must be a finite number.");
        }

        if (request.PitTempC is { } pit && !double.IsFinite(pit))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request), pit, "Pit temperature must be a finite number.");
        }

        var now = _clock.UtcNow;
        var recordedAt = request.RecordedAt ?? now;

        var entry = new TempEntry
        {
            CookId = cook.Id,
            RecordedAt = recordedAt,
            MeatTempC = request.MeatTempC,
            Note = request.Note,
            Source = TempSource.Manual,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _tempEntries.SaveAsync(entry, cancellationToken).ConfigureAwait(false);

        if (request.PitTempC is { } pitTempC)
        {
            var pitEntry = new PitTempEntry
            {
                // The rig, not the cook.
                EquipmentId = cook.EquipmentId,
                RecordedAt = recordedAt,
                PitTempC = pitTempC,
                AmbientTempC = request.AmbientTempC,
                Note = request.Note,
                Source = TempSource.Manual,
                CreatedAt = now,
                UpdatedAt = now,
            };

            await _pitTempEntries.SaveAsync(pitEntry, cancellationToken).ConfigureAwait(false);
        }

        // Activity is when the reading was taken, not when it was typed. A cook
        // catching up on three readings at once should not look more recently
        // active than the last one actually is -- but a back-dated entry must not
        // drag activity backwards either.
        if (recordedAt > cook.LastActivityAt)
        {
            cook.LastActivityAt = recordedAt;
        }

        cook.UpdatedAt = now;
        await _cooks.SaveAsync(cook, cancellationToken).ConfigureAwait(false);

        return entry;
    }

    public Task<IReadOnlyList<TempEntry>> GetTemperaturesAsync(
        Guid cookId,
        CancellationToken cancellationToken = default)
        => _tempEntries.GetForCookAsync(cookId, cancellationToken);

    public async Task<Cook> FinishCookAsync(
        Guid cookId,
        int? rating = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var cook = await _cooks.GetAsync(cookId, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException($"No cook with id {cookId}.");

        if (cook.IsFinished)
        {
            throw new InvalidOperationException("This cook is already finished.");
        }

        if (rating is not null and (< 1 or > 5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rating), rating, "Rating is 1-5 stars on the result, or null for unrated.");
        }

        var now = _clock.UtcNow;
        cook.FinishedAt = now;
        cook.FinishReason = CookFinishReason.Manual;
        cook.LastActivityAt = now;
        cook.UpdatedAt = now;

        if (rating is not null)
        {
            cook.Rating = rating;
        }

        if (!string.IsNullOrWhiteSpace(notes))
        {
            cook.Notes = notes;
        }

        await _cooks.SaveAsync(cook, cancellationToken).ConfigureAwait(false);
        return cook;
    }
}
