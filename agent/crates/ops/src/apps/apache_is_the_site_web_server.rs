//! Whether Apache is the site web server.

use maran_distro::SiteWebServer;

use crate::apps::installed_site_web_server::installed_site_web_server;

/// Whether the marker names Apache, for the startup pass that validates Apache's tree only then.
#[must_use]
pub fn apache_is_the_site_web_server() -> bool {
    installed_site_web_server() == SiteWebServer::Apache
}
