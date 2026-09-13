//! The in-memory [`MonitorHost`] the monitoring tests decide against, and the
//! captures they decide about.
//!
//! Shared by every `*_tests.rs` in this folder through `#[path]`. The real host
//! reads whatever the machine running the suite happens to be doing and asks
//! whatever service manager that machine has, so a test against it would assert
//! on the build server's load and on the build server's units. Behind this fake
//! the numbers are chosen and the parsing is what is under examination.
//!
//! # Where the captures came from
//!
//! `crates/ops/tests/fixtures/proc/{ubuntu24,alma9}/` holds text taken from the
//! two polygon images — `docker run maran-polygon-<family> cat /proc/<file>` —
//! and not from a developer's workstation.
//!
//! One property of that capture is worth stating rather than leaving for the
//! next reader to rediscover: a container shares its host's kernel, and
//! `meminfo`, `stat` and `loadavg` are not namespaced, so those three files
//! report the machine the image was RUN on. `net/dev` is namespaced and is the
//! container's own. What the captures therefore pin is the FORMAT — which is a
//! property of the kernel interface rather than of a distribution, and is the
//! thing these parsers can get wrong — read out of the two supported images
//! rather than out of a parser author's memory.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::BTreeMap;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_distro::{DistroAdapter, DistroFamily, adapter_for};

use crate::monitor::model::filesystem_usage::FilesystemUsage;
use crate::monitor::{MonitorError, MonitorHost};

/// Memory accounting captured from the Ubuntu 24.04 polygon.
///
/// Captured from that image, and a reading of the machine the image RAN on —
/// see the module doc: this file is not namespaced. It pins the FORMAT.
pub(crate) const UBUNTU_MEMINFO: &str =
    include_str!("../../../tests/fixtures/proc/ubuntu24/meminfo.txt");

/// Processor accounting captured from the Ubuntu 24.04 polygon.
///
/// The machine the image ran on, not the image; the FORMAT is what it pins.
pub(crate) const UBUNTU_STAT: &str = include_str!("../../../tests/fixtures/proc/ubuntu24/stat.txt");

/// Load averages captured from the Ubuntu 24.04 polygon.
///
/// The machine the image ran on, not the image; the FORMAT is what it pins.
pub(crate) const UBUNTU_LOADAVG: &str =
    include_str!("../../../tests/fixtures/proc/ubuntu24/loadavg.txt");

/// Per-interface counters captured from the Ubuntu 24.04 polygon.
///
/// The one capture that IS the container's own: `net/dev` is namespaced, which
/// is why both families' copies are byte-identical fresh namespaces showing
/// `lo: 0` — and why the loopback test cannot be built on them.
pub(crate) const UBUNTU_NET_DEV: &str =
    include_str!("../../../tests/fixtures/proc/ubuntu24/net_dev.txt");

/// Memory accounting captured from the AlmaLinux 9 polygon.
///
/// Identical `MemTotal` to the Ubuntu capture, and honestly so: not namespaced,
/// so both read the one host kernel. The FORMAT is what it pins.
pub(crate) const ALMA_MEMINFO: &str =
    include_str!("../../../tests/fixtures/proc/alma9/meminfo.txt");

/// Processor accounting captured from the AlmaLinux 9 polygon.
///
/// The machine the image ran on, not the image; the FORMAT is what it pins.
pub(crate) const ALMA_STAT: &str = include_str!("../../../tests/fixtures/proc/alma9/stat.txt");

/// Load averages captured from the AlmaLinux 9 polygon.
///
/// The machine the image ran on, not the image; the FORMAT is what it pins.
pub(crate) const ALMA_LOADAVG: &str =
    include_str!("../../../tests/fixtures/proc/alma9/loadavg.txt");

/// Per-interface counters captured from the AlmaLinux 9 polygon.
///
/// Namespaced, so this one is the container's own — and byte-identical to the
/// Ubuntu capture, two empty network namespaces looking the same.
pub(crate) const ALMA_NET_DEV: &str =
    include_str!("../../../tests/fixtures/proc/alma9/net_dev.txt");

/// What the service manager says about a unit this host knows nothing about.
const UNKNOWN_UNIT: &str = "LoadState=not-found\nActiveState=inactive\nSubState=dead\nTriggeredBy=";

