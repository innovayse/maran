//! The text of the site web server marker.

use maran_distro::SiteWebServer;

/// The text the marker holds for `server`. The one spelling of the marker: reading it back is
/// [`crate::apps::parse_marker::parse_marker`], which compares against this.
#[must_use]
pub(crate) fn marker_text(server: SiteWebServer) -> &'static str {
    match server {
        SiteWebServer::Nginx => "nginx",
        SiteWebServer::Apache => "apache",
    }
}
