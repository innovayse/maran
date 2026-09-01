//! DeleteSite: taking a site's vhost away.

use maran_agent_core::validation::php_version::PhpVersion;
use maran_distro::DistroAdapter;

use crate::php::PhpHost;
use crate::sites::model::site_identity::SiteIdentity;
use crate::sites::model::site_paths::SitePaths;
use crate::sites::remove_site_pool::remove_site_pool;
use crate::sites::remove_vhost::remove_vhost;
use crate::sites::{SiteHost, SitesOpError};

/// Removes the site's vhost and reloads the web server.
///
/// The customer's files are NOT removed. A document root holds the only copy
/// of somebody's site, deleting it is the one mistake that cannot be undone,
/// and removing a vhost is a reversible act while deleting a home directory's
/// contents is not — the account operations own that, at the point where the
/// whole account goes.
///
/// The site's CERTIFICATE material is not removed here either, and that is a
/// decision about dependencies rather than about lifetimes: it MUST be removed,
/// and `ssl::purge_certificate` is what removes it, called by the same handler
/// immediately after this operation returns. Doing it inline would make the site
/// area depend on the certificate area, which already depends on the site area —
/// `SslHost` is a `SiteHost`, and installing a certificate re-renders a vhost
/// with this area's own renderer. That is a cycle, not the one-directional
/// `sites -> php` shape the rules anticipate, and it spreads: it would force
/// every caller and every test of `delete_site` to hold a certificate host to
/// delete a site that may never have had a certificate.
///
/// Removal goes through the config-write protocol like any other change
/// (`safe_write::remove_config`), because unlinking a file the rest of the
/// tree references can leave nginx unable to start: validating after the
/// unlink and restoring the file when it refuses is what keeps the next reboot
/// from being the moment anyone finds out.
///
/// # Errors
///
/// Returns [`SitesOpError::NotFound`] when the site is already gone, which the
/// caller reads as a converged retry rather than a failure.
/// [`SitesOpError::NginxValidation`] and [`SitesOpError::ReloadFailed`] are
/// returned with the vhost restored; [`SitesOpError::ConfigWrite`] covers
/// every other failure of the protocol.
pub fn delete_site(
    host: &dyn SiteHost,
    php_host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    site: &SiteIdentity,
    pool_to_remove: Option<&PhpVersion>,
) -> Result<(), SitesOpError> {
    // Named, not resolved: the only path this operation touches is the vhost
    // in the agent's own include directory, and requiring the customer's
    // document root to still resolve would make a site with a deleted home
    // impossible to clean up.
    let paths = SitePaths::for_site(&site.account, &site.domain);

    if host.read_config(&paths.config_path)?.is_none() {
        return Err(SitesOpError::NotFound {
            domain: site.domain.as_str().to_owned(),
        });
    }

    remove_vhost(host, distro, &paths.config_path)?;

    // The vhost FIRST, the pool second, and never the other way round. Between
    // the two steps the site is already gone from nginx, so the pool it is
    // about to lose has no traffic left to serve; reversed, there is a window
    // in which a live vhost points at a socket nothing is bound to and every
    // request in it is a 502 — for however long the removal protocol takes to
    // validate and reload php-fpm.
    //
    // `pool_to_remove` is `None` for a static or reverse-proxied site, and for
    // a PHP site whose account still has other sites on the same version: a
    // pool is shared per account x version, so the panel decides (see
    // `remove_site_pool`). `None` is therefore the common case, not the
    // exception.
    match pool_to_remove {
        Some(version) => remove_site_pool(php_host, distro, &site.account, version),
        None => Ok(()),
    }
}

#[cfg(test)]
#[path = "../tests/sites/delete_site_tests.rs"]
mod tests;
