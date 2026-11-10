//! The one rendering of the vhost a suspended site serves.

use maran_distro::SiteWebServer;
use maran_templates::apache::suspended_site::SuspendedSite as ApacheSuspendedSite;
use maran_templates::nginx::suspended_site::SuspendedSite;

use crate::sites::SitesOpError;
use crate::sites::model::site_paths::SitePaths;

/// Renders the suspended server block for one site.
///
/// One rendering and not two, and that is the whole reason this is a file
/// rather than five lines inside `disable_site`. `disable_site` WRITES this
/// text and `inspect_account_sites` COMPARES against it, so a second copy
/// would be a second opinion about what a suspended site looks like — and the
/// half that drifted would be the one deciding whether a suspension can be
/// reported as done. The comparison is byte-for-byte, so "almost the same
/// text" is indistinguishable from "still serving the customer's site".
///
/// `paths` must be the RESOLVED paths (`resolved_site_paths`), never the
/// merely named ones: on a host where `/home` is a symlink or the homes are
/// bind-mounted the two differ, and the comparison would never match.
///
/// # Errors
///
/// Returns [`SitesOpError::Render`] when the template itself fails, which can
/// only happen if the template and its render type have drifted apart.
pub(crate) fn render_suspended_vhost(
    domain: &str,
    aliases: &[String],
    paths: &SitePaths,
    web_server: SiteWebServer,
) -> Result<String, SitesOpError> {
    let document_root = paths.document_root.display().to_string();
    let access_log = paths.access_log.display().to_string();
    let error_log = paths.error_log.display().to_string();

    match web_server {
        SiteWebServer::Nginx => SuspendedSite {
            domain,
            aliases,
            document_root: &document_root,
            access_log: &access_log,
            error_log: &error_log,
        }
        .render_config(),
        // Apache's stub has only one form: it was never written before the challenge
        // directory carried its link rule, so its "older" render is its current one.
        SiteWebServer::Apache => ApacheSuspendedSite {
            domain,
            aliases,
            document_root: &document_root,
            access_log: &access_log,
            error_log: &error_log,
        }
        .render_config(),
    }
    .map_err(|error| SitesOpError::Render {
        reason: error.to_string(),
    })
}
