//! What one entry's last run left behind, as the panel shows it.

use crate::cron::model::cron_run_record::CronRunRecord;

/// The output an entry's last run wrote, and what that run reported.
///
/// One value rather than a `(Option<String>, Option<CronRunRecord>)` tuple,
/// because the two halves are read from two different files and a caller that
/// swapped them at a call site would get a type error rather than a display
/// with the output in the timestamp's place.
///
/// Both halves are `Option`, and independently so: an entry that has never run
/// has neither, and an entry killed between its redirect and its `echo` has
/// output with no record. Neither absence is an error — an entry created a
/// minute ago and scheduled for Sunday has simply not run yet.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CronEntryOutput {
    /// The tail of what the last run wrote to standard output and standard
    /// error, bounded by the caller's ceiling.
    ///
    /// `None` when there is no output file — the entry has never run. An empty
    /// string is a different answer: it ran and said nothing.
    pub output: Option<String>,
    /// What the last run reported about itself, if it reported anything.
    pub last_run: Option<CronRunRecord>,
}
