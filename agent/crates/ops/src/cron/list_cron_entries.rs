//! ListCronEntries: the crontab's schedules, each one's command from its file.

use maran_agent_core::validation::system::name::AccountName;

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::model::cron_entry::CronEntry;
use crate::cron::model::crontab_document::CrontabDocument;

/// Lists the entries this agent manages for `account`.
///
/// Two reads, because the design puts the two halves of an entry in two places.
/// The crontab gives the id, the schedule and whether cron can see the line;
/// the command comes from the entry's own `.cmd` file, which is where it was
/// written verbatim and where it has stayed. An entry whose file has since gone
/// is listed with no command rather than left out — the schedule is still
/// installed, cron will still try to run it, and hiding it would hide exactly
/// the entry an operator needs to see.
///
/// An account with no crontab lists as empty. That is not an error: it is what
/// every account looks like until the panel installs something.
///
/// Foreign lines are not listed, and no operation in this area can reach one.
/// A crontab may hold entries an administrator wrote by hand; they are carried
/// across every install byte for byte and are never reported as the panel's.
///
/// # Errors
///
/// - [`CronError::CrontabRefused`] when the crontab could not be read.
/// - [`CronError::EntryFileUnreadable`] when a command file is there and could
///   not be read as the entry's own file.
/// - [`CronError::Privilege`] when the account cannot be resolved.
pub fn list_cron_entries(
    host: &dyn CronHost,
    account: &AccountName,
) -> Result<Vec<CronEntry>, CronError> {
    let Some(text) = host.read_crontab(account)? else {
        return Ok(Vec::new());
    };

    let document = CrontabDocument::parse(&text);
    let mut entries = Vec::with_capacity(document.entries().len());

    for entry in document.entries() {
        let command = host
            .read_command_file(account, &entry.id)?
            .as_deref()
            .map(CronEntry::command_from_file);

        entries.push(CronEntry {
            command,
            ..entry.clone()
        });
    }

    Ok(entries)
}

#[cfg(test)]
#[path = "../tests/cron/list_cron_entries_tests.rs"]
mod tests;
