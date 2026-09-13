//! The [`MonitorHost`] that actually reads this machine.

use std::fs;
use std::path::Path;
use std::thread;
use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::directory::directory_size;
use maran_agent_core::utils::spawn_argv::spawn_argv;

use crate::monitor::model::filesystem_usage::FilesystemUsage;
use crate::monitor::monitor_error::MonitorError;
use crate::monitor::monitor_host::MonitorHost;

/// Where the kernel publishes its own statistics.
///
/// The four constants below are absolute paths in a crate that says it names no
/// platform location of its own ([`MonitorHost`]), so they get the same
/// justification `get_host_metrics.rs` gives `/`. They are not platform facts:
/// `/proc` is the Linux kernel's own interface, identical on every family this
/// panel supports and on every other Linux besides, so there is nothing for a
/// `DistroAdapter` to differ about — putting them there would file a
/// non-difference in the crate whose whole purpose is differences. They are not
/// agent-owned either, so they are not `AgentPaths` constants: the agent did not
/// create them and cannot move them. `maran_agent_core::utils::current_uid`
/// reads `/proc/self` on the same reasoning, and `scripts/lib/check-structure.sh`
/// rule 17 matches `/usr/s?bin`, `/s?bin` and `/etc` — the places where families
/// really do disagree — and deliberately not this one.
///
/// The kernel's processor time accounting.
const PROC_STAT: &str = "/proc/stat";

/// The kernel's memory accounting.
const PROC_MEMINFO: &str = "/proc/meminfo";

/// The kernel's load averages.
const PROC_LOADAVG: &str = "/proc/loadavg";

/// The kernel's per-interface byte counters.
const PROC_NET_DEV: &str = "/proc/net/dev";

/// How long the host waits between the two processor readings.
///
/// The ONE in-call wait anywhere in this crate, and it is here because a
/// processor percentage cannot be produced without it: the kernel counts ticks,
/// so a utilisation figure only exists between two readings taken some time
/// apart. A quarter of a second is long enough that the kernel's tick counters
/// (100 per second per processor on both supported families) have moved by a
/// number whose ratio means something, and short enough that a dashboard
/// refresh does not feel like a stall.
///
/// The caller must run this off a runtime worker — `spawn_blocking`
/// (rules/rust.md "Async and blocking") — because a quarter of a second parked
/// on a worker thread is a quarter of a second every other in-flight command
/// waits.
const CPU_SAMPLE_INTERVAL: Duration = Duration::from_millis(250);

/// Reads the real `/proc` and asks the real service manager.
///
/// The only implementation that touches the machine, and deliberately the
/// smallest piece of the area: it reads four files, waits once, measures one
/// filesystem and spawns one reporting subcommand. Every decision worth
/// reviewing — what the text means, what a unit's state implies, which accounts
/// are hosting accounts — lives in the operations, where a test decides the
/// input.
pub struct ProcessMonitorHost;

impl ProcessMonitorHost {
    /// Creates the host.
    #[must_use]
    pub fn new() -> Self {
        Self
    }
}

impl Default for ProcessMonitorHost {
    /// The host has no state, so the default is the only value there is.
    fn default() -> Self {
        Self::new()
    }
}

impl MonitorHost for ProcessMonitorHost {
    /// Reads the kernel's processor accounting.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::HostStatisticsUnavailable`] when it cannot be
    /// read, which on Linux means the kernel's statistics tree is not mounted.
    fn read_cpu_times(&self) -> Result<String, MonitorError> {
        read_statistic(PROC_STAT)
    }

    /// Sleeps for the sampling interval.
    fn pause_between_cpu_samples(&self) {
        thread::sleep(CPU_SAMPLE_INTERVAL);
    }

    /// Reads the kernel's memory accounting.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::HostStatisticsUnavailable`] when it cannot be
    /// read.
    fn read_memory(&self) -> Result<String, MonitorError> {
        read_statistic(PROC_MEMINFO)
    }

    /// Reads the kernel's load averages.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::HostStatisticsUnavailable`] when it cannot be
    /// read.
    fn read_load_average(&self) -> Result<String, MonitorError> {
        read_statistic(PROC_LOADAVG)
    }

    /// Reads the kernel's per-interface byte counters.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::HostStatisticsUnavailable`] when they cannot be
    /// read.
    fn read_network_counters(&self) -> Result<String, MonitorError> {
        read_statistic(PROC_NET_DEV)
    }

    /// Asks the filesystem holding `path` how full it is.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::FilesystemUnavailable`] when the query fails,
    /// which is what a path on no mounted filesystem produces.
    fn filesystem_usage(&self, path: &Path) -> Result<FilesystemUsage, MonitorError> {
        let statistics =
            rustix::fs::statvfs(path).map_err(|_| MonitorError::FilesystemUnavailable)?;

        Ok(usage_of(&statistics))
    }

