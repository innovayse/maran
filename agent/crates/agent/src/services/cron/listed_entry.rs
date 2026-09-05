//! Turning one entry of an account's crontab into the wire's entry message.

use maran_ops::cron::CronEntry;

use crate::proto::CronEntry as WireEntry;
use crate::proto::CronSchedule as WireSchedule;

/// The exit code a LISTED entry reports.
///
/// Zero, and it means "not read" rather than "the last run succeeded" —
/// `cron.proto` says so on the field. A listing does not read it because doing
/// so would mean one privileged read per entry under the account's home just to
/// draw a table; `GetCronEntryOutput` answers it for the one entry an operator
/// opened.
const UNREAD_EXIT_CODE: i32 = 0;

/// The run timestamp a LISTED entry reports: zero, meaning "not read". See
/// [`UNREAD_EXIT_CODE`].
const UNREAD_RUN_TIME: i64 = 0;

/// Builds the wire message for one entry of an account's crontab.
///
/// The command is the part worth naming. It does not live in the crontab: the
/// installed line names a file, and the listing reads that file back. When the
/// file is not there the entry still exists and still runs, so the entry is
/// reported with an EMPTY command rather than being dropped from the listing or
/// turned into an error — a missing command file is a state an operator needs
/// to see, and an entry silently absent from the panel while cron goes on
/// running it is the worse of the two answers.
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
        last_exit_code: UNREAD_EXIT_CODE,
        last_run_at_unix: UNREAD_RUN_TIME,
    }
}

#[cfg(test)]
#[path = "../../tests/services/cron/listed_entry_tests.rs"]
mod tests;
