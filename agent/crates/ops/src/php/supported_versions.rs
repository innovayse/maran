//! The closed set of PHP versions this panel runs, and the check against it.

use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::DistroAdapter;

/// Every PHP version the panel supports, oldest first (spec §11).
///
/// A closed list and not a range check, because the two families' repositories
/// package exactly these: Sury on Debian and Remi on RHEL (spec §4). A version
/// outside it has no package name to derive, so it is refused by the agent
/// rather than handed to the install unit — the caller does not get to choose
/// what the agent installs (rules/security.md item 12).
///
/// This is where the closed set is ENFORCED, and the only place: the install
/// unit installs whatever package its instance names, and a package manager
/// treats a name nothing has exactly as a pattern (`.` in `php8.3` matches any
/// character), so the six strings are not a guarantee about what gets
/// installed, only about what this agent asks for.
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

/// Refuses `version` unless this host's package repository publishes it.
///
/// The closed supported set is the panel's; this is the family's. A version
/// both agree on is installable, and it is the same intersection the
/// installable-versions query reports, so the picker never offers what the
/// install would refuse.
///
/// # Errors
///
/// Returns [`crate::php::PhpOpError::UnsupportedVersion`] for a version the
/// adapter's repository does not publish.
pub(crate) fn ensure_installable(
    distro: &dyn DistroAdapter,
    version: &PhpVersion,
) -> Result<(), crate::php::PhpOpError> {
    if distro.installable_php_versions().contains(&version.as_str()) {
        return Ok(());
    }

    Err(crate::php::PhpOpError::UnsupportedVersion {
        version: version.as_str().to_owned(),
    })
}
