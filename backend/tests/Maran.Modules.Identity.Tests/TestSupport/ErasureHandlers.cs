using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// Builds the handlers that pseudonymise the audit journal when a login is deleted, with their
/// collaborators over one context, so a test names what it varies and not the wiring.
/// </summary>
/// <remarks>
/// The journal writes into the recording double rather than into the database on purpose: a test then
/// reads exactly the summary entries the handler asked for, apart from the rows the test itself seeded
/// into the journal table to be erased.
/// </remarks>
public static class ErasureHandlers
{
    /// <summary>The instant the clock stands at, stamped on every row the erasure touches.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Builds the journal over <paramref name="audit"/>, acting for <paramref name="caller"/>.</summary>
    /// <param name="audit">Where the entries are kept.</param>
    /// <param name="caller">Who the journal reads as the current principal; an administrator when null.</param>
    /// <returns>The journal.</returns>
    public static IdentityAuditJournal Journal(RecordingAuditWriter audit, ICurrentUser? caller = null)
    {
        return new IdentityAuditJournal(audit, caller ?? new StubCurrentUser());
    }

    /// <summary>Builds the pseudonymiser over <paramref name="context"/>.</summary>
    /// <param name="context">The database holding the journal rows.</param>
    /// <param name="audit">Where the sweep's summary entry is kept.</param>
    /// <param name="caller">Who the journal reads as the current principal; an administrator when null.</param>
    /// <returns>The pseudonymiser, reading the clock at <see cref="Now"/>.</returns>
    public static AuditPseudonymiser Pseudonymiser(
        IdentityDbContext context,
        RecordingAuditWriter audit,
        ICurrentUser? caller = null)
    {
        return new AuditPseudonymiser(
            context,
            new FakeClock(Now),
            Journal(audit, caller),
            NullLogger<AuditPseudonymiser>.Instance);
    }

    /// <summary>Builds the revoker of the keys a suspended login created over <paramref name="context"/>.</summary>
    /// <param name="context">The Identity database.</param>
    /// <param name="caller">Who the journal reads as the current principal; an administrator when null.</param>
    /// <returns>The revoker, reading the clock at <see cref="Now"/>, staging its entries on <paramref name="context"/>.</returns>
    public static ApiKeyCreatorRevoker KeyRevoker(IdentityDbContext context, ICurrentUser? caller = null)
    {
        var clock = new FakeClock(Now);

        return new ApiKeyCreatorRevoker(
            context,
            clock,
            new DatabaseAuditWriter(context, clock, new FakeCorrelationIdAccessor(null), caller ?? new StubCurrentUser()));
    }

    /// <summary>Builds the account-deletion handler over <paramref name="context"/>.</summary>
    /// <param name="context">The Identity database.</param>
    /// <param name="audit">Where the summary entries are kept.</param>
    /// <returns>The handler.</returns>
    public static AccountDeletingHandler AccountDeleting(IdentityDbContext context, RecordingAuditWriter audit)
    {
        return new AccountDeletingHandler(context, Pseudonymiser(context, audit), Journal(audit));
    }

    /// <summary>Builds the reseller-deletion handler over <paramref name="context"/>.</summary>
    /// <param name="context">The Identity database.</param>
    /// <param name="audit">Where the summary entries are kept.</param>
    /// <returns>The handler.</returns>
    public static ResellerDeletingHandler ResellerDeleting(IdentityDbContext context, RecordingAuditWriter audit)
    {
        return new ResellerDeletingHandler(context, Pseudonymiser(context, audit), Journal(audit));
    }
}
