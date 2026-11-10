//! The package manager, the reading of its failures, the reading of its pending updates, and the
//! journal an install unit writes.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").

use crate::adapter_binary::AdapterBinary;
use crate::catalogue_probe::CatalogueProbe;
use crate::package_failure::PackageFailure;
use crate::package_query_failure::PackageQueryFailure;
use crate::pending_update::PendingUpdate;
use crate::pending_updates_failure::PendingUpdatesFailure;
use crate::reboot_probe::RebootProbe;
use crate::reboot_probe_failure::RebootProbeFailure;
use crate::reboot_requirement::RebootRequirement;

/// The package manager, the reading of its failures, and the journal an install unit writes.
pub trait PackageFacts: Send + Sync {
    /// Absolute path of the package manager binary.
    ///
    /// `/usr/bin/apt-get` on the Debian family, `/usr/bin/dnf` on the RHEL
    /// family — the two package repositories the spec fixes (§4) are reached
    /// through these. The agent does NOT spawn it: a package install runs in
    /// the installer's `maran-app-install@.service` unit, outside the agent's
    /// sandbox. This is the program that unit's rendered `ExecStart=` names;
    /// the installer (`installer/lib/70-services.sh`) holds a second literal
    /// copy of the same path, so the two must be changed together.
    fn package_manager(&self) -> &'static str;

    /// Reads a failed run of [`Self::package_manager`]: its exit `status` and
    /// its combined `output`, as the install unit's journal holds them.
    ///
    /// The one place a package manager's failure is classified, and asked of
    /// the adapter because both halves are platform facts: the status a family's
    /// package manager exits with on an error (`apt-get` 100, `dnf` 1) and the
    /// words it prints for a source that refused or was never reached. The
    /// answer is [`PackageFailure::Local`] whenever the run does not prove a
    /// source was at fault (issue #251).
    fn classify_package_failure(&self, status: i32, output: &str) -> PackageFailure;

    /// Absolute path of the program the agent runs to READ the pending package
    /// updates (issue #324): `apt-get` on the Debian family, `dnf` on the RHEL
    /// family.
    ///
    /// Distinct from [`Self::package_manager`] on purpose, though it names the
    /// same file: that one is plain text handed to the install unit, which runs
    /// outside the agent's sandbox, and this one is the only package-manager
    /// path the agent itself may execute. It is an [`AdapterBinary`], so only
    /// this crate can name it, and the only arguments ever given to it are
    /// [`Self::catalogue_probe`]'s arguments and [`Self::pending_updates_arguments`].
    fn package_query_binary(&self) -> AdapterBinary;

    /// The run that tells whether the package catalogue has been built at all,
    /// or `None` when [`Self::pending_updates_arguments`] already fails on a
    /// host without one.
    ///
    /// Needed on the Debian family, where `apt-get -s dist-upgrade` against an
    /// empty lists directory exits 0 and prints "0 upgraded" (recorded in
    /// `tests/fixtures/package_manager/apt_dist_upgrade_empty_lists.txt`): that
    /// is indistinguishable from an up-to-date host, and reporting it as one is
    /// the failure this rpc exists to avoid. `dnf check-update -C` exits 1 on
    /// the same host, so the RHEL family answers `None`.
    fn catalogue_probe(&self) -> Option<CatalogueProbe>;

