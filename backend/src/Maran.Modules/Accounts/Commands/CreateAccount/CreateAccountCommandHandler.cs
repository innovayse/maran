using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Models;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Maran.Modules.Accounts.Commands.CreateAccount;

/// <summary>
/// Handles <see cref="CreateAccountCommand"/>: provisions the account's Linux system user through
/// the agent, then records the account row (spec §8 — an Account IS a system user, and the
/// isolation between customers is the operating system's, not the panel's).
/// </summary>
/// <remarks>
/// The agent runs first and the row is written only if it succeeded, the same order the suspend,
/// reactivate and delete handlers use, and for the same reason: the two stores can still disagree
/// if the database write fails afterwards, and this order decides WHICH way. A system user with no
/// row is invisible but harmless, and creating the account again converges because the agent's
/// operations are idempotent. The reverse — a row the panel shows as a live account with no user,
/// no home directory and no quota behind it — is a customer who has been sold something that does
/// not exist, and every later operation on it fails.
///
/// <b>A reseller's account is admitted before anything is provisioned.</b> The owner is decided
/// first (<see cref="ResellerScope.ResolveOwner"/>), the plan must be that owner's own, and the
/// owner's envelope must have a free account slot and room in its disk pool
/// (<see cref="ResellerAllocationGuard"/>). The admission also claims the reseller's row, and the
/// claim is only decided at the save: a second creation admitted against the same slot loses it
/// there, and the system user the agent already made for it is removed again, exactly as when the
/// runtime ceilings are refused.
///
/// The quota travels with the creation call rather than as a second round trip: the plan's
/// <see cref="Plan.DiskQuotaMb"/> is part of what the account IS, and an account that briefly
/// exists without one is an account that briefly has the whole disk. So do the inode ceiling and the
/// login shell the creator chose (issue #194).
///
/// <b>What the account holds is its own figure, else its plan's.</b> The ceilings the creator states
/// are decided by <see cref="AccountLimitOverrideResolver"/> before anything is provisioned, stored on
/// the row, and pushed to the host with the plan's (<see cref="AccountRuntimeLimitsEnforcer"/>); the
/// open-files ceiling, which no plan states, is pushed after the user exists, and a refusal there
/// undoes the user as a refused runtime ceiling does.
///
/// <b>A weak initial password is refused before the account exists.</b> The login is issued after the
/// account, so the password is judged first through <see cref="IInitialPasswordPolicy"/>, the window
/// Identity implements; Accounts holds no copy of the policy.
///
/// <b>The owner's login is announced with how it is to be made.</b> The password an administrator
/// states travels to Identity only on <see cref="AccountCreated"/>, as a
/// <see cref="Maran.SharedKernel.Security.SensitiveString"/>; it is not stored on the row and not
/// journalled, which lists the mode only (<see cref="AccountAuditJournal.RecordCreatedAsync"/>).
/// </remarks>
public sealed class CreateAccountCommandHandler
{
    /// <summary>
    /// Pre-compiled log delegate for an Identity subscriber that failed to issue the account owner's
    /// login. Source-generated because the subscriber's own exception text has no place in a
    /// customer-facing message (rules/security.md item 8) — it is logged here, for the operator, and
    /// never returned.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> LogLoginIssuanceFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(CreateAccountCommandHandler)),
            "Account {AccountName} was created, but its owner's login could not be issued.");

    /// <summary>
    /// The one log line for a system user whose creation could not be undone after its runtime
    /// ceilings were refused: the operator must remove it by hand before the name can be created again.
    /// </summary>
    private static readonly Action<ILogger, string, string, Exception?> LogRollbackFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(2, nameof(CreateAccountCommandHandler)),
            "Account {AccountName} was provisioned but its runtime ceilings were refused, and removing it failed ({Code}); the system user is left behind.");

    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Says who owns what the caller creates, and whether a suspended reseller may still write.</summary>
    private readonly ResellerScope _scope;

    /// <summary>Holds the account to the owner's envelope and claims the owner's row.</summary>
    private readonly ResellerAllocationGuard _allocation;

    /// <summary>Answers, across every tenant, whether a name or a domain is already claimed.</summary>
    private readonly AccountNamespaceClaims _claims;

    /// <summary>Names the owning reseller in the answer.</summary>
    private readonly ResellerNameDirectory _resellerNames;

    /// <summary>The agent, which owns everything about the account that lives on the host.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>Puts the account's runtime ceilings on its slice and its open-files ceiling on the host.</summary>
    private readonly AccountRuntimeLimitsEnforcer _runtimeLimits;

    /// <summary>Holds the ceilings the creator stated to the plan and to who is creating.</summary>
    private readonly AccountLimitOverrideResolver _overrides;

    /// <summary>Judges an initial password by the panel's password policy, which Identity owns.</summary>
    private readonly IInitialPasswordPolicy _passwordPolicy;

    /// <summary>Tells whether the owner's login name and e-mail address are free.</summary>
    private readonly IOwnerLoginDirectory _ownerLogins;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>The bus <see cref="AccountCreated"/> is invoked on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Where a subscriber's failure to issue the owner's login is recorded.</summary>
    private readonly ILogger<CreateAccountCommandHandler> _logger;

    /// <summary>Names the account's state in the response, in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler with the module's own database context, the agent, the clock and the bus.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="scope">Says who owns what the caller creates, and whether a suspended reseller may still write.</param>
    /// <param name="allocation">Holds the account to the owner's envelope and claims the owner's row.</param>
    /// <param name="claims">Answers, across every tenant, whether a name or a domain is already claimed.</param>
    /// <param name="resellerNames">Names the owning reseller in the answer.</param>
    /// <param name="agent">The agent client that provisions the system user.</param>
    /// <param name="runtimeLimits">Puts the account's runtime ceilings on its slice and its open-files ceiling on the host.</param>
    /// <param name="overrides">Holds the ceilings the creator stated to the plan and to who is creating.</param>
    /// <param name="passwordPolicy">Judges an initial password by the panel's password policy.</param>
    /// <param name="ownerLogins">Tells whether the owner's login name and e-mail address are free.</param>
    /// <param name="clock">The injected time source used to stamp the new account's creation time.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="bus">The bus <see cref="AccountCreated"/> is invoked on.</param>
    /// <param name="logger">Where a subscriber's failure to issue the owner's login is recorded.</param>
    /// <param name="displayNames">Names the account's state in the response, in the request's culture.</param>
    public CreateAccountCommandHandler(
        AccountsDbContext dbContext,
        ResellerScope scope,
        ResellerAllocationGuard allocation,
        AccountNamespaceClaims claims,
        ResellerNameDirectory resellerNames,
        IAgentAccountsClient agent,
        AccountRuntimeLimitsEnforcer runtimeLimits,
        AccountLimitOverrideResolver overrides,
        IInitialPasswordPolicy passwordPolicy,
        IOwnerLoginDirectory ownerLogins,
        IClock clock,
        AccountAuditJournal journal,
        IMessageBus bus,
        ILogger<CreateAccountCommandHandler> logger,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _scope = scope;
        _allocation = allocation;
        _claims = claims;
        _resellerNames = resellerNames;
        _agent = agent;
        _runtimeLimits = runtimeLimits;
        _overrides = overrides;
        _passwordPolicy = passwordPolicy;
        _ownerLogins = ownerLogins;
        _clock = clock;
        _journal = journal;
        _bus = bus;
        _logger = logger;
        _displayNames = displayNames;
    }

    /// <summary>
    /// Creates the account row, guarding against a duplicate name or domain.
    /// </summary>
    /// <param name="command">The validated account parameters; see <see cref="CreateAccountCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// The created account, <c>AccountNameTaken</c>/<c>AccountDomainTaken</c>/<c>PlanNotFound</c>,
    /// <c>ResellerSuspended</c>, <c>ResellerNotFound</c>, <c>PlanOwnerMismatch</c>,
    /// <c>ResellerAccountLimitReached</c> or <c>ResellerDiskPoolExceeded</c> for a reseller's account,
    /// <c>AccountShellAccessAdministratorOnly</c>/<c>AccountInitialPasswordAdministratorOnly</c> when a reseller
    /// asks for a login shell or states the owner's first password (issue #488),
    /// <c>AccountLimitAbovePlan</c> when a reseller states a process ceiling above its plan's,
    /// <c>AccountInitialPasswordTooShort</c>/<c>AccountInitialPasswordTooLong</c>/<c>AccountInitialPasswordSameAsName</c>
    /// naming the rule the initial password breaks in the panel's password policy,
    /// <c>AccountOwnerEmailTaken</c>/<c>AccountLoginNameTaken</c> when another login already holds the owner's
    /// e-mail address or the account's name as a login name (nothing is provisioned),
    /// the agent's own typed failure when the system user could not be provisioned, or
    /// <c>AccountLoginIssuanceFailed</c> when the account was created but its owner's login was not
    /// — see the remarks on the invocation below for why creating the account again is not the
    /// recovery from that one.
    /// </returns>
    public async Task<Result<AccountDto>> HandleAsync(CreateAccountCommand command, CancellationToken cancellationToken)
    {
        var inactive = await _scope.RequireActiveAsync(cancellationToken);
        if (inactive is not null)
        {
            return await FailAsync(command, inactive, cancellationToken);
        }

        var owner = _scope.ResolveOwner(command.ResellerId);
        if (!owner.IsSuccess)
        {
            return await FailAsync(command, owner.Error!, cancellationToken);
        }

        var ownerId = owner.Value;

        var administratorOnly = RefuseAdministratorOnlyOptions(command);
        if (administratorOnly is not null)
        {
            return await FailAsync(command, administratorOnly, cancellationToken);
        }

        if (await _claims.IsNameTakenAsync(command.Name, cancellationToken))
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.AccountNameTaken), ErrorType.Conflict), cancellationToken);
        }

        if (await _claims.IsDomainTakenAsync(command.PrimaryDomain, cancellationToken))
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.AccountDomainTaken), ErrorType.Conflict), cancellationToken);
        }

        var plan = await _dbContext.Plans
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == command.PlanId, cancellationToken);
        if (plan is null)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.PlanNotFound), ErrorType.NotFound), cancellationToken);
        }

        var limits = _overrides.Resolve(command.InodeLimit, command.ProcessLimit, command.OpenFilesLimit, plan);
        if (!limits.IsSuccess)
        {
            return await FailAsync(command, limits.Error!, cancellationToken);
        }

        // Judged before anything exists on the host: the login is issued after the account, so a weak
        // password found there would leave an account nobody can sign in to (issue #194).
        if (command.InitialPassword is not null)
        {
            var verdict = await _passwordPolicy.JudgeAsync(command.InitialPassword, command.Name, cancellationToken);
            if (verdict != InitialPasswordVerdict.Accepted)
            {
                return await FailAsync(
                    command,
                    Error.Of(InitialPasswordRefusalCode(verdict), ErrorType.Validation),
                    cancellationToken);
            }
        }

        // Also before anything exists on the host: the owner's login is made after the account, and a
        // name or an e-mail address another login holds would leave an account with no way in.
        var loginConflict = await _ownerLogins.FindConflictAsync(command.Name, command.OwnerEmail, cancellationToken);
        if (loginConflict != OwnerLoginConflict.None)
        {
            var code = loginConflict == OwnerLoginConflict.EmailTaken
                ? nameof(ErrorMessages.AccountOwnerEmailTaken)
                : nameof(ErrorMessages.AccountLoginNameTaken);

            return await FailAsync(command, Error.Of(code, ErrorType.Conflict), cancellationToken);
        }

        var refusal = await _allocation.AdmitAccountAsync(ownerId, plan, cancellationToken)
            ?? ResellerAllocationGuard.RequirePlanOwnedBy(ownerId, plan);
        if (refusal is not null)
        {
            return await FailAsync(command, refusal.Error, cancellationToken);
        }

        var provisioning = new AccountProvisioningDto(
            (ulong?)limits.Value.InodeLimit,
            AccountLoginShellMapper.ToAgent(command.ShellAccess));
        var provisioned = await _agent.CreateAsync(command.Name, QuotaBytes(plan), provisioning, cancellationToken);
        if (!provisioned.IsSuccess)
        {
            return await FailAsync(command, provisioned.Error!, cancellationToken);
        }

        // Always pushed, unlimited included: a uid recycled from a deleted account must not keep a
        // ceiling its previous owner's plan put on the slice. A refusal undoes the system user, as the
        // agent undoes its own half-made creation: an account the panel has no row for blocks its name
        // for good, because the agent never adopts an existing user. The open-files ceiling follows the
        // slice's, and only when the account states one: no plan does, and a new account has no
        // drop-in to clear.
        var ceilingFailure =
            await _runtimeLimits.ApplyAsync(
                [new AccountLimitTarget(command.Name, limits.Value)], plan.RuntimeLimits, cancellationToken)
            ?? await _runtimeLimits.ApplyOpenFilesAsync(command.Name, limits.Value, cancellationToken);
        if (ceilingFailure is not null)
        {
            await RemoveSystemUserAsync(command.Name, cancellationToken);

            return await FailAsync(command, ceilingFailure, cancellationToken);
        }

        var account = new Account(
            Guid.NewGuid(),
            command.Name,
            command.PrimaryDomain,
            command.PlanId,
            _clock.UtcNow,
            command.OwnerEmail,
            command.OwnerLanguage,
            ownerId,
            command.ShellAccess,
            limits.Value);

        _dbContext.Accounts.Add(account);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The owner's row was claimed by another write between the admission above and this save:
            // the slot the admission counted is gone. The system user made for it is removed again,
            // and the refusal is the one the admission would have given had this creation run second.
            await RemoveSystemUserAsync(command.Name, cancellationToken);

            return await FailAsync(
                command,
                Error.Of(nameof(ErrorMessages.ResellerAccountLimitReached), ErrorType.Conflict),
                cancellationToken);
        }

        await _journal.RecordCreatedAsync(
            account.Name,
            new AccountCreationOptions(command.LoginMode, command.ShellAccess, command.BackupEnabled, limits.Value),
            command.IpAddress,
            command.UserAgent,
            account.Id,
            cancellationToken);

        // INVOKED, not published: a failure to issue the login must reach the caller. The account
        // already exists at this point and is not rolled back — see AccountCreated's remarks — so
        // the failure is reported and "resend invitation" is the recovery. Caught rather than left
        // to propagate, exactly as DeleteAccountCommandHandler catches AccountDeleting's subscribers:
        // an uncaught throw here would reach the caller as a generic 500 through ExceptionMiddleware,
        // reading as "creation failed" when the account is in fact sitting there without a login —
        // and retrying creation would only hit AccountNameTaken above, not fix anything.
        try
        {
            await _bus.InvokeAsync(
                new AccountCreated(
                    account.Id,
                    account.Name,
                    command.OwnerEmail,
                    account.OwnerLanguage,
                    command.LoginMode,
                    command.InitialPassword,
                    command.BackupEnabled,
                    command.SuppressInvitationMail),
                cancellationToken);
        }
        catch (Exception exception)
        {
            // The subscriber's own text is never customer-facing (rules/security.md item 8); it is
            // logged here, for the operator, and answered with a typed code instead. The success
            // journal entry above stands: the account WAS created, so this is not re-journalled as a
            // failed AccountCreated — that would contradict the entry an operator already has.
            LogLoginIssuanceFailed(_logger, account.Name, exception);

            return Result<AccountDto>.Fail(Error.Of(nameof(ErrorMessages.AccountLoginIssuanceFailed), ErrorType.Failure));
        }

        return Result<AccountDto>.Ok(
            AccountMapper.ToDto(
                account,
                _displayNames,
                await _resellerNames.NameOfAsync(account.ResellerId, cancellationToken)));
    }

    /// <summary>
    /// Removes the system user the agent made for an account whose row could not be written, so its
    /// name is not blocked for good (the agent never adopts an existing user). A removal that fails is
    /// logged for the operator, who must then remove the user by hand.
    /// </summary>
    /// <param name="name">The account's name, which is the system user's.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    private async Task RemoveSystemUserAsync(string name, CancellationToken cancellationToken)
    {
        var undone = await _agent.DeleteAsync(name, cancellationToken);
        if (!undone.IsSuccess)
        {
            LogRollbackFailed(_logger, name, undone.Error!.Code, null);
        }
    }

    /// <summary>Journals a refused creation and returns it as the typed failure.</summary>
    /// <remarks>
    /// The subject is the name the caller asked for, which is the only thing that exists to name:
    /// no account was created. A refusal is worth journalling here because "that name is taken" and
    /// "that domain is taken" are how one caller learns what another customer holds, so a run of
    /// them from one address is exactly the pattern the journal exists to make visible.
    /// </remarks>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<AccountDto>> FailAsync(
        CreateAccountCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AccountAuditActions.AccountCreated, command.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<AccountDto>.Fail(error);
    }

    /// <summary>
    /// Refuses the two options that hand the creator a way into the customer's account: a login shell,
    /// and a first password the creator chose. Both are the administrator's alone; a reseller, or an
    /// integration holding a reseller's API key, creates an account the customer alone can sign in to.
    /// </summary>
    /// <remarks>
    /// Decided from the caller's identity here and not from the request, so a body cannot talk its way
    /// past it, and before anything is provisioned so a refusal leaves nothing on the host. A reseller
    /// is not trusted with either by default: a shell is a way to run code as the account on a server
    /// shared with every other tenant, and a password the reseller knows is a way to sign in as the
    /// customer. The reseller's remaining way to give the owner a login is the invitation, which keeps
    /// the password with the owner alone (issue #488).
    /// </remarks>
    /// <param name="command">The creation being judged.</param>
    /// <returns>
    /// <c>null</c> when the caller is not a reseller, or asks for neither option;
    /// <c>AccountShellAccessAdministratorOnly</c> or <c>AccountInitialPasswordAdministratorOnly</c> otherwise.
    /// </returns>
    private Error? RefuseAdministratorOnlyOptions(CreateAccountCommand command)
    {
        if (_scope.CallerResellerId is null)
        {
            return null;
        }

        if (command.ShellAccess)
        {
            return Error.Of(nameof(ErrorMessages.AccountShellAccessAdministratorOnly), ErrorType.Forbidden);
        }

        if (command.LoginMode != OwnerLoginMode.Invitation || command.InitialPassword is not null)
        {
            return Error.Of(nameof(ErrorMessages.AccountInitialPasswordAdministratorOnly), ErrorType.Forbidden);
        }

        return null;
    }

    /// <summary>Names the error code for the rule an initial password broke.</summary>
    /// <param name="verdict">The policy's verdict; never <see cref="InitialPasswordVerdict.Accepted"/>.</param>
    /// <returns>The code of the sentence that tells the administrator which rule to fix.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The verdict is not a refusal; a caller bug, never user input.</exception>
    private static string InitialPasswordRefusalCode(InitialPasswordVerdict verdict)
    {
        return verdict switch
        {
            InitialPasswordVerdict.TooShort => nameof(ErrorMessages.AccountInitialPasswordTooShort),
            InitialPasswordVerdict.TooLong => nameof(ErrorMessages.AccountInitialPasswordTooLong),
            InitialPasswordVerdict.SameAsUsername => nameof(ErrorMessages.AccountInitialPasswordSameAsName),
            _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Only a refusal has a code."),
        };
    }

    /// <summary>Converts a plan's disk allowance to the bytes the agent's quota call expects.</summary>
    /// <param name="plan">The plan the account is created against.</param>
    /// <returns>The quota in bytes.</returns>
    private static ulong QuotaBytes(Plan plan)
    {
        return (ulong)plan.DiskQuotaMb * 1024UL * 1024UL;
    }
}
