//! The `systemd-run` argument vector that starts one entry by hand.

use std::time::Duration;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::account_slice_unit::account_slice_unit;
use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::managed_account::ManagedAccount;

use crate::cron::model::cron_environment::CronEnvironment;

/// How long a run started by hand may last before the service manager stops it.
///
/// Two minutes. The panel waits for the answer inside one HTTP request, and the
/// reverse proxy in front of it closes an upstream that is silent for 300 s
/// (`installer/nginx/maran.conf`), so the job, the agent's client deadline
/// (`CallClass::CronRun`, 180 s) and the panel's own wait
/// (`AgentCronRunPipeline`, 200 s) all have to fit inside that, in that order.
/// A run by hand is for checking that a job works; a job that needs longer
/// still runs on its schedule, where no ceiling of this agent applies.
pub(crate) const RUN_NOW_RUNTIME_CEILING: Duration = Duration::from_secs(120);

/// The most bytes each output stream of a run started by hand may carry.
///
/// Past it the run is stopped and no output is returned (`CronRunLimit::Output`).
/// 64 KiB is what the panel shows of a scheduled run's log as well
/// (`get_cron_entry_output`), so a run started either way shows as much.
pub(crate) const RUN_NOW_OUTPUT_CEILING: u64 = 64 * 1024;

/// The arguments `systemd-run` is started with to run `entry` of `account` now.
///
/// **Every one of them is agent-derived.** The uid and gid are the password
/// database's answer for a managed account; the slice is built from that uid;
/// the runtime ceiling is the constant above; the command file's path is built
/// by [`AgentPaths`] from the account and a validated id; and each `--setenv`
/// pair is the crontab's managed environment, whose names and values were
/// validated on the way into the crontab and again on the way out of it. No
/// argument is a caller's text, and the customer's command is not here at all
/// — it is the contents of the file the shell is handed, read by the shell as
/// the account.
///
/// What each argument is for:
///
/// - `--uid` / `--gid`: the service manager runs the job as the account, never
///   as root.
/// - `--slice`: the job lands in the account's `user-<uid>.slice`, so the CPU,
///   memory and task ceilings set on it (issue #133) hold it.
/// - `--wait --pipe`: the client stays until the job ends and passes the job's
///   standard output and error through, so the agent captures them under its
///   own ceiling.
/// - `--collect`: the transient unit is removed even when the job failed, so a
///   run leaves nothing behind in the service manager.
/// - `--quiet`: the client prints nothing of its own on success, so what comes
///   back is the job's output only.
/// - `RuntimeMaxSec`: the service manager stops the whole unit — every process
///   in it, not one process group — when the ceiling passes.
/// - `WorkingDirectory=~`: the account's home, where cron runs a job too.
///
/// [`END_OF_OPTIONS`] ends the client's options before the shell's path, and
/// each environment pair is inside its own `--setenv=` argument, so no value
/// can be read as an option of the client.
pub(crate) fn run_now_arguments(
    account: &ManagedAccount,
    entry: &CronEntryId,
    environment: &[CronEnvironment],
    sh_binary: &str,
) -> Vec<String> {
    let mut arguments = vec![
        format!("--uid={}", account.uid()),
        format!("--gid={}", account.gid()),
        format!("--slice={}", account_slice_unit(account.uid())),
        "--wait".to_owned(),
        "--pipe".to_owned(),
        "--collect".to_owned(),
        "--quiet".to_owned(),
        format!(
            "--property=RuntimeMaxSec={}",
            RUN_NOW_RUNTIME_CEILING.as_secs()
        ),
        "--property=WorkingDirectory=~".to_owned(),
    ];

    // The same assignments cron gives the job, in the order the crontab holds
    // them, so a job that works by hand works on its schedule.
    arguments.extend(environment.iter().map(|variable| {
        format!(
            "--setenv={}={}",
            variable.name.as_str(),
            variable.value.as_str()
        )
    }));

    arguments.push(END_OF_OPTIONS.to_owned());
    arguments.push(sh_binary.to_owned());
    arguments.push(
        AgentPaths::cron_cmd_path(account.name(), entry)
            .display()
            .to_string(),
    );

    arguments
}

#[cfg(test)]
#[path = "../tests/cron/run_now_arguments_tests.rs"]
mod tests;
