namespace Maran.Modules.Accounts.Domain;

/// <summary>
/// A hosting account: the unit of ownership on the server. Backs exactly one Linux user
/// (provisioned by the agent once its Accounts operations exist — not in this pass), carries
/// a plan id that bounds its resource limits, and a suspension state (spec §8).
/// </summary>
public sealed class Account
{
    /// <summary>Creates a new account in the <see cref="AccountStatus.Active"/> state.</summary>
    /// <param name="id">The account's identity.</param>
    /// <param name="name">The account's unique, Linux-username-safe short name.</param>
    /// <param name="primaryDomain">The account's primary domain.</param>
    /// <param name="planId">The id of the plan bounding this account's resource limits.</param>
    /// <param name="createdAt">The instant the account was created, taken from <see cref="IClock"/>.</param>
    public Account(Guid id, string name, string primaryDomain, Guid planId, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        PrimaryDomain = primaryDomain;
        PlanId = planId;
        Status = AccountStatus.Active;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private Account()
    {
        Name = string.Empty;
        PrimaryDomain = string.Empty;
    }

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

    /// <summary>The instant the account was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Marks the account suspended. Idempotent: suspending an already-suspended account is a no-op.</summary>
    public void Suspend() => Status = AccountStatus.Suspended;

    /// <summary>Marks the account active. Idempotent: reactivating an already-active account is a no-op.</summary>
    public void Reactivate() => Status = AccountStatus.Active;
}
