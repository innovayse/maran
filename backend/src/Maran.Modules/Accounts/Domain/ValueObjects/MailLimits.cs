using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Domain.ValueObjects;

/// <summary>
/// What a plan allows an account to do with mail: how many mailboxes it may hold, how large a new
/// mailbox is by default, how fast one mailbox may send, and how much disk all of its mailboxes may be
/// given together (issue #177, plan decisions D13 and D17).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these four, and why together.</b> The figures are read at one moment, when a mailbox is
/// created or resized, and they constrain each other: a default quota larger than the pool would let
/// the plan sell a mailbox the pool can never hold. They are replaced whole, so a plan never holds one
/// edit's mailbox count beside another edit's pool, which is why they are one value and not four
/// loose fields on the plan.
/// </para>
/// <para>
/// <b>The pool is a ceiling on promises, not a measure of use.</b> A mailbox is given a quota when it
/// is created, and <see cref="MailboxQuotaPoolMib"/> bounds the SUM of those quotas, so an account
/// cannot promise more disk than its plan sold whatever the mailboxes actually hold.
/// </para>
/// <para>
/// <b>One source of the default.</b> <see cref="Default"/> is read from
/// <see cref="MailLimitsSnapshot.Default"/>, the figures every consumer of the Sdk contract already
/// falls back to, so the plan's default and the snapshot's cannot drift apart.
/// </para>
/// </remarks>
public sealed record MailLimits
{
    /// <summary>The largest mailbox count a plan may sell. An overflow and typo guard, far above any real plan.</summary>
    public const int MaxMailboxesBound = 100_000;

    /// <summary>The largest quota, or pool, a plan may state, in mebibytes: ten tebibytes.</summary>
    public const int MaxQuotaMib = 10_485_760;

    /// <summary>The largest hourly send limit a plan may state for one mailbox.</summary>
    public const int MaxSendLimitPerHourBound = 1_000_000;

    /// <summary>The most mailboxes the account may hold; zero sells none.</summary>
    public int MaxMailboxes { get; private set; }

    /// <summary>The quota in mebibytes a new mailbox gets when the customer names none.</summary>
    public int DefaultMailboxQuotaMib { get; private set; }

    /// <summary>The most messages one mailbox may send in an hour.</summary>
    public int MailboxSendLimitPerHour { get; private set; }

    /// <summary>The most mebibytes the quotas of all the account's mailboxes may add up to.</summary>
    public int MailboxQuotaPoolMib { get; private set; }

    /// <summary>
    /// The set a plan carries until its operator states one. A new instance on every read: EF Core
    /// persists the figures as an owned value, and one instance reachable from two plans would be
    /// refused at the save as an owned entity shared between two owners.
    /// </summary>
    /// <value>The figures of <see cref="MailLimitsSnapshot.Default"/>, each read its own instance.</value>
    public static MailLimits Default
    {
        get
        {
            var snapshot = MailLimitsSnapshot.Default;

            return Of(
                snapshot.MaxMailboxes,
                snapshot.DefaultMailboxQuotaMib,
                snapshot.MailboxSendLimitPerHour,
                snapshot.MailboxQuotaPoolMib);
        }
    }

    /// <summary>Builds the figures; reached only through <see cref="Of"/>.</summary>
    /// <param name="maxMailboxes">The mailbox count.</param>
    /// <param name="defaultMailboxQuotaMib">The default quota of a new mailbox.</param>
    /// <param name="mailboxSendLimitPerHour">The hourly send limit of one mailbox.</param>
    /// <param name="mailboxQuotaPoolMib">The pool the quotas are drawn from.</param>
    private MailLimits(int maxMailboxes, int defaultMailboxQuotaMib, int mailboxSendLimitPerHour, int mailboxQuotaPoolMib)
    {
        MaxMailboxes = maxMailboxes;
        DefaultMailboxQuotaMib = defaultMailboxQuotaMib;
        MailboxSendLimitPerHour = mailboxSendLimitPerHour;
        MailboxQuotaPoolMib = mailboxQuotaPoolMib;
    }

    /// <summary>Parameterless constructor required by EF Core materialization of the owned value.</summary>
    private MailLimits()
    {
    }

    /// <summary>States the four figures.</summary>
    /// <param name="maxMailboxes">The mailbox count, from 0 (sells none) to <see cref="MaxMailboxesBound"/>.</param>
    /// <param name="defaultMailboxQuotaMib">The default quota of a new mailbox, from 1 to <see cref="MaxQuotaMib"/>; at most the pool.</param>
    /// <param name="mailboxSendLimitPerHour">The hourly send limit of one mailbox, from 1 to <see cref="MaxSendLimitPerHourBound"/>.</param>
    /// <param name="mailboxQuotaPoolMib">The pool the quotas are drawn from, from 1 to <see cref="MaxQuotaMib"/>.</param>
    /// <returns>The figures.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a figure is outside its range, or the default quota is larger than the pool.
    /// </exception>
    public static MailLimits Of(
        int maxMailboxes,
        int defaultMailboxQuotaMib,
        int mailboxSendLimitPerHour,
        int mailboxQuotaPoolMib)
    {
        Guard(maxMailboxes, 0, MaxMailboxesBound, nameof(maxMailboxes));
        Guard(defaultMailboxQuotaMib, 1, MaxQuotaMib, nameof(defaultMailboxQuotaMib));
        Guard(mailboxSendLimitPerHour, 1, MaxSendLimitPerHourBound, nameof(mailboxSendLimitPerHour));
        Guard(mailboxQuotaPoolMib, 1, MaxQuotaMib, nameof(mailboxQuotaPoolMib));

        if (defaultMailboxQuotaMib > mailboxQuotaPoolMib)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultMailboxQuotaMib),
                defaultMailboxQuotaMib,
                "A new mailbox's default quota cannot be larger than the pool every quota is drawn from.");
        }

        return new MailLimits(maxMailboxes, defaultMailboxQuotaMib, mailboxSendLimitPerHour, mailboxQuotaPoolMib);
    }

    /// <summary>Refuses a figure outside its range.</summary>
    /// <param name="value">The figure to check.</param>
    /// <param name="minimum">The smallest figure allowed.</param>
    /// <param name="maximum">The largest figure allowed.</param>
    /// <param name="parameterName">The parameter the figure came from.</param>
    private static void Guard(int value, int minimum, int maximum, string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, value, $"A mail figure must be from {minimum} to {maximum}.");
        }
    }
}
