//! Turning what an entry's last run left behind into the wire's payload.

use std::time::UNIX_EPOCH;

use maran_ops::cron::CronEntryOutput;

use crate::proto::GetCronEntryOutputOk;

/// Builds the wire payload for `GetCronEntryOutput`.
///
/// Three fields, all with explicit presence, and every absence here means the
/// same thing: the agent has no reading, rather than a reading that happens to
/// be empty or zero. That distinction is why the fields are `optional` in
/// `cron.proto` — 0 is the exit status of a successful run and an empty string
/// is what a run that printed nothing leaves, so neither could double as "never
/// ran" without the panel showing a green tick for a job that has never
/// started.
///
/// The timestamp is the modification time of the file the run's status was
/// written to, converted here and not read from a clock: it says when the RUN
/// ended. A time before the epoch, which a host with a badly set clock can
/// produce, is reported as absent rather than as a negative instant — the agent
/// has no reading it can stand behind, and saying so is the same answer it
/// gives for a run that never happened.
#[must_use]
pub fn entry_output(output: CronEntryOutput) -> GetCronEntryOutputOk {
    let last_run = output.last_run;

    GetCronEntryOutputOk {
        output: output.output,
        last_exit_code: last_run.as_ref().and_then(|record| record.exit_code),
        last_run_at_unix: last_run.and_then(|record| {
            record
                .ran_at
                .duration_since(UNIX_EPOCH)
                .ok()
                .map(|since| since.as_secs())
                .and_then(|seconds| i64::try_from(seconds).ok())
        }),
    }
}

#[cfg(test)]
#[path = "../../tests/services/cron/entry_output_tests.rs"]
mod tests;
