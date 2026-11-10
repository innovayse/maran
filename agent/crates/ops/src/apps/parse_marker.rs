//! Reading the site web server marker.

use maran_distro::SiteWebServer;

use crate::apps::marker_text::marker_text;

/// The web server a marker's text names, `None` for no marker or text that names neither.
#[must_use]
pub(crate) fn parse_marker(text: Option<&str>) -> Option<SiteWebServer> {
    let text = text?.trim();
    [SiteWebServer::Nginx, SiteWebServer::Apache]
        .into_iter()
        .find(|server| marker_text(*server) == text)
}

#[cfg(test)]
#[path = "../tests/apps/parse_marker_tests.rs"]
mod tests;
