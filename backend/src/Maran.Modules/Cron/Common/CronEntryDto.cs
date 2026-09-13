using Maran.Modules.Cron.Services;
namespace Maran.Modules.Cron.Common;

/// <summary>Outward view of one scheduled task: everything a screen shows about it.</summary>
/// <remarks>
/// One DTO for the listing and for a creation alike, because a cron entry has no detail beyond
/// these four things. What the last run left behind is a separate question with a separate cost —
/// reading it is one privileged read per entry — and it has its own type,
/// <see cref="CronEntryOutputDto"/>.
///
/// Nothing here is a panel record: every field is what the agent reported the crontab currently
/// holds. The account can edit that crontab directly, so this is a reading of the server rather
/// than a memory of what the panel installed.
/// </remarks>
/// <param name="EntryId">
/// The agent's identifier for this entry, and the only thing a later request may name it by. Also
/// the audit subject for every operation on it — see <see cref="CronAuditJournal"/> for why the
/// command may not be.
/// </param>
/// <param name="AccountId">The account whose crontab holds this entry.</param>
/// <param name="Schedule">When the entry runs.</param>
/// <param name="Command">
/// The command line exactly as the customer wrote it.
///
/// <b>It is shown here on purpose, and a later reader must not "fix" that.</b> A cron command can
/// legitimately carry a credential — <c>mysql -pSECRET</c>, a URL with a token — which is why it is
/// kept out of every log line and out of the audit journal (<see cref="CronAuditJournal"/>). But it
/// is the CUSTOMER'S OWN text, typed by them, and this response goes back to them: hiding or masking
/// it would leave them unable to read or edit the job they wrote. Sensitive from the operator's
/// logs, not from its owner.
/// </param>
/// <param name="Enabled">
/// True when the entry is a live crontab line. A disabled entry stays in the crontab commented out,
/// so switching one off never loses it.
/// </param>
public sealed record CronEntryDto(
    string EntryId,
    Guid AccountId,
    CronScheduleDto Schedule,
    string Command,
    bool Enabled);
