using Maran.Modules.Identity.Domain.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Models;

/// <summary>
/// The conditions that narrow the audit journal, in the one shape the journal list and every reader
/// of the journal share, so a condition is written once and means the same thing wherever it is asked.
/// </summary>
/// <param name="Action">
/// Only entries recording this machine-stable action, or <see cref="FilterValue.None"/> for every action.
/// </param>
/// <param name="Outcome">Only entries that ended this way, or <c>null</c> for both.</param>
/// <param name="From">Only entries at or after this instant, or <c>null</c> for no lower bound.</param>
/// <param name="To">Only entries at or before this instant, or <c>null</c> for no upper bound.</param>
/// <param name="Search">
/// A term matched literally inside the actor's name, the subject, the address or the viewing
/// operator's name, or <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Actor">
/// Only entries whose actor used exactly this name, compared without regard to case, or
/// <see cref="FilterValue.None"/> for every actor.
/// </param>
/// <param name="AccountId">Only entries recorded against this hosting account, or <c>null</c> for every entry.</param>
public sealed record AuditEventFilter(
    FilterValue Action,
    AuditOutcome? Outcome,
    DateTimeOffset? From,
    DateTimeOffset? To,
    SearchTerm Search,
    FilterValue Actor,
    Guid? AccountId);
