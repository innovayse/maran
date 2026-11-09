using Maran.Modules.Dns.Domain.Entities;
using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Dns.Models;

/// <summary>
/// One open edit of one zone: the zone with its records loaded, the settings in force, and the database
/// transaction that holds the zone's lock until the edit is published or abandoned. Passed between
/// <c>DnsZoneEditor</c> and a handler, and never put on the wire.
/// </summary>
/// <remarks>
/// Disposing an edit that was not committed rolls the transaction back, which releases the lock and
/// discards every change made to the tracked zone, so a handler that returns early on a refusal needs no
/// cleanup of its own.
/// </remarks>
public sealed class ZoneEdit : IAsyncDisposable
{
    /// <summary>The transaction that holds the zone's advisory lock.</summary>
    private readonly IDbContextTransaction _transaction;

    /// <summary>The zone, tracked, with its records loaded. A handler changes it through its methods.</summary>
    public Zone Zone { get; }

    /// <summary>The settings in force: name servers, SOA mailbox and default time to live.</summary>
    public DnsSettings Settings { get; }

    /// <summary>Opens an edit over a zone whose lock <paramref name="transaction"/> holds.</summary>
    /// <param name="zone">The tracked zone, records loaded.</param>
    /// <param name="settings">The settings in force.</param>
    /// <param name="transaction">The transaction holding the lock.</param>
    public ZoneEdit(Zone zone, DnsSettings settings, IDbContextTransaction transaction)
    {
        Zone = zone;
        Settings = settings;
        _transaction = transaction;
    }

    /// <summary>Commits the transaction, which releases the zone's lock.</summary>
    /// <param name="cancellationToken">Cancels the commit.</param>
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await _transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Rolls back an uncommitted transaction and releases the lock.</summary>
    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
    }
}
