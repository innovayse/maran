//! Bringing every installed PHP version's OPcache drop-in to the current render.

use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::DistroAdapter;

use crate::php::model::opcache_drop_in_outcome::OpcacheDropInOutcome;
use crate::php::write_opcache_drop_in::write_opcache_drop_in;
use crate::php::{PhpHost, PhpOpError, list_php_versions};

/// Writes the OPcache drop-in of every installed PHP version that does not
/// already carry the current one.
///
/// Why it exists: a version installed before this drop-in existed has none, and
/// one whose file was edited by hand has the wrong one. The isolation lines of the
/// drop-in protect every tenant of the shared OPcache, so they are converged at
/// each start of the agent rather than only at install time.
///
/// The installed versions come from [`list_php_versions`], the same question every
/// pool write asks. A host with no PHP installed yields an empty vector, which is
/// the honest answer and not a failure.
///
/// # Errors
///
/// None. Every refusal is reported per version in the returned
/// [`OpcacheDropInOutcome`]s, because one version's refusal must not stop the
/// others from being converged. A host that cannot be asked which versions it has
/// yields no outcomes, the same fold [`super::reload_pool_trees`] makes.
#[must_use]
pub fn reconcile_opcache_drop_ins(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
) -> Vec<OpcacheDropInOutcome> {
    let Ok(installed) = list_php_versions(host, distro) else {
        return Vec::new();
    };

    installed
        .into_iter()
        .map(|item| {
            let result = match PhpVersion::parse(&item.version) {
                Ok(version) => write_opcache_drop_in(host, distro, &version),
                // The enumeration is a closed list of supported versions, so
                // this is a defence and not a path anything takes today.
                Err(_) => Err(PhpOpError::UnsupportedVersion {
                    version: item.version.clone(),
                }),
            };
            OpcacheDropInOutcome {
                version: item.version,
                result,
            }
        })
        .collect()
}

#[cfg(test)]
#[path = "../tests/php/reconcile_opcache_drop_ins_tests.rs"]
mod tests;
