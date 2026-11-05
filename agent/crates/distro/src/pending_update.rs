//! One package the host's package manager already knows a newer version of.

use crate::update_classification::UpdateClassification;

/// A package with a newer version available in the host's package cache
/// (issue #324).
///
/// The shape both families answer with, so it sits flat in this crate beside
/// [`crate::PackageFailure`]: what each family's package manager PRINTS differs
/// and is read by [`crate::PackageFacts::read_pending_updates`]; what the panel
/// is told does not. Every field comes from package metadata the host's own
/// configured repositories published, so each is untrusted text: the readers
/// accept only a token without whitespace or control characters, and nothing
/// downstream places one in a path, a shell or a query.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PendingUpdate {
    /// The package name as the manager names it: with a `:arch` qualifier where
    /// apt prints one for a foreign architecture, and without the architecture
    /// suffix on the RHEL family. A multilib host can therefore report one name
    /// twice on the RHEL family, once per architecture.
    pub package_name: String,
    /// The version an upgrade would install, exactly as the manager prints it
    /// (epoch and release included).
    pub available_version: String,
    /// The version installed now, or `None` when the family does not report it:
    /// `dnf check-update` prints only the new version. A package that is not
    /// installed at all is not an update and never becomes a row.
    pub installed_version: Option<String>,
    /// Where the update comes from: the apt origin list as apt prints it, or
    /// the dnf repository id. May be empty.
    pub source: String,
    /// Whether the metadata marks the update as a security fix (issue #163).
    /// Set by the reader on the Debian family, which sees the origins in the
    /// same output, and by [`crate::PackageFacts::mark_security_updates`] on the
    /// RHEL family; [`crate::UpdateClassification::Unmeasured`] until then and
    /// whenever the second reading failed.
    pub classification: UpdateClassification,
}
