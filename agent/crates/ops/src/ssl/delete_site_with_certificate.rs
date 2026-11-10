//! Deleting a site and the certificate material that belonged to it, as one act.

use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::DistroAdapter;

use crate::php::PhpHost;
use crate::sites::{SiteIdentity, SiteLogHost, SitesOpError, delete_site, remove_site_logs};
use crate::ssl::purge_certificate::purge_certificate;
use crate::ssl::ssl_host::SslHost;

/// Removes the site's vhost and then whatever certificate material its domain
/// had, in that order.
///
/// # Why this is one function and not two calls at the call site
///
/// It was two calls at the call site, and there was no call site.
/// [`purge_certificate`]'s own documentation says it is "called by the handler
/// that has just deleted a site, immediately after `sites::delete_site`
/// returns", and `delete_site` says the same thing from the other end — and
/// nothing in the agent called it. The operation existed, was unit-tested,
/// re-exported, and dead. What that cost was measured on a real host rather
/// than imagined: deleting a site left its `privkey.pem` in
/// `/etc/maran/certificates/<domain>/`, and deleting the whole ACCOUNT left it
/// there too, while the panel reported the deletion complete.
///
/// So the ordering is no longer a thing a caller has to remember and a comment
/// has to assert. There is one way to delete a site from the service layer, and
/// taking the key with it is part of what that way does.
///
/// # Why the order is this way round
///
/// The material goes AFTER the vhost. Unlinking a key the running configuration
/// still names makes the next `nginx -t` fail — and the next `nginx -t` may
/// belong to an unrelated site, minutes from now, long after anyone would
/// connect the two events.
///
/// # Why it lives in the `ssl` area
///
/// Because this area is already allowed to depend on `sites` — `SslHost` is a
/// `SiteHost`, and installing a certificate re-renders a vhost with the site
/// area's own renderer. The reverse is not true, so putting it in `sites` would
/// close a cycle between two areas and force every caller and every test of
/// `delete_site` to hold a certificate host in order to delete a site that may
/// never have had a certificate.
///
/// # The site's visitor logs go with it (issue #477)
///
/// After the material, the site's access and error logs and their rotated
/// generations are removed ([`remove_site_logs`]). They are named by the account
/// and the domain, which the panel recycles, and the log tail and the traffic
/// summary open them whatever their age: left behind, they handed the previous
/// tenant's visitor addresses and request targets to the next holder of the same
/// pair. Unlike the key, a failure here IS reported: the logs are personal data
/// the deletion promises to erase, so the caller must hear that it did not
/// finish. The retry converges, because a repeat finds the vhost gone
/// ([`SitesOpError::NotFound`]) and still removes the logs before answering it.
///
/// # Errors
///
/// Returns whatever [`delete_site`] refused with, and [`SitesOpError::LogUnreadable`]
/// when the site's logs could not be removed (the vhost is gone by then, and a retry
/// finishes the removal). A failure to remove the certificate material is reported
/// to the operator's log by [`purge_certificate`] and not to the caller, because a
/// site whose vhost is gone IS deleted and failing here would leave the caller
/// retrying an operation that has already succeeded.
pub fn delete_site_with_certificate(
    host: &dyn SslHost,
    php_host: &dyn PhpHost,
    log_host: &dyn SiteLogHost,
    distro: &'static dyn DistroAdapter,
    site: &SiteIdentity,
    retired: Option<&PhpVersion>,
) -> Result<(), SitesOpError> {
    match delete_site(host, php_host, distro, site, retired) {
        Ok(()) => purge_certificate(host, distro, &site.domain),
        // A site already gone is the converged retry, and the retry of a deletion whose
        // log removal failed: the logs are swept before the answer is given.
        Err(SitesOpError::NotFound { domain }) => {
            remove_site_logs(log_host, &site.account, &site.domain)?;
            return Err(SitesOpError::NotFound { domain });
        }
        Err(other) => return Err(other),
    }

    remove_site_logs(log_host, &site.account, &site.domain)?;

    Ok(())
}

#[cfg(test)]
#[path = "../tests/ssl/delete_site_with_certificate_tests.rs"]
mod tests;
