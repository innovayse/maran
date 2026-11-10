//! Rendering a site's vhost for the web server that serves it.

use maran_agent_core::agent_paths::AgentPaths;
use maran_distro::SiteWebServer;
use maran_templates::nginx::redirect_line::RedirectLine;

use crate::sites::SitesOpError;
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::redirect_match::RedirectMatch;
use crate::sites::model::redirect_status::RedirectStatus;
use crate::sites::model::site_kind::SiteKind;
use crate::sites::model::site_paths::SitePaths;
use crate::sites::render_apache_vhost::render_apache_vhost;
use crate::sites::render_nginx_vhost::render_nginx_vhost;

/// The loopback address a Node.js site's application listens on, and its vhost proxies to.
///
/// Loopback and not a wildcard: the application is reached only through the web server, so
/// a port it binds is never a way around the vhost's access rules.
const NODE_LOOPBACK_HOST: &str = "127.0.0.1";

/// Renders the complete vhost for `input`, in the dialect of the web server it names.
///
/// Both renderers keep one invariant, and it is the reason a vhost is rendered here and not
/// inlined into `create_site`: a site with a certificate has TWO server blocks — the port-80
/// one and the TLS one — and both must serve the same root, the same index and the same
/// locations. Each renders its body ONCE and hands that same string to both halves, so a
/// rule added to a site's body reaches both or neither.
///
/// The options are checked against the web server first, so a request limit on an Apache host
/// is refused here, in the one place every writer of a vhost passes through, and not rendered
/// without the promise it makes.
///
/// # Errors
///
/// Returns [`SitesOpError::InvalidOptions`] when the options do not fit the site or the web
/// server: a canonical host it does not serve, an upload size on a static site, a cache rule
/// on a reverse-proxied or Node.js one, a request limit on Apache. Returns
/// [`SitesOpError::Render`] when a template fails, which can only happen if a template and its
/// render type have drifted apart.
pub(crate) fn render_vhost(
    input: &CreateSiteInput,
    paths: &SitePaths,
) -> Result<String, SitesOpError> {
    input.options.check_web_server(input.web_server)?;

    match input.web_server {
        SiteWebServer::Nginx => render_nginx_vhost(input, paths),
        SiteWebServer::Apache => render_apache_vhost(input, paths),
    }
}

/// The `host:port` a site's vhost proxies to, when its kind is proxied.
///
/// A reverse-proxied site names its own upstream; a Node.js site is always its own loopback
/// port. One definition, so the two web servers' renders cannot disagree about where a Node.js
/// site's traffic goes.
#[must_use]
pub(crate) fn upstream_of(kind: &SiteKind) -> Option<String> {
    match kind {
        SiteKind::ReverseProxy { upstream } => Some(upstream.as_str().to_owned()),
        SiteKind::Node { port, .. } => Some(format!("{NODE_LOOPBACK_HOST}:{}", port.value())),
        SiteKind::Static | SiteKind::Php { .. } => None,
    }
}

/// The site's redirect rules as the templates write them.
pub(crate) fn redirect_lines(input: &CreateSiteInput) -> Vec<RedirectLine<'_>> {
    input
        .options
        .redirects()
        .iter()
        .map(|rule| RedirectLine {
            path: rule.path().as_str(),
            prefix: rule.matching() == RedirectMatch::Prefix,
            target: rule.target().as_str(),
            permanent: rule.status() == RedirectStatus::Permanent,
        })
        .collect()
}

/// The site's IP-list networks in the canonical spelling the type renders.
pub(crate) fn ip_networks(input: &CreateSiteInput) -> Vec<String> {
    input
        .options
        .ip_access()
        .map(|access| access.networks().iter().map(ToString::to_string).collect())
        .unwrap_or_default()
}

/// The unix socket of the php-fpm pool serving this account at this version.
///
/// One pool per account × version (spec §11), so the socket is named after
/// both. The directory is the agent's own
/// [`AgentPaths::PHP_FPM_SOCKET_DIRECTORY`], never either family's packaged
/// one — the agent renders the pool that listens here, so both ends of the
/// socket come from the same constant and cannot disagree.
pub(crate) fn fpm_socket_path(input: &CreateSiteInput, version: &str) -> String {
    format!(
        "{}/{}-{version}.sock",
        AgentPaths::PHP_FPM_SOCKET_DIRECTORY,
        input.account.as_str()
    )
}

/// Turns a template failure into this area's typed error.
pub(crate) fn render_failed(error: maran_templates::render_error::RenderError) -> SitesOpError {
    SitesOpError::Render {
        reason: error.to_string(),
    }
}

#[cfg(test)]
#[path = "../tests/sites/render_vhost_tests.rs"]
mod tests;
