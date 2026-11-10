using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.SharedKernel.Constants;

namespace Maran.Modules.Accounts.Domain.Entities;

/// <summary>
/// A hosting account: the unit of ownership on the server. Backs exactly one Linux user
/// (provisioned by the agent once its Accounts operations exist — not in this pass), carries
/// a plan id that bounds its resource limits, and a suspension state (spec §8).
/// </summary>
/// <remarks>
/// <b><see cref="OwnerEmail"/> is not a duplicate of <c>Identity.Domain.Entities.User.Email</c>.</b>
/// This one is the account's own contact address — the administrator's record of who to reach about
/// the hosting itself — kept independently of any login. <c>User.Email</c> is the address the
/// login's own mails (its invitation, its password resets) are sent to, and Identity owns it. They
/// start equal, because <c>CreateAccountCommand.OwnerEmail</c> is the same value
/// <see cref="Maran.Sdk.Events.AccountCreated"/> carries to the login Identity creates, and an
/// administrator's change of the owner address (<c>ChangeAccountOwnerEmailCommandHandler</c>) moves
/// both together, so the invitation and the password-reset mails follow it. An account with no login
/// at that moment has nothing to move; its login is later recreated from this row
/// (<c>InvitationIssuer</c>) and so starts from the new address. Storing it here — rather than solely on the login, as a single
/// unduplicated copy — is the deliberate correction to that earlier design: the login and its
/// contact address used to be inseparable, and a login that failed to get created (an
/// <c>AccountCreatedHandler</c> that never reached <c>SaveChanges</c>) took the account's only
/// record of its owner's address down with it, with no repair short of deleting and recreating a
/// live account. A second copy that can drift is a smaller cost than an account nobody can invite.
/// </remarks>
public sealed class Account
{
    /// <summary>The account's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account's unique short name (the eventual system user name), in the agent's account alphabet and never a reserved system name.</summary>
    public string Name { get; private set; }

    /// <summary>The account's primary domain.</summary>
    public string PrimaryDomain { get; private set; }

    /// <summary>The id of the plan bounding this account's resource limits.</summary>
    public Guid PlanId { get; private set; }

    /// <summary>The account's current lifecycle state.</summary>
    public AccountStatus Status { get; private set; }

    /// <summary>
    /// Who suspended the account while <see cref="Status"/> is <see cref="AccountStatus.Suspended"/>,
    /// and <c>null</c> while it is active. A suspended account whose value is <c>null</c> was
    /// suspended before this column existed, or by a release that did not write it; its origin is
    /// unknown and <see cref="IsReleasableBy"/> reads it as an administrator's, the stricter reading,
    /// because the alternative lets a reseller undo an administrator's suspension (issue #493).
    /// </summary>
    public SuspensionActor? SuspendedBy { get; private set; }

    /// <summary>
    /// The account's own contact address — see the type's remarks for why this is not the same
    /// datum as the login's address, and why both exist. <c>null</c> only for an account created
    /// before this column existed: nothing in the panel ever recorded that account's owner address
    /// anywhere durable, so there is nothing to backfill it from, and a null here means exactly
    /// "unknown", never "this account has no contact address by design".
    /// </summary>
    public string? OwnerEmail { get; private set; }

    /// <summary>
    /// The owner's language, one of <see cref="PanelLanguages.Supported"/>: the language the owner's
    /// invitation is written in, at creation and on every resend (issue #194). Chosen by whoever
    /// creates the account, because the creator's own interface language says nothing about the
    /// owner's.
    /// </summary>
    public string OwnerLanguage { get; private set; }

    /// <summary>
    /// The reseller that owns the account, or <c>null</c> for an account the administrator owns, which
    /// is what every account that existed before resellers is. Set once, at creation, and never
    /// edited: moving an account between owners is not an operation the panel has. An account and its
    /// plan always share an owner (<see cref="Plan.ResellerId"/>).
    /// </summary>
    public Guid? ResellerId { get; private set; }

    /// <summary>
    /// Whether the account's system user has an interactive login shell instead of none (issue #194).
    /// Chosen when the account is created and never edited since. The agent keeps no record of the
    /// shell it gave a user and gives <c>nologin</c> back on every unsuspension unless told otherwise,
    /// so the panel is the one place this survives, and it is passed to the agent whenever the shell
    /// is restored.
    /// </summary>
    public bool ShellAccess { get; private set; }

    /// <summary>
    /// The inode, process and open-file ceilings this account holds in place of its plan's; empty when
    /// it follows the plan for all of them. Set once, at creation, and never edited.
    /// </summary>
    public AccountLimitOverrides LimitOverrides { get; private set; } = AccountLimitOverrides.None();

