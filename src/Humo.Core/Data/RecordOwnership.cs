using Humo.Core.Data.Records;

namespace Humo.Core.Data;

/// <summary>
/// Adopting records that predate account scoping.
/// <para>
/// Every table carried an <c>AccountId</c> column from the start but nothing set
/// it, so every row written before this slice holds <see cref="Guid.Empty"/>.
/// Switching scoping on without this would make all of it invisible at once —
/// which for a guest with no account and no sync is not "hidden", it is gone.
/// </para>
/// </summary>
public interface IRecordOwnership
{
    /// <summary>
    /// Assigns every ownerless row to <paramref name="accountId"/> and reports
    /// how many were adopted. Safe to run on every launch: once there are no
    /// ownerless rows it does nothing.
    /// </summary>
    Task<int> ClaimUnownedRecordsAsync(Guid accountId, CancellationToken cancellationToken = default);
}

internal sealed class RecordOwnership : IRecordOwnership
{
    /// <summary>
    /// Every table that carries an account id. A table added later and missed
    /// here would silently lose its pre-scoping rows, so the conventions test
    /// checks this list against the records that actually exist.
    /// </summary>
    internal static readonly IReadOnlyList<string> OwnedTables =
    [
        "equipment",
        "cooks",
        "temp_entries",
        "pit_temp_entries",
        "fuel_events",
        "events",
    ];

    private readonly IConnectionSource _connections;

    public RecordOwnership(IConnectionSource connections) => _connections = connections;

    public async Task<int> ClaimUnownedRecordsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Claiming rows for Guid.Empty would be a no-op that looks like success.",
                nameof(accountId));
        }

        var db = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        var claimed = 0;
        foreach (var table in OwnedTables)
        {
            // Table names are from the constant list above, never from input.
            claimed += await db.ExecuteAsync(
                $"UPDATE {table} SET AccountId = ? WHERE AccountId = ?",
                accountId,
                Guid.Empty).ConfigureAwait(false);
        }

        return claimed;
    }
}
