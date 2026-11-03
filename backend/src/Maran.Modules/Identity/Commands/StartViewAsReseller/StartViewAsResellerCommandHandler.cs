using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Commands.StartViewAsReseller;

/// <summary>
/// Handles <see cref="StartViewAsResellerCommand"/>: issues a short-lived view-as session for the
/// login of a reseller, and journals the start with the operator as actor (issues #317, #179).
/// </summary>
/// <remarks>
/// <para>
/// <b>Who may view as a reseller — administrators, and only them.</b> The endpoint's policy is
/// administrator-only and this handler asks the same question of the operator's ROW, so a reseller
/// operator, or one demoted since its token was issued, is refused as <c>ViewAsResellerNotFound</c>
/// (404): the refusal discloses nothing about which resellers exist. A reseller viewing a reseller
/// is not an edge.
/// </para>
/// <para>
/// <b>The token this starts carries role Reseller</b> (<c>IssueViewAs</c> writes the viewed login's
/// role), so every administrator-only endpoint refuses it: an administrator viewing as a reseller
/// keeps no administrator power. It is one hop; no view-as token can start another.
/// </para>
/// <para>
/// A reseller that is suspended, or whose login may not sign in, answers <c>ViewAsLoginUnavailable</c>
/// (409), the same refusal the account kind gives for a login that cannot be signed in as.
/// </para>
/// </remarks>
public sealed class StartViewAsResellerCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The window onto the resellers the accounts module owns.</summary>
    private readonly IResellerDirectory _resellerDirectory;

    /// <summary>Starts the session and records the start and its refusals.</summary>
    private readonly ViewAsStarter _starter;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="resellerDirectory">The window onto the resellers the accounts module owns.</param>
    /// <param name="starter">Starts the session and records the start and its refusals.</param>
    public StartViewAsResellerCommandHandler(
        IdentityDbContext dbContext,
        IResellerDirectory resellerDirectory,
        ViewAsStarter starter)
    {
        _dbContext = dbContext;
        _resellerDirectory = resellerDirectory;
        _starter = starter;
    }

    /// <summary>Starts the view-as session.</summary>
    /// <param name="command">The reseller, and the operator stamped from their own token.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The view-as token and what the banner shows; <c>ViewAsResellerNotFound</c> (404) for a reseller
    /// that does not exist or has no login, or when the operator is not an administrator who may sign
    /// in; <c>ViewAsLoginUnavailable</c> (409) when the reseller is suspended or its login may not
    /// sign in.
    /// </returns>
    public async Task<Result<ViewAsSessionDto>> HandleAsync(
        StartViewAsResellerCommand command,
        CancellationToken cancellationToken)
    {
        var refusedSubject = command.ResellerId.ToString();

        var actor = await _starter.FindOperatorAsync(command.OperatorUserId, cancellationToken);
        if (actor is null || actor.Role != UserRole.Admin)
        {
            await RefuseAsync(command, actor, refusedSubject, cancellationToken);
            return Result<ViewAsSessionDto>.Fail(Error.Of(nameof(ErrorMessages.ViewAsResellerNotFound), ErrorType.NotFound));
        }

        var reseller = await _resellerDirectory.FindAsync(command.ResellerId, cancellationToken);
        var login = reseller is null
            ? null
            : await _dbContext.Users.SingleOrDefaultAsync(
                u => u.ResellerId == reseller.Id && u.Role == UserRole.Reseller,
                cancellationToken);
        if (reseller is null || login is null)
        {
            await RefuseAsync(command, actor, refusedSubject, cancellationToken);
            return Result<ViewAsSessionDto>.Fail(Error.Of(nameof(ErrorMessages.ViewAsResellerNotFound), ErrorType.NotFound));
        }

        var subject = new ViewAsSubject(ViewAsKind.Reseller, reseller.Id, reseller.Name);
        if (reseller.State != ResellerState.Active || !login.MayAuthenticate)
        {
            await RefuseAsync(command, actor, ViewAsSubjectText.Of(subject.Kind, subject.Name, login.Username), cancellationToken);
            return Result<ViewAsSessionDto>.Fail(Error.Of(nameof(ErrorMessages.ViewAsLoginUnavailable), ErrorType.Conflict));
        }

        var started = await _starter.StartAsync(
            actor,
            login,
            subject,
            command.OperatorSessionId,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<ViewAsSessionDto>.Ok(started);
    }

    /// <summary>Journals a refused start with the operator as actor.</summary>
    /// <param name="command">The command, for the operator and the origin.</param>
    /// <param name="actor">The operator's row, when it was found.</param>
    /// <param name="subject">The viewed identity, or the reseller id asked for.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    private async Task RefuseAsync(
        StartViewAsResellerCommand command,
        User? actor,
        string subject,
        CancellationToken cancellationToken)
    {
        await _starter.RecordRefusalAsync(
            command.OperatorUserId,
            actor,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);
    }
}
