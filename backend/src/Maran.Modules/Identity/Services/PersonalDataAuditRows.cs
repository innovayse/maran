using Maran.Modules.Identity.Domain.Entities;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Answers which journal rows are ABOUT a personal-data subject, so the handler that counts them
/// against the cap and the writer that streams them can never disagree on what "about" means.
/// </summary>
/// <remarks>
/// A user is the actor of a row or the operator who was viewing as the actor; an account is a row
/// stamped with the account. The two scopes themselves live in <see cref="AuditEventQuery"/>; this
/// type only chooses between them by kind.
/// </remarks>
public sealed class PersonalDataAuditRows
{
    /// <summary>The journal's shared query surface.</summary>
    private readonly AuditEventQuery _events;

    /// <summary>Creates the selector.</summary>
    /// <param name="events">The journal's shared query surface.</param>
    public PersonalDataAuditRows(AuditEventQuery events)
    {
        _events = events;
    }

    /// <summary>Returns the journal rows about <paramref name="subject"/>, untracked and unordered.</summary>
    /// <param name="subject">The user or account.</param>
    /// <returns>A query over the rows; the caller orders and pages it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is neither a user nor an account.</exception>
    public IQueryable<AuditEvent> Of(PersonalDataSubject subject)
    {
        return subject.Kind switch
        {
            PersonalDataSubjectKind.User => _events.InvolvingUser(subject.Id),
            PersonalDataSubjectKind.Account => _events.OfAccount(subject.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(subject), subject.Kind, "Unknown subject kind."),
        };
    }
}