    /// Spawns `program` with `arguments` as an argv array.
    ///
    /// No shell is involved, at any point (rules/security.md item 3): the
    /// arguments reach `execve` one by one, so there is no command line for
    /// anything to re-parse. `program` comes from the `DistroAdapter`'s
    /// allow-list and never from a request, and so does every argument — this
    /// area accepts no unit name from a caller at all.
    ///
    /// The spawn itself is [`spawn_argv`], shared with every other host that
    /// runs an argv array. Standard input is closed rather than inherited
    /// there too — a tool that decides to prompt fails instead of hanging a
    /// root daemon forever — and it pins `LC_ALL=C`, which this file did not
    /// do before: a gain, since this area parses the service manager's own
    /// words to decide whether a unit is running.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::ServiceManagerUnavailable`] with a `code` of
    /// `-1` when the program cannot be started or waited for.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, MonitorError> {
        spawn_argv(program, arguments).map_err(|_| MonitorError::program_unavailable())
    }

    /// Reads the host's own passwd file.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::AccountsUnavailable`] when it cannot be read.
    fn read_password_database(&self, path: &str) -> Result<String, MonitorError> {
        fs::read_to_string(path).map_err(|_| MonitorError::AccountsUnavailable)
    }

    /// Walks the tree and sums it, through the one implementation of that walk.
    ///
    /// Symlinks count as zero and are never followed, which is what keeps a
    /// customer's link into `/` from making their account look enormous and a
    /// link into its own parent from making the walk endless.
    fn directory_size(&self, path: &Path) -> u64 {
        directory_size(path)
    }
}

/// Turns a filesystem query's block counts into bytes used and bytes in total.
///
/// Split from [`MonitorHost::filesystem_usage`] so that a test can drive it:
/// the whole question here is WHICH of `statvfs`' three block counts the panel
/// reports, and that choice is invisible to a test that can only run the real
/// syscall against whatever filesystem the build machine happens to have.
///
/// # The rule: the panel reports space the people whose data it is can write
///
/// Used is total minus **available**, never total minus free. A filesystem
/// keeps a reserve — 5% by default on ext4 — that only root may write into, so
/// its blocks are free and unavailable at the same time. A hosting account is
/// not root: at the instant `f_bavail` reaches zero every customer write fails
/// with `ENOSPC`, and a panel built on `f_bfree` would be showing "94.9% used,
/// 23.8 GiB free" at that exact moment. The reserve counts as used because to
/// every account on the machine it is used, and this number exists so an
/// operator knows how much room their CUSTOMERS have.
///
/// The measurement behind those figures, taken on a real 467 GiB ext4 root:
/// `f_blocks=122512118`, `f_bfree=99553529`, `f_bavail=93311878`,
/// `f_frsize=4096` — a 6,241,651-block reserve, 23.8 GiB, 5.09% of the
/// filesystem.
///
/// # This is deliberately NOT `df`'s `Used`, and the difference is the reserve
///
/// Say it plainly, because `df` is the obvious thing to check this against and
/// the two disagree. `df` reports `Used` as the blocks the filesystem itself
/// accounts for — `f_blocks - f_bfree` — and computes its `Use%` against
/// `used + available`, which is why its percentage is right while its byte
/// count excludes the reserve. This function reports one number for both
/// purposes, so its `used_bytes` is larger than `df`'s `Used` by exactly the
/// reserve: on the filesystem above, 121,162,036 KiB and 24.72% here against
/// `df`'s 96,195,436 KiB and 21%. `total_bytes` does equal `df`'s `Size`.
///
/// The gap is not an error to be reconciled away — it is the rule above, in
/// numbers. The two converge exactly when `f_bavail` reaches zero: at the
/// moment the filesystem is full for the people whose data it is, this reads
/// 100% and so does `df`. Anywhere else, this reads higher on purpose, because
/// the reserve is room no account will ever get.
///
/// Block counts are multiplied by the FRAGMENT size and not by the preferred
/// block size: POSIX defines `f_blocks`, `f_bfree` and `f_bavail` in units of
/// `f_frsize`, and `df` multiplies by that same field — the one place the two
/// really are doing the same arithmetic. The two agree on every
/// filesystem this panel will meet, which is exactly why using the other one is
/// a bug that hides until the day they do not.
///
/// Saturating throughout: a filesystem reporting more available blocks than it
/// has — which a network filesystem may — reads as fully free rather than
/// wrapping to an enormous "used".
fn usage_of(statistics: &rustix::fs::StatVfs) -> FilesystemUsage {
    let block = statistics.f_frsize;

    FilesystemUsage {
        used_bytes: statistics
            .f_blocks
            .saturating_sub(statistics.f_bavail)
            .saturating_mul(block),
        total_bytes: statistics.f_blocks.saturating_mul(block),
    }
}

/// Reads one of the kernel's statistics files.
///
/// Every failure is the same failure to the caller — the number is not
/// available this time round — so the specific `io::Error` is deliberately not
/// carried across: this area's error type has no field that could hold one, and
/// the panel's question is answered by the variant.
fn read_statistic(path: &str) -> Result<String, MonitorError> {
    fs::read_to_string(path).map_err(|_| MonitorError::HostStatisticsUnavailable)
}

#[cfg(test)]
#[path = "../tests/monitor/process_monitor_host_tests.rs"]
mod tests;
