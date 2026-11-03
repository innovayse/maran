//! The package manager, the reading of its failures, the reading of its pending updates, and the
//! journal an install unit writes.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").

use crate::adapter_binary::AdapterBinary;
use crate::catalogue_probe::CatalogueProbe;
use crate::package_failure::PackageFailure;
use crate::pending_update::PendingUpdate;
use crate::pending_updates_failure::PendingUpdatesFailure;

/// The package manager, the reading of its failures, and the journal an install unit writes.
pub trait PackageFacts: Send + Sync {
    /// Absolute path of the package manager binary.
    ///
    /// `/usr/bin/apt-get` on the Debian family, `/usr/bin/dnf` on the RHEL
    /// family — the two package repositories the spec fixes (§4) are reached
    /// through these. The agent does NOT spawn it: a package install runs in
    /// the installer's `maran-php-install@.service` unit, outside the agent's
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

    /// Absolute path of the journal reader, `journalctl`, for the
    /// process-execution allow-list.
    ///
    /// The package manager runs in the installer's install unit and not as the
    /// agent's child, so its output reaches the agent only through that unit's
    /// journal; this is the program that reads it back.
    fn journal_binary(&self) -> AdapterBinary;
}
