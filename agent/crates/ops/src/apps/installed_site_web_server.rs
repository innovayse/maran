//! Which web server serves this host's sites.

use maran_agent_core::agent_paths::AgentPaths;
use maran_distro::SiteWebServer;

use crate::apps::parse_marker::parse_marker;

/// The web server that serves this host's sites: the one the agent's marker names, nginx while
/// there is none.
///
/// nginx is always present because it is the panel's own front, so a binary on disk says nothing
/// about the choice; the marker (`AgentPaths::SITE_WEB_SERVER_MARKER`) is written by `InstallApp`
/// and removed by `UninstallApp`, and is the one record of it. Used where an operation needs the
/// web server's identity and has no request that names it: the user a php-fpm pool hands its
/// socket to, the group an account's home is owned by, the trees the startup pass validates.
/// Read from disk on every call and never cached, like every other app probe. An unreadable or
/// unrecognised marker is the nginx default: the identity of the panel's own front is the one
/// that always exists.
#[must_use]
pub fn installed_site_web_server() -> SiteWebServer {
    let text = std::fs::read_to_string(AgentPaths::SITE_WEB_SERVER_MARKER).ok();
    parse_marker(text.as_deref()).unwrap_or(SiteWebServer::Nginx)
}
