using Maran.Modules.Accounts.Domain.Enums;
namespace Maran.Modules.Accounts.Domain.Entities;

/// <summary>
/// A hosting account: the unit of ownership on the server. Backs exactly one Linux user
/// (provisioned by the agent once its Accounts operations exist — not in this pass), carries
/// a plan id that bounds its resource limits, and a suspension state (spec §8).
/// </summary>
/// <remarks>
/// <b><see cref="OwnerEmail"/> is not a duplicate of <c>Identity.Domain.Entities.User.Email</c>,
/// and the two are allowed to diverge.</b> This one is the account's own contact address — the
/// administrator's record of who to reach about the hosting itself — and it is set once, from
/// whoever created the account, independently of any login. <c>User.Email</c> is a login's own
/// address, changeable by whoever holds that login without touching the account it owns. They
/// start equal, because <c>CreateAccountCommand.OwnerEmail</c> is the same value
/// <see cref="Maran.Sdk.Events.AccountCreated"/> carries to the login Identity creates, but nothing
/// keeps them equal afterwards. Storing it here — rather than solely on the login, as a single
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

    /// <summary>The account's unique, Linux-username-safe short name (the eventual system user name).</summary>
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
    public Account(
        Guid id, string name, string primaryDomain, Guid planId, DateTimeOffset createdAt, string ownerEmail = "")
    {
        Id = id;
        Name = name;
        PrimaryDomain = primaryDomain;
        PlanId = planId;
        OwnerEmail = ownerEmail;
        Status = AccountStatus.Active;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private Account()
    {
        Name = string.Empty;
        PrimaryDomain = string.Empty;
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
