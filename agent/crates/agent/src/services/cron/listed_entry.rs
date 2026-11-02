//! Turning one entry of an account's crontab into the wire's entry message.

use maran_ops::cron::CronEntry;

use crate::proto::CronEntry as WireEntry;
use crate::proto::CronSchedule as WireSchedule;

/// Builds the wire message for one entry of an account's crontab.
///
/// A listing carries no run information. Reading an entry's exit status means
/// one privileged read per entry under the account's home just to draw a
/// table, so the two fields that used to hold it as zeros are reserved in
/// `cron.proto` (revision 13); `GetCronEntryOutput` answers them, with explicit
/// presence, for the one entry an operator opened.
///
/// The command is the part worth naming. It does not live in the crontab: the
/// installed line names a file, and the listing reads that file back. When the
/// file is not there the entry still exists and still runs, so the entry is
/// reported with an EMPTY command rather than being dropped from the listing or
/// turned into an error — a missing command file is a state an operator needs
/// to see, and an entry silently absent from the panel while cron goes on
/// running it is the worse of the two answers.
///
/// `enabled` and `suspended` are carried as two separate fields because they
/// are two separate facts: the first is the customer's own switch, the second
/// is the account's suspension. An entry runs only when it is enabled and not
/// suspended, and the panel shows the two reasons apart so that resuming an
/// account gives back exactly the entries the customer had running.
#[must_use]
pub fn listed_entry(entry: CronEntry) -> WireEntry {
    WireEntry {
        entry_id: entry.id.as_str().to_owned(),
        schedule: Some(WireSchedule {
            minute: entry.schedule.minute().to_owned(),
            hour: entry.schedule.hour().to_owned(),
            day_of_month: entry.schedule.day_of_month().to_owned(),
            month: entry.schedule.month().to_owned(),
            day_of_week: entry.schedule.day_of_week().to_owned(),
        }),
        command: entry.command.unwrap_or_default(),
        enabled: entry.enabled,
        suspended: entry.suspended,
    }
}

#[cfg(test)]
#[path = "../../tests/services/cron/listed_entry_tests.rs"]
mod tests;
