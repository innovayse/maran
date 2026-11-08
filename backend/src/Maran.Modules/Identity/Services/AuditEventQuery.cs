using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// The one place the audit journal is narrowed: what an <see cref="AuditEventFilter"/> means, which
/// entries involve one user and which belong to one account. A reader of the journal asks here and
/// then pages, counts or streams the result, so no two readers can disagree about which rows match.
/// </summary>
/// <remarks>
/// Every query is read-only (<c>AsNoTracking</c>) and stays a query: nothing is materialized here, so
/// the caller's filter, count and page all run in PostgreSQL. The journal is server-wide
/// unless a reader asks for a scope: <see cref="Matching(AuditEventFilter, AuditEventScope)"/>
/// narrows to what a reseller may read (issue #370), and the plain <see cref="Matching(AuditEventFilter)"/>
/// is for the readers the controllers' policy already holds to administrators.
/// </remarks>
public sealed class AuditEventQuery
{
    /// <summary>The module's database context; Identity owns the journal's table.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the query.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public AuditEventQuery(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// The entries that satisfy every condition <paramref name="filter"/> states; a condition left at
    /// its empty state narrows nothing.
    /// </summary>
    /// <remarks>
    /// The search reads four columns and matches each literally. The actor's name is how an operator
    /// asks "what did this user do", the subject "what happened to this site", and the address "what
    /// came from this host". The fourth is the name of the operator who was viewing as the actor
    /// (issue #327): without it, "what did this administrator do" could not find an entry written
    /// while they viewed as a customer, because that entry names the customer as its actor. The
    /// impersonator is an optional owned value that is absent for an actor acting as themselves, so
    /// its name is read only behind a null check. <see cref="LikePattern"/> escapes the wildcards, so
    /// a login with an underscore in it matches itself and not every name one character longer.
    /// The actor condition is different from the search: it is an exact, case-insensitive match on the
    /// actor's name alone, for the question "everything this one name did".
    /// </remarks>
    /// <param name="filter">The conditions to apply.</param>
    /// <returns>The matching entries, unordered, untracked.</returns>
    public IQueryable<AuditEvent> Matching(AuditEventFilter filter)
    {
        return Narrow(_dbContext.AuditEvents.AsNoTracking(), filter);
    }

    /// <summary>
    /// The entries a caller may read (<see cref="Visible"/>) that also satisfy every condition
    /// <paramref name="filter"/> states. The scope is applied BEFORE the filter, so no combination of
    /// conditions can reach an entry outside it.
    /// </summary>
    /// <param name="filter">The conditions to apply.</param>
    /// <param name="scope">Which entries the caller may read.</param>
    /// <returns>The matching entries within the scope, unordered, untracked.</returns>
    public IQueryable<AuditEvent> Matching(AuditEventFilter filter, AuditEventScope scope)
    {
        return Narrow(Visible(scope), filter);
    }

    /// <summary>
    /// The entries <paramref name="scope"/> allows: every entry, or for a reseller those stamped with an
    /// account it owns and those its own login made. The login half is the actor's user id and never the
    /// actor's name, which a failed sign-in lets anybody type.
    /// </summary>
    /// <param name="scope">Which entries the caller may read.</param>
    /// <returns>The entries within the scope, unordered, untracked.</returns>
    public IQueryable<AuditEvent> Visible(AuditEventScope scope)
    {
        IQueryable<AuditEvent> events = _dbContext.AuditEvents.AsNoTracking();

        if (scope is not ResellerEntriesScope reseller)
        {
            return events;
        }

        // Captured locals, so EF Core sends the ids as one array parameter and the login as a parameter.
        var ownedAccountIds = reseller.OwnedAccountIds.ToArray();
        var loginUserId = reseller.LoginUserId;
        return events.Where(auditEvent =>
            (auditEvent.AccountId != null && ownedAccountIds.Contains(auditEvent.AccountId.Value))
            || auditEvent.ActorUserId == loginUserId);
    }

    /// <summary>
    /// The entries that involve one user: those where the user is the actor, and those where the user
    /// is the operator who was viewing as somebody else. Entries another operator wrote while viewing
    /// as this user's login name this user as actor and are included by the first half.
    /// </summary>
    /// <param name="userId">The user's id.</param>
    /// <returns>The entries naming the user as actor or as the viewing operator, unordered, untracked.</returns>
    public IQueryable<AuditEvent> InvolvingUser(Guid userId)
    {
        return _dbContext.AuditEvents.AsNoTracking().Where(auditEvent =>
            auditEvent.ActorUserId == userId
            || (auditEvent.Impersonator != null && auditEvent.Impersonator.OperatorId == userId));
    }

    /// <summary>The entries recorded against one hosting account.</summary>
    /// <param name="accountId">The account's id.</param>
    /// <returns>The entries stamped with the account, unordered, untracked.</returns>
    public IQueryable<AuditEvent> OfAccount(Guid accountId)
    {
        return _dbContext.AuditEvents.AsNoTracking().Where(auditEvent => auditEvent.AccountId == accountId);
    }

    /// <summary>Applies the conditions of <paramref name="filter"/> to <paramref name="events"/>.</summary>
    /// <param name="events">The entries to narrow.</param>
    /// <param name="filter">The conditions to apply.</param>
    /// <returns>The entries that satisfy every condition, unordered, untracked.</returns>
    private static IQueryable<AuditEvent> Narrow(IQueryable<AuditEvent> events, AuditEventFilter filter)
    {
        if (!filter.Action.IsNone)
        {
            var action = filter.Action.Value;
            events = events.Where(auditEvent => auditEvent.Action == action);
        }

        if (filter.Outcome is { } outcome)
        {
            var succeeded = outcome == AuditOutcome.Succeeded;
            events = events.Where(auditEvent => auditEvent.Succeeded == succeeded);
        }

        if (filter.From is { } from)
        {
            events = events.Where(auditEvent => auditEvent.OccurredAt >= from);
        }

        if (filter.To is { } to)
        {
            events = events.Where(auditEvent => auditEvent.OccurredAt <= to);
        }

        if (!filter.Actor.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; ILIKE with no wildcard is a
            // case-insensitive equality, and LikePattern.Exactly escapes the underscore a name may hold.
            var actor = LikePattern.Exactly(filter.Actor.Value);
            events = events.Where(auditEvent =>
                EF.Functions.ILike(auditEvent.ActorUsername, actor, LikePattern.EscapeCharacter));
        }

        if (filter.AccountId is { } accountId)
        {
            events = events.Where(auditEvent => auditEvent.AccountId == accountId);
        }

        if (!filter.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the
            // wildcards so the term matches literally.
            var pattern = LikePattern.Contains(filter.Search.Value);
            events = events.Where(auditEvent =>
                EF.Functions.ILike(auditEvent.ActorUsername, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(auditEvent.Subject, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(auditEvent.IpAddress, pattern, LikePattern.EscapeCharacter)
                || (auditEvent.Impersonator != null
                    && EF.Functions.ILike(auditEvent.Impersonator.Username, pattern, LikePattern.EscapeCharacter)));
        }

        return events;
    }
}
