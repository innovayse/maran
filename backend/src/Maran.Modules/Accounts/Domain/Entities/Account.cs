using Maran.Modules.Accounts.Domain.Enums;
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
    public Account(
        Guid id,
        string name,
        string primaryDomain,
        Guid planId,
        DateTimeOffset createdAt,
        string ownerEmail = "",
        string ownerLanguage = PanelLanguages.English)
    {
        Id = id;
        Name = name;
        PrimaryDomain = primaryDomain;
        PlanId = planId;
        OwnerEmail = ownerEmail;
        OwnerLanguage = ownerLanguage;
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

    /// <summary>Replaces the account's own contact address.</summary>
    /// <param name="ownerEmail">The new, validated address.</param>
    public void ChangeOwnerEmail(string ownerEmail)
    {
        OwnerEmail = ownerEmail;
    }

    /// <summary>Marks the account suspended. Idempotent: suspending an already-suspended account is a no-op.</summary>
    public void Suspend()
    {
        Status = AccountStatus.Suspended;
    }

    /// <summary>Marks the account active. Idempotent: reactivating an already-active account is a no-op.</summary>
    public void Reactivate()
    {
        Status = AccountStatus.Active;
    }
}
