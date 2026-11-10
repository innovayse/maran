//! Reads the answer of the web-server probe.

use crate::transfer::model::source_web_server::SourceWebServer;

/// The server `text` (the banner of `nginx -v` or `apache2 -v`) names: nginx or
/// Apache from the first line, else `None`.
#[must_use]
pub fn parse_web_server(text: &str) -> Option<SourceWebServer> {
    let banner = text.lines().next()?.to_ascii_lowercase();

    if banner.contains("nginx") {
        Some(SourceWebServer::Nginx)
    } else if banner.contains("apache") {
        Some(SourceWebServer::Apache)
    } else {
        None
    }
}

#[cfg(test)]
#[path = "../../tests/transfer/probes/parse_web_server_tests.rs"]
mod tests;
