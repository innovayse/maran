//! The one place a site's php-fpm pool is written from.

use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::DistroAdapter;

use crate::php::model::php_override::PhpOverride;
use crate::php::model::pool_input::PoolInput;
use crate::php::{PhpHost, write_pool};
use crate::sites::SitesOpError;
use crate::sites::model::create_site_input::CreateSiteInput;

/// Writes the php-fpm pool the site's rendered vhost will point at, for
/// `version`.
///
/// A SHARED unit, and not a convenience. Creating a PHP site and switching one
/// to another version both have to leave the same thing behind — a pool
/// listening on exactly the socket the rendered vhost names — and for as long
/// as only the switch wrote a pool, a site that was created and never switched
/// had a `fastcgi_pass` naming a socket nothing had bound, and answered every
/// request with a 502. Both callers now reach the pool writer through this one
/// function, so they cannot drift apart again; what a pool CONTAINS is still
/// [`write_pool`]'s decision alone.
///
/// The version is its own argument rather than being read off `input.kind`,
/// because the switch's whole business is that the two disagree: `input` is the
/// site as it is now, `version` is what it is becoming, and the pool must be
/// written for the second one. Whether a site wants a pool at all is the
/// caller's question — a static or reverse-proxied site owns none — and it is
/// deliberately not asked here, because the switch's answer and creation's
/// answer are different.
///
/// # Errors
///
/// Returns whatever [`write_pool`] refused, mapped into this area's error type
/// — most usefully [`SitesOpError::PhpVersionNotInstalled`] for a version this
/// host does not have, and [`SitesOpError::ConfigWrite`] carrying the PHP
/// area's own failure for anything else.
pub fn write_site_pool(
    php_host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    input: &CreateSiteInput,
    version: &PhpVersion,
    max_children: u32,
    overrides: &[PhpOverride],
) -> Result<(), SitesOpError> {
    write_pool(
        php_host,
        distro,
        &PoolInput {
            account: input.account.clone(),
            version: version.clone(),
            max_children,
            // Carried through, not dropped. A pool is written from scratch every
            // time — a different version's pool is a different file in a
            // different directory and nothing carries its contents forward — so
            // rendering an empty list here would hand a customer with
            // `memory_limit = 256M` a pool with no `php_value` lines at all, and
            // a success response saying so. Silently discarding a customer's
            // setting is the failure the whitelist's refuse-don't-drop rule
            // exists to prevent, and it is no better for happening on an
            // unrelated operation.
            overrides: overrides.to_vec(),
        },
    )?;

    Ok(())
}

#[cfg(test)]
#[path = "../tests/sites/write_site_pool_tests.rs"]
mod tests;