    /// <summary>The instant the account was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Creates a new account in the <see cref="AccountStatus.Active"/> state.</summary>
    /// <param name="id">The account's identity.</param>
    /// <param name="name">The account's unique, Linux-username-safe short name.</param>
    /// <param name="primaryDomain">The account's primary domain.</param>
    /// <param name="planId">The id of the plan bounding this account's resource limits.</param>
    /// <param name="createdAt">The instant the account was created, taken from <see cref="IClock"/>.</param>
    /// <param name="ownerEmail">
    /// The account's own contact address, from whoever created it. Defaults to empty only because
    /// this constructor is called positionally at dozens of test sites that predate this parameter
    /// and a required one would stop them building — the same temporary-default shape
    /// <c>AccountSnapshot.MaxFtpUsers</c> already carries, and owed the same cleanup. The one
    /// production call site, <c>CreateAccountCommandHandler</c>, always passes a real, validated
    /// address.
    /// </param>
    /// <param name="ownerLanguage">
    /// The owner's language. Defaulted to English for the same test-site reason as
    /// <paramref name="ownerEmail"/>; <c>CreateAccountCommandHandler</c> always passes the validated
    /// choice.
    /// </param>
    /// <param name="resellerId">
    /// The reseller that owns the account, or <c>null</c> for the administrator. Defaulted to the
    /// administrator for the same test-site reason as <paramref name="ownerEmail"/>; the account
    /// creation handler always states it.
    /// </param>
    /// <param name="shellAccess">
    /// Whether the system user has a login shell. Defaulted to none, which is what every account that
    /// existed before the choice had.
    /// </param>
    /// <param name="limitOverrides">
    /// The ceilings the account holds in place of its plan's; <c>null</c> follows the plan for all.
    /// </param>
    public Account(
        Guid id,
        string name,
        string primaryDomain,
        Guid planId,
        DateTimeOffset createdAt,
        string ownerEmail = "",
        string ownerLanguage = PanelLanguages.English,
        Guid? resellerId = null,
        bool shellAccess = false,
        AccountLimitOverrides? limitOverrides = null)
    {
        Id = id;
        Name = name;
        PrimaryDomain = primaryDomain;
        PlanId = planId;
        OwnerEmail = ownerEmail;
        OwnerLanguage = ownerLanguage;
        ResellerId = resellerId;
        ShellAccess = shellAccess;
        LimitOverrides = limitOverrides ?? AccountLimitOverrides.None();
        Status = AccountStatus.Active;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private Account()
    {
        Name = string.Empty;
        PrimaryDomain = string.Empty;
        OwnerLanguage = PanelLanguages.English;
    }

    /// <summary>Moves the account onto another plan, which from now on bounds its limits.</summary>
    /// <param name="planId">The plan the account is now sold.</param>
    /// <remarks>
    /// Whether the account FITS the plan is not this method's question: the disk usage it would have
    /// to fit under lives on the host, and only the handler can ask the agent
    /// (<c>ChangeAccountPlanCommandHandler</c>).
    /// </remarks>
    public void ChangePlan(Guid planId)
    {
        PlanId = planId;
    }

    /// <summary>Hands the account to another owner, together with the plan of that owner it is sold from.</summary>
    /// <param name="resellerId">The reseller that now owns the account; <c>null</c> for the administrator.</param>
    /// <param name="planId">The plan the account is now sold; it must have the same owner.</param>
    /// <remarks>
    /// Takes the owner and the plan in ONE call because the pair is the invariant: an account and its plan
    /// always share an owner, so a method that changed the owner alone would leave a row the reseller's
    /// list cannot show. That the plan really has this owner, that the new owner has room for the
    /// account, and that the host carries the plan's figures are the handler's to prove
    /// (<c>TransferAccountCommandHandler</c>), as they need the plan's row, the owner's envelope and the
    /// agent.
    /// </remarks>
    public void TransferTo(Guid? resellerId, Guid planId)
    {
        ResellerId = resellerId;
        PlanId = planId;
    }

    /// <summary>Replaces the account's own contact address.</summary>
    /// <param name="ownerEmail">The new, validated address.</param>
    public void ChangeOwnerEmail(string ownerEmail)
    {
        OwnerEmail = ownerEmail;
    }

    /// <summary>
    /// Marks the account suspended and records who did it. Idempotent: suspending an already-suspended
    /// account changes nothing, except that an administrator's suspension replaces a reseller's and is
    /// never weakened back to one, so the strongest hold is the one that is kept.
    /// </summary>
    /// <param name="suspendedBy">Who is suspending the account.</param>
    public void Suspend(SuspensionActor suspendedBy)
    {
        Status = AccountStatus.Suspended;

        if (SuspendedBy != SuspensionActor.Administrator)
        {
            SuspendedBy = suspendedBy;
        }
    }

    /// <summary>
    /// Whether <paramref name="releasedBy"/> may lift the suspension: an administrator always may, a
    /// reseller only a suspension a reseller placed, and anybody may "lift" an account that is not
    /// suspended (reactivating an active account is the idempotent no-op).
    /// </summary>
    /// <param name="releasedBy">Who is asking to lift the suspension.</param>
    /// <returns><c>true</c> when the suspension may be lifted by that actor.</returns>
    public bool IsReleasableBy(SuspensionActor releasedBy)
    {
        return Status == AccountStatus.Active
            || releasedBy == SuspensionActor.Administrator
            || SuspendedBy == SuspensionActor.Reseller;
    }

    /// <summary>Marks the account active and forgets who suspended it. Idempotent: reactivating an already-active account is a no-op.</summary>
    public void Reactivate()
    {
        Status = AccountStatus.Active;
        SuspendedBy = null;
    }
}
