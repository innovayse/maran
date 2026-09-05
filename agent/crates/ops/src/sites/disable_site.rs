//! DisableSite: suspending a site without taking its vhost away.

use maran_distro::DistroAdapter;
use maran_templates::nginx::suspended_site::SuspendedSite;

use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::resolved_site_paths::resolved_site_paths;
use crate::sites::write_vhost::write_vhost;
use crate::sites::{SiteHost, SitesOpError};

/// Replaces the site's vhost with the suspended one.
///
/// The vhost is RE-RENDERED, never deleted: `sites.proto` keeps it on disable
/// "so SSL renewal and SEO are not disrupted". The suspended template still
/// answers `/.well-known/acme-challenge/` from the document root for exactly
/// that reason — a suspended site whose certificate cannot renew comes back
/// from suspension with an expired one, and a domain that stops resolving to
/// an answer at all loses its search ranking on the way. Everything else
/// answers the fixed refusal.
///
/// Disabling an already-disabled site is a no-op success (spec §9): the
/// rendered content is compared with what is on disk, and an unchanged file is
/// left alone rather than rewritten and reloaded.
///
/// # Errors
///
/// Returns [`SitesOpError::UnsafeDocumentRoot`] when the document root is gone
/// or no longer resolves inside the account's home.
/// Returns [`SitesOpError::NotFound`] when no vhost exists for the domain.
/// Returns [`SitesOpError::Render`], [`SitesOpError::NginxValidation`],
/// [`SitesOpError::ReloadFailed`] and [`SitesOpError::ConfigWrite`] as
/// `create_site` does.
pub fn disable_site(
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

    let aliases: Vec<String> = input
        .aliases
        .iter()
        .map(|alias| alias.as_str().to_owned())
        .collect();
    let contents = SuspendedSite {
        domain: input.domain.as_str(),
        aliases: &aliases,
        document_root: &paths.document_root.display().to_string(),
        access_log: &paths.access_log.display().to_string(),
        error_log: &paths.error_log.display().to_string(),
    }
    .render_config()
    .map_err(|error| SitesOpError::Render {
        reason: error.to_string(),
    })?;

    if current == contents {
        return Ok(());
    }

    write_vhost(host, distro, &paths.config_path, &contents)
}

#[cfg(test)]
#[path = "../tests/sites/disable_site_tests.rs"]
mod tests;