/// A [`MonitorHost`] whose every reading is chosen by the test.
pub(crate) struct FakeMonitorHost {
    /// Successive answers to `read_cpu_times`; the last one repeats.
    cpu_samples: Mutex<Vec<Option<String>>>,
    /// How many processor readings have been taken.
    cpu_reads: Mutex<usize>,
    /// How many times the host was asked to wait between them.
    pauses: Mutex<usize>,
    /// The memory accounting, or `None` for a file that cannot be read.
    memory: Mutex<Option<String>>,
    /// The load averages, or `None`.
    load: Mutex<Option<String>>,
    /// The per-interface counters, or `None`.
    network: Mutex<Option<String>>,
    /// What the filesystem query answers.
    filesystem: Mutex<Result<FilesystemUsage, MonitorError>>,
    /// `systemctl show` output per unit; a unit absent here is not installed.
    units: Mutex<BTreeMap<String, String>>,
    /// The exit status the service manager reports, or a spawn failure.
    service_manager: Mutex<Result<i32, MonitorError>>,
    /// The password database's text, or `None` for a file that cannot be read.
    passwd: Mutex<Option<String>>,
    /// What each directory measures; a directory absent here measures zero.
    sizes: Mutex<BTreeMap<PathBuf, u64>>,
    /// Every command the host was asked to spawn, as `program` plus its argv.
    commands: Mutex<Vec<Vec<String>>>,
}

impl FakeMonitorHost {
    /// A host answering the Ubuntu 24.04 polygon's captures, with a filesystem
    /// half full, no units and no accounts.
    pub(crate) fn from_ubuntu_captures() -> Self {
        Self {
            cpu_samples: Mutex::new(vec![Some(UBUNTU_STAT.to_owned())]),
            cpu_reads: Mutex::new(0),
            pauses: Mutex::new(0),
            memory: Mutex::new(Some(UBUNTU_MEMINFO.to_owned())),
            load: Mutex::new(Some(UBUNTU_LOADAVG.to_owned())),
            network: Mutex::new(Some(UBUNTU_NET_DEV.to_owned())),
            filesystem: Mutex::new(Ok(FilesystemUsage {
                used_bytes: 512,
                total_bytes: 1024,
            })),
            units: Mutex::new(BTreeMap::new()),
            service_manager: Mutex::new(Ok(0)),
            passwd: Mutex::new(Some(String::new())),
            sizes: Mutex::new(BTreeMap::new()),
            commands: Mutex::new(Vec::new()),
        }
    }

    /// The same host, answering the AlmaLinux 9 polygon's captures.
    pub(crate) fn from_alma_captures() -> Self {
        let host = Self::from_ubuntu_captures();
        *host.cpu_samples.lock().unwrap() = vec![Some(ALMA_STAT.to_owned())];
        *host.memory.lock().unwrap() = Some(ALMA_MEMINFO.to_owned());
        *host.load.lock().unwrap() = Some(ALMA_LOADAVG.to_owned());
        *host.network.lock().unwrap() = Some(ALMA_NET_DEV.to_owned());
        host
    }

    /// Answers `first` and then `second` to the two processor readings.
    pub(crate) fn with_cpu_samples(self, first: &str, second: &str) -> Self {
        *self.cpu_samples.lock().unwrap() = vec![Some(first.to_owned()), Some(second.to_owned())];
        self
    }

    /// Makes every kernel statistics file unreadable.
    pub(crate) fn with_unreadable_statistics(self) -> Self {
        *self.cpu_samples.lock().unwrap() = vec![None];
        *self.memory.lock().unwrap() = None;
        *self.load.lock().unwrap() = None;
        *self.network.lock().unwrap() = None;
        self
    }

    /// Replaces the memory accounting with `meminfo`.
    pub(crate) fn with_memory(self, meminfo: &str) -> Self {
        *self.memory.lock().unwrap() = Some(meminfo.to_owned());
        self
    }

    /// Makes the filesystem query fail.
    pub(crate) fn with_unmeasurable_filesystem(self) -> Self {
        *self.filesystem.lock().unwrap() = Err(MonitorError::FilesystemUnavailable);
        self
    }

    /// Teaches the host what the service manager says about `unit`.
    pub(crate) fn with_unit(self, unit: &str, properties: &str) -> Self {
        self.units
            .lock()
            .unwrap()
            .insert(unit.to_owned(), properties.to_owned());
        self
    }

    /// Makes the service manager exit with `status` on every call.
    pub(crate) fn with_service_manager_status(self, status: i32) -> Self {
        *self.service_manager.lock().unwrap() = Ok(status);
        self
    }

    /// Makes the service manager impossible to start at all.
    pub(crate) fn with_absent_service_manager(self) -> Self {
        *self.service_manager.lock().unwrap() = Err(MonitorError::program_unavailable());
        self
    }

    /// Gives the host a password database.
    pub(crate) fn with_passwd(self, passwd: &str) -> Self {
        *self.passwd.lock().unwrap() = Some(passwd.to_owned());
        self
    }