    /// The fixed arguments of the READ-ONLY, cache-only run that lists pending
    /// updates (issue #324).
    ///
    /// `-s` (a simulation: nothing is installed, removed or configured) on the
    /// Debian family and `-C` (cache only: no metadata refresh, no outbound
    /// call) on the RHEL family are the whole safety of the operation, so they
    /// are a `'static` constant array that no caller value reaches. There is no
    /// operand, so no `--` is needed.
    fn pending_updates_arguments(&self) -> &'static [&'static str];

    /// Reads the run of [`Self::pending_updates_arguments`]: its exit `status`
    /// and its `stdout`.
    ///
    /// The one place the exit status and the output grammar of a family's
    /// package manager are read. Exit status is the contract: `apt-get` 0;
    /// `dnf check-update` 0 (none) or 100 (updates) and anything else is a
    /// failure. A line the grammar does not recognise fails the whole read.
    ///
    /// # Errors
    ///
    /// Returns a [`PendingUpdatesFailure`] for a status that is not a success,
    /// a line that does not match the family's grammar, and output that ended
    /// without the line that closes it. An empty `Ok` list is only ever the
    /// answer to a read that succeeded.
    fn read_pending_updates(
        &self,
        status: i32,
        stdout: &str,
    ) -> Result<Vec<PendingUpdate>, PendingUpdatesFailure>;

    /// The fixed arguments of a SECOND read-only, cache-only run that names the
    /// security updates, or `None` when [`Self::read_pending_updates`] already
    /// classifies every row (issue #163).
    ///
    /// The Debian family answers `None`: the simulated upgrade prints each
    /// package's origin list, and a suite ending `-security` is read there. The
    /// RHEL family answers `check-update --security -C -q`, run against the
    /// same cache as the first reading and measured to answer from it alone
    /// after `dnf makecache` (`tests/fixtures/package_manager/`). The run is
    /// made with [`Self::package_query_binary`]; no operand exists, so no
    /// caller value reaches it.
    fn security_pending_updates_arguments(&self) -> Option<Vec<String>>;

    /// Folds the second reading, the run of
    /// [`Self::security_pending_updates_arguments`] (its exit `status` and
    /// `stdout`), into the rows [`Self::read_pending_updates`] returned
    /// (issue #163).
    ///
    /// The RHEL family marks every row whose package name the second reading
    /// names [`crate::UpdateClassification::Security`] and every other row
    /// [`crate::UpdateClassification::Ordinary`]; the Debian family has no
    /// second reading and returns `updates` unchanged.
    ///
    /// # Errors
    ///
    /// Returns the [`PendingUpdatesFailure`] of the second reading, by the same
    /// rules as [`Self::read_pending_updates`]. The caller then keeps the first
    /// reading's rows as they were, `Unmeasured`, and never guesses
    /// `Ordinary`.
    fn mark_security_updates(
        &self,
        updates: Vec<PendingUpdate>,
        status: i32,
        stdout: &str,
    ) -> Result<Vec<PendingUpdate>, PendingUpdatesFailure>;

    /// How this family finds out whether the host needs a reboot to run what is
    /// already installed (issue #163): a marker file on the Debian family, a
    /// program with constant arguments on the RHEL family.
    fn reboot_probe(&self) -> RebootProbe;

    /// Reads the exit `status` of a [`RebootProbe::Program`] run.
    ///
    /// # Errors
    ///
    /// Returns [`RebootProbeFailure::Unsuccessful`] for a status the program
    /// gives no answer to, a missing program included, and
    /// [`RebootProbeFailure::NotAProgramProbe`] on a family whose probe is a
    /// marker file. A failure is never read as "no reboot is required".
    fn read_reboot_probe_run(&self, status: i32) -> Result<RebootRequirement, RebootProbeFailure>;

    /// Absolute path of the program the agent runs to ask which packages the
    /// host's sources OFFER: `apt-cache` on the Debian family, `dnf` on the RHEL
    /// family.
    ///
    /// Read-only and cache-only on both: it names what the package metadata
    /// already on disk says, opens no connection and changes nothing. It is only
    /// ever given [`Self::package_candidate_arguments`].
    fn package_candidate_binary(&self) -> AdapterBinary;

    /// The arguments of the run that reports which of `packages` have a
    /// candidate version (`apt-cache policy -- …`, `dnf -q -C list --available -- …`).
    ///
    /// `packages` are spellings the adapter itself produced (for example
    /// [`PhpFacts::php_package`](crate::PhpFacts::php_package)), never a caller's
    /// text, and the operands follow `--`.
    fn package_candidate_arguments(&self, packages: &[String]) -> Vec<String>;

    /// Reads the run of [`Self::package_candidate_arguments`]: its exit `status`,
    /// `stdout` and `stderr`, and answers which of `packages` the host offers,
    /// in the order they were asked about.
    ///
    /// This is the host's own answer to "can this be installed here", the one
    /// the PHP versions screen is built from: a version no source carries is
    /// absent from the list instead of being offered and failing when chosen.
    ///
    /// # Errors
    ///
    /// Returns a [`PackageQueryFailure`] for a status that is not an answer, and
    /// for a line outside the family's grammar. A failure is never an empty
    /// list. On the Debian family an empty answer is also what a never-built
    /// catalogue prints, so the caller asks [`Self::catalogue_probe`] first.
    fn read_package_candidates(
        &self,
        status: i32,
        stdout: &str,
        stderr: &str,
        packages: &[String],
    ) -> Result<Vec<String>, PackageQueryFailure>;

    /// Absolute path of the journal reader, `journalctl`, for the
    /// process-execution allow-list.
    ///
    /// The package manager runs in the installer's install unit and not as the
    /// agent's child, so its output reaches the agent only through that unit's
    /// journal; this is the program that reads it back.
    fn journal_binary(&self) -> AdapterBinary;
}
