//! Taking a deleted site's certificate material away with it.

use maran_agent_core::validation::web::domain::Domain;
use maran_distro::DistroAdapter;

use crate::sites::SiteCertificate;
use crate::ssl::remove_material::remove_material;
use crate::ssl::ssl_host::SslHost;

/// Removes whatever certificate material `domain` has, on a best-effort basis,
/// and reports a failure to the operator's log rather than to the caller.
///
/// Called by [`super::delete_site_with_certificate`], immediately after
/// `sites::delete_site` returns, and in that order for a reason that is not
/// stylistic: unlinking material the running configuration still names makes the
/// next `nginx -t` fail — and the next `nginx -t` may belong to an unrelated
/// site, minutes from now, long after anyone connects the two events.
///
/// It lives HERE, and not inside `delete_site`, because this area is already
/// allowed to depend on `sites` — `SslHost` is a `SiteHost`, and installing a
/// certificate re-renders a vhost with the site area's own renderer. Putting the
/// call inside `delete_site` would have `sites` depend on `ssl` in return, which
/// closes a cycle between two areas rather than the one-directional dependency
/// the rules anticipate, and would force every caller and every test of
/// `delete_site` to hold a certificate host in order to delete a site that may
/// never have had a certificate.
///
/// Why it must happen at all: the material is the agent's file, not the
/// customer's, so nothing else ever collects it. Left behind, it is an unbounded
/// pile of live private keys nothing accounts for — and, the part that matters,
/// a site created tomorrow on the same domain for a DIFFERENT account would find
/// the previous tenant's key and serve their certificate.
///
/// Returns nothing, deliberately. A site whose vhost is gone IS deleted, and
/// failing the delete over a leftover file would leave the caller retrying an
/// operation that has already succeeded. But a failure to remove a private key
/// is precisely the event an operator has to know about — it means a key for a
/// deleted site is still on disk, which is the whole concern this function
/// exists for — so it is logged at `warn` with the domain and nothing else. No
/// path, no material, no key: `tracing` output outlives the incident.
pub fn purge_certificate(host: &dyn SslHost, distro: &dyn DistroAdapter, domain: &Domain) {
    let certificate = SiteCertificate::for_domain(domain);

    if let Err(error) = remove_material(host, distro, &certificate) {
        tracing::warn!(
            domain = domain.as_str(),
            // The typed error's `Display`, which by construction carries no key
            // material: no variant of `SslOpError` can hold any.
            reason = %error,
            "the certificate material of a deleted site could not be removed; \
             a private key for a site that no longer exists is still on disk"
        );
    }
}

#[cfg(test)]
#[path = "../tests/ssl/purge_certificate_tests.rs"]
mod tests;
