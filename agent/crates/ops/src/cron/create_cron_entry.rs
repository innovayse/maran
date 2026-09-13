//! CreateCronEntry: the command into a file, then a line that only names it.

use maran_agent_core::validation::system::cron_command::CronCommand;
use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::cron_schedule::CronSchedule;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::cron_lock::cron_lock;
use crate::cron::model::cron_entry::CronEntry;
use crate::cron::model::crontab_document::CrontabDocument;

/// Adds one scheduled entry to `account`'s crontab.
///
/// # The order, and what each step protects
///
/// 1. **Read and parse the crontab.** An absent one parses as an empty
///    document, so an account's first entry needs no special case.
/// 2. **Refuse a duplicate, before anything is written.** Two entries with the
///    same schedule and the same command are one entry the customer can see and
///    one they cannot explain, and a retry after a lost response would create
///    exactly that. The comparison reads each candidate's command back from its
///    own `.cmd` file, because the crontab does not carry commands — the
///    schedule narrows the candidates first, so an account with fifty entries
///    at fifty different times reads no files at all.
/// 3. **Refuse the entry the caller's allowance does not leave room for.** The
///    count is the document just read, which is the whole reason this check is
///    here and not in the caller: the lock taken above is held from before that
///    read to after the install, so the count and the installation are one
///    indivisible act. A caller that counts over a separate call cannot have
///    that — two of its requests interleave between its count and its creation
///    and both install. See `max_entries` below.
///
///    **After the duplicate check and not before it**, because this rpc is
///    idempotent and must stay so. The account that has just filled its
///    allowance is exactly the account whose creation succeeded, so a retry of
///    that creation after a lost response arrives with the crontab full. Asked
///    in the other order it would answer "your plan is full" about an entry that
///    is already installed and running, and the caller would report a failure
///    for work that had in fact been done. Neither check writes anything, so
///    the order costs nothing and buys that.
/// 4. **Mint the id and write the command file.** The customer's command goes
///    into that file verbatim and never anywhere else.
/// 5. **Render the whole document and install it.** Not an append to the file
///    on disk: the render rebuilds every managed line from validated values, so
///    a line somebody tampered with is repaired rather than carried forward.
/// 6. **On a refused install, take the command file away again.** The entry is
///    not in the crontab, so a file left behind is litter inside the customer's
///    home that nothing will ever run and nothing will ever clean up.
///
/// # What reaches the crontab
///
/// Nothing the caller wrote, except the five schedule fields — and those are a
/// [`CronSchedule`], which cannot hold a space, a control character or a `%`.
/// The command is in a file; the line names the file. See
/// [`CrontabDocument`] for the render and for the two designs a real host
/// disproved before this one.
///
/// # What `max_entries` is, and what it is not
///
/// It is the caller's allowance for this account, and `None` means the caller
/// stated none. It is NOT a policy this crate owns: the agent holds no plan, and
/// the number is compared rather than remembered. `None` enforces nothing, which
/// is what any caller predating the allowance needs and is what this operation
/// did before the parameter existed; `Some(0)` is an account permitted no
/// scheduled tasks at all and refuses the first one.
///
/// The caller is still expected to refuse a full plan before calling, because
/// that refusal touches no host and can name the plan. This parameter closes the
/// window between the caller's count and its creation, and nothing else.
///
/// # Errors
///
/// - [`CronError::AlreadyExists`] when the account already has an entry with
///   this schedule and command, enabled or not. Nothing is written. Decided
///   BEFORE the allowance, so an idempotent retry keeps this answer even when
///   the account has since become full.
/// - [`CronError::EntryLimitReached`] when `max_entries` is stated, this is not
///   a repeat of an entry the account already has, and the account already
///   holds at least that many managed entries. Nothing is written and nothing
///   is installed.
/// - [`CronError::EntryIdUnavailable`] when no id could be minted.
/// - [`CronError::EntryFileUnwritable`] when the command file could not be
///   written. Nothing is installed.
/// - [`CronError::CrontabRefused`] when `crontab` refused the table. The
///   command file is removed and the live crontab is what it was.
/// - [`CronError::EntryFileUnreadable`] when an existing entry's command file
///   is there and could not be read, which is checked before anything is
///   written.
/// - [`CronError::Privilege`] when the account cannot be resolved or the
///   privilege drop for the home-side write fails.
pub fn create_cron_entry(
    host: &dyn CronHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    schedule: &CronSchedule,
    command: &CronCommand,
    max_entries: Option<u32>,
) -> Result<CronEntryId, CronError> {
    // Held from before the read to after the install, because the two are one
    // decision: a table rendered from a document read outside the lock would
    // still overwrite whatever landed in between. Per account, process-local —
    // see `cron_lock` for the whole argument.
    let _crontab = cron_lock(account);

    let existing = host.read_crontab(account)?.unwrap_or_default();
    let mut document = CrontabDocument::parse(&existing);

    if is_duplicate(host, account, &document, schedule, command)? {
        return Err(CronError::AlreadyExists);
    }

    if let Some(allowance) = max_entries {
        // Counted from the document read above, inside the lock taken above, so
        // nothing can add an entry between this comparison and the install
        // below. The count is CLAMPED rather than cast: a count is a `usize` and
        // the allowance is a `u32`, and a narrowing cast could wrap a huge count
        // down to a small number and let the entry through — the failure would
        // be silent and in the permissive direction. A crontab with more than
        // `u32::MAX` entries cannot exist, so the clamp never fires; it is here
        // so that no cast can.
        let held = u32::try_from(document.entries().len()).unwrap_or(u32::MAX);

        if held >= allowance {
            return Err(CronError::EntryLimitReached);
        }
    }

    let id = host.new_entry_id()?;
    host.write_command_file(account, &id, command)?;

    document.append(CronEntry {
        id: id.clone(),
        schedule: schedule.clone(),
        enabled: true,
        // The account's own state, not a choice this operation makes: an entry
        // created while the account is suspended must not start firing. The
        // render normalises every managed line to the document's flag anyway,
        // so this says out loud what the table is about to say.
        suspended: document.is_suspended(),
        command: None,
    });

    if let Err(refusal) =
        host.install_crontab(account, &document.render(account, distro.sh_binary()))
    {
        // Best effort, and its failure is deliberately not reported: the
        // operation already failed for a reason worth reporting, and replacing
        // that reason with "cleanup failed" would hide it.
        let _ = host.remove_entry_files(account, &id);

        return Err(refusal);
    }

    Ok(id)
}

/// Reports whether `account` already has an entry with this schedule and
/// command.
///
/// The schedule is compared first because it is already in memory, and only an
/// entry that matches it has its command file read. That is not only cheaper —
/// it is what keeps a listing-sized amount of privileged file reading out of
/// the common case where a customer adds one more entry at a new time.
///
/// # Errors
///
/// Returns [`CronError::EntryFileUnreadable`] when a candidate's file is there
/// and cannot be read, and [`CronError::Privilege`] when the account cannot be
/// resolved. An entry whose file is simply absent is not a duplicate: there is
/// no command there to be the same as this one.
fn is_duplicate(
    host: &dyn CronHost,
    account: &AccountName,
    document: &CrontabDocument,
    schedule: &CronSchedule,
    command: &CronCommand,
) -> Result<bool, CronError> {
    for entry in document.entries() {
        if entry.schedule != *schedule {
            continue;
        }

        let Some(contents) = host.read_command_file(account, &entry.id)? else {
            continue;
        };

        if CronEntry::command_from_file(&contents) == command.as_str() {
            return Ok(true);
        }
    }

    Ok(false)
}

#[cfg(test)]
#[path = "../tests/cron/create_cron_entry_tests.rs"]
mod tests;
