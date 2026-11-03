//! The name of the transient unit one run by hand is started under.

use std::sync::atomic::{AtomicU64, Ordering};

use maran_agent_core::validation::system::cron_entry_id::CronEntryId;

/// What every run's unit name begins with, so an operator reading
/// `systemctl list-units` knows what started it.
const UNIT_PREFIX: &str = "maran-cron-run";

/// The suffix the service manager gives a transient service.
const SERVICE_SUFFIX: &str = ".service";

/// Runs started since this process began; the last component of every name.
static RUNS_STARTED: AtomicU64 = AtomicU64::new(0);

/// The name a run by hand's transient unit is given, so the agent can stop THAT
/// unit and no other (issue #336).
///
/// Without `--unit=`, `systemd-run` picks a name (`run-r1a2b3c.service`) and the
/// agent never learns it: the only handle on the job is the client process, and
/// ending the client does not end the job. With a name the agent chose,
/// stopping the job is `systemctl stop <name>`.
///
/// **Built from values the agent already holds and trusts, and nothing a
/// request carries.** The account's uid is a number read from the password
/// database, the entry id is a [`CronEntryId`] (36 characters of lowercase hex
/// and hyphens, validated against being a path), and the last component is a
/// counter: so the name is `[a-z0-9-]` throughout, has no room for an option, a
/// path separator or a second unit, and is at most 100 characters.
///
/// **Unique within this process by construction**, so two runs of one entry at
/// once (an account may have two) get two units. Across a restart of the agent
/// the counter begins again, so a run started in the first seconds of a new
/// process can meet a unit of the old one that is still inside its two-minute
/// ceiling; the service manager then refuses the name, the run reports
/// [`CronError::RunUnavailable`](crate::cron::cron_error::CronError), and
/// repeating it succeeds. That is the residue of choosing a counter over a
/// random source, and it is a refusal and never a collision with a live job.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct RunUnitName {
    /// The name without the `.service` suffix, as `systemd-run --unit=` takes it.
    stem: String,
}

impl RunUnitName {
    /// The name for the next run of `entry` by the account with `uid`.
    pub(crate) fn next(uid: u32, entry: &CronEntryId) -> Self {
        Self::numbered(uid, entry, RUNS_STARTED.fetch_add(1, Ordering::Relaxed))
    }

    /// The name for run number `run`; split from [`Self::next`] so a test can
    /// state the whole name.
    pub(crate) fn numbered(uid: u32, entry: &CronEntryId, run: u64) -> Self {
        Self {
            stem: format!("{UNIT_PREFIX}-{uid}-{}-{run}", entry.as_str()),
        }
    }

    /// The name `systemd-run --unit=` is given.
    pub(crate) fn stem(&self) -> &str {
        &self.stem
    }

    /// The full unit name `systemctl stop` is given.
    pub(crate) fn service(&self) -> String {
        format!("{}{SERVICE_SUFFIX}", self.stem)
    }
}

#[cfg(test)]
#[path = "../../tests/cron/run_unit_name_tests.rs"]
mod tests;
