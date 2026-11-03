//! One PHP version this host's repository can install.

/// A PHP version the host can install, and whether it already is.
///
/// Returned by `list_installable_php_versions`, which feeds the operator's
/// install picker: the picker offers every installable version and marks the
/// installed ones, so it never asks a human to type a version.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InstallablePhpVersion {
    /// The two-component version, as it is written — `8.3`.
    pub version: String,
    /// Whether this version is already installed on the host, decided by the
    /// same pool-directory check `list_php_versions` uses.
    pub installed: bool,
}
