//! EnableSite: putting a suspended site back to serving its own content.

use maran_distro::DistroAdapter;

use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::render_vhost::render_vhost;
use crate::sites::resolved_site_paths::resolved_site_paths;
use crate::sites::write_vhost::write_vhost;
use crate::sites::{SiteHost, SitesOpError};

/// Re-renders the site's own vhost, replacing the suspended one.
///
/// Enabling an already-enabled site is a no-op success, not an error (spec §9,
/// and the panel retries after a timeout). The check is a comparison of what
/// the vhost would say against what it already says, because the rendered file
/// IS this area's state: a marker file beside it could outlive the config it
/// claimed to describe, and then "enabled" would be a fact about the marker
/// rather than about what nginx serves. Comparing content also means an
/// interrupted operation is finished by running the same command again.
///
/// The no-op path runs no validator and no reload. Reloading a web server to
/// reach a state it is already in is not free, and doing it on every retry of
/// a timed-out request is how a reload storm starts.
///
/// # Errors
///
/// Returns [`SitesOpError::UnsafeDocumentRoot`] when the document root is gone
/// or no longer resolves inside the account's home.
/// Returns [`SitesOpError::NotFound`] when no vhost exists for the domain —
/// there is nothing to enable, and creating one here would let a delete be
/// undone by a retry of an older request. Returns [`SitesOpError::Render`],
/// [`SitesOpError::NginxValidation`], [`SitesOpError::ReloadFailed`] and
/// [`SitesOpError::ConfigWrite`] as `create_site` does.
pub fn enable_site(
    host: &dyn SiteHost,
    distro: &dyn DistroAdapter,
    input: &CreateSiteInput,
) -> Result<(), SitesOpError> {
    // Resolved, not merely named: `create_site` rendered the canonical
    // document root, so anything less here renders different text for the same
    // site and the comparison below never matches.
    let paths = resolved_site_paths(host, &input.account, &input.domain)?;

    let current = host
        .read_config(&paths.config_path)?
        .ok_or_else(|| SitesOpError::NotFound {
            domain: input.domain.as_str().to_owned(),
        })?;

    let contents = render_vhost(input, &paths)?;
    if current == contents {
        return Ok(());
    }

    write_vhost(host, distro, &paths.config_path, &contents)
}

#[cfg(test)]
#[path = "../tests/sites/enable_site_tests.rs"]
mod tests;
