using System.Diagnostics;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Reads what a live view-as session is looking at: its kind, its id and its name (issues #317, #314).
/// The banner's answer and the audit subject of the end both come from here, so they cannot disagree.
/// </summary>
/// <remarks>
/// The kind is the viewed login's role, a stored fact, and the id comes from the session (an account)
/// or from the login (a reseller) through <see cref="ViewAsKindPolicy"/>. The name is read through the
/// caller-scoped directories: the request is made with the view-as token, whose scope is exactly the
/// viewed account or the viewed reseller, so the directory answers for it. When the subject has been
/// deleted meanwhile the id stands in for the name, which is the one case a name is not there to read.
/// </remarks>
public sealed class ViewAsSubjectReader
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The tenant-scoped window onto the accounts module, read for an account's name.</summary>
    private readonly IAccountDirectory _accountDirectory;

    /// <summary>The window onto the resellers the accounts module owns, read for a reseller's name.</summary>
    private readonly IResellerDirectory _resellerDirectory;

    /// <summary>Creates the reader.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="accountDirectory">The tenant-scoped window onto the accounts module.</param>
    /// <param name="resellerDirectory">The window onto the resellers the accounts module owns.</param>
    public ViewAsSubjectReader(
        IdentityDbContext dbContext,
        IAccountDirectory accountDirectory,
        IResellerDirectory resellerDirectory)
    {
        _dbContext = dbContext;
        _accountDirectory = accountDirectory;
        _resellerDirectory = resellerDirectory;
    }

    /// <summary>Reads the subject of a view-as session.</summary>
    /// <param name="session">A view-as session.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The subject, or null when the session's facts do not describe an allowed kind consistently
    /// (the viewed login is gone, is an administrator, or disagrees with the session's account).
    /// </returns>
    public async Task<ViewAsSubject?> ReadAsync(Session session, CancellationToken cancellationToken)
    {
        var viewed = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == session.UserId)
            .Select(u => new { u.Role, u.ResellerId })
            .SingleOrDefaultAsync(cancellationToken);

        if (viewed is null
            || !ViewAsKindPolicy.TryDescribe(viewed.Role, viewed.ResellerId, session.ActiveAccountId, out var kind, out var subjectId))
        {
            return null;
        }

        var name = kind switch
        {
            ViewAsKind.Account => (await _accountDirectory.FindAsync(subjectId, cancellationToken))?.Username,
            ViewAsKind.Reseller => (await _resellerDirectory.FindAsync(subjectId, cancellationToken))?.Name,
            _ => throw new UnreachableException("ViewAsKind has exactly two cases: Account and Reseller."),
        };

        return new ViewAsSubject(kind, subjectId, name ?? subjectId.ToString());
    }
}
