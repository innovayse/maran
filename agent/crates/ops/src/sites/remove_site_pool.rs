//! The one place a site's php-fpm pool is taken away from.

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::DistroAdapter;

use crate::php::{PhpHost, remove_pool};
use crate::sites::SitesOpError;

/// Removes `account`'s pool at `version`, when the caller has established that
/// nothing needs it any more.
///
/// **A pool belongs to an ACCOUNT and a version, not to a site.** Two sites of
/// the same account on the same PHP version share one pool and one set of
/// workers, which is the point of the design — a worker budget is a plan's, and
/// a plan belongs to an account. So a site being deleted, or moved to another
/// version, does NOT by itself mean its pool may go: the account's other sites
/// may still be served by it, and removing it would take them down.
///
/// Whether it may go is therefore the PANEL's answer and not the agent's. The
/// panel holds the site rows and can ask "does this account still have a site
/// on this version"; the agent holds a directory of rendered vhosts, which is a
/// rendering of those rows rather than a second copy to read back
/// (rules/architecture.md: truth lives in PostgreSQL). An agent that counted
/// `fastcgi_pass` lines to decide would be inventing a second source of truth
/// for the one question where being wrong takes a site off the air.
///
/// So this unit exists to be called only when the caller has been TOLD to, and
/// its whole job is to keep both callers — deleting a site, and switching one's
/// version — reaching the PHP area the same way, in the same order, with the
/// same error mapping.
///
/// # Errors
///
/// Returns whatever [`remove_pool`] refused, mapped into this area's error type
/// — [`SitesOpError::ConfigWrite`] carrying the PHP area's own failure.
pub fn remove_site_pool(
    php_host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    version: &PhpVersion,
) -> Result<(), SitesOpError> {
    remove_pool(php_host, distro, account, version)?;

    Ok(())
}

#[cfg(test)]
#[path = "../tests/sites/remove_site_pool_tests.rs"]
mod tests;