    /// Makes the password database unreadable.
    pub(crate) fn with_unreadable_passwd(self) -> Self {
        *self.passwd.lock().unwrap() = None;
        self
    }

    /// Says how big the tree at `path` is.
    pub(crate) fn with_size(self, path: &str, bytes: u64) -> Self {
        self.sizes
            .lock()
            .unwrap()
            .insert(PathBuf::from(path), bytes);
        self
    }

    /// How many times the host was asked to wait between processor readings.
    pub(crate) fn pauses(&self) -> usize {
        *self.pauses.lock().unwrap()
    }

    /// Every command the host was asked to spawn.
    pub(crate) fn commands(&self) -> Vec<Vec<String>> {
        self.commands.lock().unwrap().clone()
    }
}

impl MonitorHost for FakeMonitorHost {
    /// Answers the next configured processor reading; the last one repeats.
    fn read_cpu_times(&self) -> Result<String, MonitorError> {
        let samples = self.cpu_samples.lock().unwrap();
        let mut reads = self.cpu_reads.lock().unwrap();
        let index = (*reads).min(samples.len().saturating_sub(1));
        *reads += 1;

        samples
            .get(index)
            .cloned()
            .flatten()
            .ok_or(MonitorError::HostStatisticsUnavailable)
    }

    /// Counts the wait instead of taking it.
    fn pause_between_cpu_samples(&self) {
        *self.pauses.lock().unwrap() += 1;
    }

    /// Answers the configured memory accounting, or reports it unreadable.
    fn read_memory(&self) -> Result<String, MonitorError> {
        self.memory
            .lock()
            .unwrap()
            .clone()
            .ok_or(MonitorError::HostStatisticsUnavailable)
    }

    /// Answers the configured load averages, or reports them unreadable.
    fn read_load_average(&self) -> Result<String, MonitorError> {
        self.load
            .lock()
            .unwrap()
            .clone()
            .ok_or(MonitorError::HostStatisticsUnavailable)
    }

    /// Answers the configured per-interface counters, or reports them
    /// unreadable.
    fn read_network_counters(&self) -> Result<String, MonitorError> {
        self.network
            .lock()
            .unwrap()
            .clone()
            .ok_or(MonitorError::HostStatisticsUnavailable)
    }

    /// Answers the configured filesystem reading, whatever path is asked
    /// about — the real query is the one thing behind this seam a test cannot
    /// aim at a filesystem of its own choosing.
    fn filesystem_usage(&self, _path: &Path) -> Result<FilesystemUsage, MonitorError> {
        *self.filesystem.lock().unwrap()
    }

    /// Records the command and answers as the configured service manager would.
    ///
    /// The unit is the LAST argument — `show --property=… -- <unit>` — and a
    /// unit the test did not teach the host about answers `LoadState=not-found`,
    /// which is what a real `systemctl show` says about a unit that is not
    /// installed.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, MonitorError> {
        let mut command = vec![program.to_owned()];
        command.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.commands.lock().unwrap().push(command);

        let status = (*self.service_manager.lock().unwrap())?;
        if status != 0 {
            return Ok(CommandOutcome {
                status,
                stdout: String::new(),
                stderr: String::new(),
            });
        }

        let unit = arguments.last().copied().unwrap_or_default();
        let stdout = self
            .units
            .lock()
            .unwrap()
            .get(unit)
            .cloned()
            .unwrap_or_else(|| UNKNOWN_UNIT.to_owned());

        Ok(CommandOutcome {
            status,
            stdout,
            stderr: String::new(),
        })
    }

    /// Answers the configured password database, or reports it unreadable.
    ///
    /// The path is ignored: which file holds it is the `DistroAdapter`'s
    /// answer, and this fake is standing in for the file, not for the adapter.
    fn read_password_database(&self, _path: &str) -> Result<String, MonitorError> {
        self.passwd
            .lock()
            .unwrap()
            .clone()
            .ok_or(MonitorError::AccountsUnavailable)
    }

    /// Answers what the test said this tree measures; anything it did not
    /// mention measures zero, exactly as an absent directory does on a real
    /// host.
    fn directory_size(&self, path: &Path) -> u64 {
        self.sizes.lock().unwrap().get(path).copied().unwrap_or(0)
    }
}

/// The Debian-family adapter the monitoring tests ask for platform facts.
///
/// Debian by default because it is the family whose SSH unit is
/// socket-activated, which is the case this area exists to get right.
pub(crate) fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// The RHEL-family adapter, which enables its SSH service directly.
pub(crate) fn rhel_distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Rhel)
}
