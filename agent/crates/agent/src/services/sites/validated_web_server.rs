//! Reading the site web server of a request.

use maran_distro::SiteWebServer;

use crate::proto::SiteWebServer as WireWebServer;

/// Maps the wire web server onto the one the operations take.
///
/// `UNSPECIFIED` is nginx and not a refusal: before the site web server was a choice, nginx
/// served every site, so a caller that predates the field (or a panel on a host that never
/// chose) means exactly that. A value this build does not know is read the same way for the
/// same reason, because refusing it would refuse the cleanup of a site whose web server a newer
/// panel named.
#[must_use]
pub fn validated_web_server(web_server: i32) -> SiteWebServer {
    match WireWebServer::try_from(web_server) {
        Ok(WireWebServer::Apache) => SiteWebServer::Apache,
        Ok(WireWebServer::Nginx | WireWebServer::Unspecified) | Err(_) => SiteWebServer::Nginx,
    }
}

#[cfg(test)]
#[path = "../../tests/services/sites/validated_web_server_tests.rs"]
mod tests;
