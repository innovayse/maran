using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Domain.ValueObjects;

/// <summary>
/// The figures a plan states, apart from the plan that holds them: what
/// <see cref="Policies.ResellerEnvelopePolicy"/> judges against a <see cref="ResellerEnvelope"/>.
/// </summary>
/// <remarks>
/// It exists so a handler can ask "would this plan fit" BEFORE it creates or edits the plan: an edit
/// that was refused must leave the tracked plan exactly as it was loaded, and applying the edit first
/// in order to judge it would not.
/// </remarks>
/// <param name="DiskQuotaMb">The disk quota of an account on the plan, in megabytes.</param>
/// <param name="MaxSites">The most sites an account on the plan may create.</param>
/// <param name="MaxDatabases">The most databases an account on the plan may create.</param>
/// <param name="MaxSftpUsers">The most SFTP logins an account on the plan may create.</param>
/// <param name="MaxFtpUsers">The most FTPS logins an account on the plan may create.</param>
/// <param name="MaxCronEntries">The most cron entries an account on the plan may keep.</param>
/// <param name="MaxPhpWorkersPerPool">The most php-fpm workers one pool of an account on the plan may run.</param>
/// <param name="Features">The features the plan sells.</param>
/// <param name="RuntimeLimits">The CPU, memory and process ceilings of an account on the plan.</param>
/// <param name="MaxMailboxes">The most mailboxes an account on the plan may hold (issue #177).</param>
/// <param name="MailboxQuotaPoolMib">The most mebibytes the mailbox quotas of an account on the plan may add up to.</param>
public sealed record PlanLimits(
    int DiskQuotaMb,
    int MaxSites,
    int MaxDatabases,
    int MaxSftpUsers,
    int MaxFtpUsers,
    int MaxCronEntries,
    int MaxPhpWorkersPerPool,
    AccountFeature Features,
    RuntimeLimits RuntimeLimits,
    int MaxMailboxes,
    int MailboxQuotaPoolMib);
