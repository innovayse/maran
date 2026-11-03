//! The package manager, the reading of its failures, and the journal an install unit writes.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").

use crate::adapter_binary::AdapterBinary;
use crate::package_failure::PackageFailure;

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

    /// Absolute path of the journal reader, `journalctl`, for the
    /// process-execution allow-list.
    ///
    /// The package manager runs in the installer's install unit and not as the
    /// agent's child, so its output reaches the agent only through that unit's
    /// journal; this is the program that reads it back.
    fn journal_binary(&self) -> AdapterBinary;
}
