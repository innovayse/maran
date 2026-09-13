//! The closed set of PHP versions this panel runs, and the check against it.

use maran_agent_core::validation::web::php_version::PhpVersion;

/// Every PHP version the panel supports, oldest first (spec §11).
///
/// A closed list and not a range check, because the two families' repositories
/// package exactly these: Sury on Debian and Remi on RHEL (spec §4). A version
/// outside it has no package name to derive, so it is refused by the agent
/// rather than handed to a package manager to fail on — the caller does not
/// get to choose what the agent installs (rules/security.md item 12).
///
/// Ordered oldest first so `list_php_versions` can reverse it to report newest
/// first without sorting version strings, which compare wrongly as text:
/// `"8.10" < "8.9"` lexicographically and `"7.4" > "10.0"`.
pub(crate) const SUPPORTED_VERSIONS: &[&str] = &["7.4", "8.0", "8.1", "8.2", "8.3", "8.4"];

/// Whether `version` is one the panel supports.
pub(crate) fn is_supported(version: &PhpVersion) -> bool {
    SUPPORTED_VERSIONS.contains(&version.as_str())
}

/// Refuses `version` unless it is on [`SUPPORTED_VERSIONS`].
///
/// # Errors
///
/// Returns [`crate::php::PhpOpError::UnsupportedVersion`] for anything else.
pub(crate) fn ensure_supported(version: &PhpVersion) -> Result<(), crate::php::PhpOpError> {
    if is_supported(version) {
        return Ok(());
    }

    Err(crate::php::PhpOpError::UnsupportedVersion {
        version: version.as_str().to_owned(),
    })
}
