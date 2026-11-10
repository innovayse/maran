//! The suspended vhost as agents wrote it before the symlink rule.

use maran_distro::SiteWebServer;
use maran_templates::apache::suspended_site::SuspendedSite as ApacheSuspendedSite;
use maran_templates::nginx::suspended_site::SuspendedSite;

use crate::sites::SitesOpError;
use crate::sites::model::site_paths::SitePaths;

/// Renders the suspended server block as an earlier agent wrote it.
///
/// Used only to RECOGNISE a stub already on disk as suspended: the file is
/// compared byte for byte with a render, so a stub written before the
/// challenge location gained `disable_symlinks if_not_owner;` must still
/// compare equal, or the next certificate renewal or PHP-version change would
/// put that customer's site back on the air. Never written.
///
/// # Errors
///
/// Returns [`SitesOpError::Render`] when the template itself fails.
pub(crate) fn render_legacy_suspended_vhost(
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
        .render_legacy_config(),
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
