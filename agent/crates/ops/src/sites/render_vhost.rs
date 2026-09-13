//! Rendering a site's vhost, with ONE rendering of the rules it serves.

use maran_agent_core::agent_paths::AgentPaths;
use maran_templates::nginx::php_site::PhpSite;
use maran_templates::nginx::proxy_site::ProxySite;
use maran_templates::nginx::site_body::SiteBody;
use maran_templates::nginx::ssl_block::SslBlock;
use maran_templates::nginx::static_site::StaticSite;

use crate::sites::SitesOpError;
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::site_kind::SiteKind;
use crate::sites::model::site_paths::SitePaths;

/// Renders the complete vhost for `input`.
///
/// The point of this function, and the reason it is not inlined into
/// `create_site`: a site with a certificate has TWO server blocks — the
/// port-80 one and the TLS one — and both must serve the same `root`, the
/// same `index` and the same locations. They are rendered ONCE here, into a
/// single string, and that same string is handed to the site template and to
/// [`SslBlock::server_body`]. A rule added to a site's body therefore reaches
/// both halves or neither.
///
/// Assembling the TLS body separately is the failure this shape prevents, and
/// it is a silent one: nothing refuses to start, `nginx -t` passes, and only
/// the half a browser actually reaches is wrong — so it surfaces as "the site
/// behaves differently over https", months later.
///
/// # Errors
///
/// Returns [`SitesOpError::Render`] when a template fails, which can only
/// happen if a template and its render type have drifted apart.
pub(crate) fn render_vhost(
    input: &CreateSiteInput,
    paths: &SitePaths,
) -> Result<String, SitesOpError> {
    let document_root = paths.document_root.display().to_string();
    let access_log = paths.access_log.display().to_string();
    let error_log = paths.error_log.display().to_string();
    let aliases: Vec<String> = input
        .aliases
        .iter()
        .map(|alias| alias.as_str().to_owned())
        .collect();

    let fpm_socket = match &input.kind {
        SiteKind::Php { version } => Some(fpm_socket_path(input, version.as_str())),
        SiteKind::Static | SiteKind::ReverseProxy { .. } => None,
    };
    let upstream = match &input.kind {
        SiteKind::ReverseProxy { upstream } => Some(upstream.as_str()),
        SiteKind::Static | SiteKind::Php { .. } => None,
    };

    // The one rendering of what this site serves. Everything below embeds
    // `body`; nothing below re-derives it.
    let body = SiteBody {
        // The logs belong to the body, not to the port-80 block: a site with a
        // certificate only redirects on 80, so logs declared there record
        // nothing and every real request lands in nginx's shared default file.
        access_log: &access_log,
        error_log: &error_log,
        document_root: &document_root,
        fpm_socket: fpm_socket.as_deref(),
        upstream,
    }
    .render_config()
    .map_err(render_failed)?;

    let certificate_path = input
        .certificate
        .as_ref()
        .map(|certificate| certificate.certificate_path().display().to_string())
        .unwrap_or_default();
    let certificate_key_path = input
        .certificate
        .as_ref()
        .map(|certificate| certificate.key_path().display().to_string())
        .unwrap_or_default();
    let ssl = input.certificate.as_ref().map(|_| SslBlock {
        domain: input.domain.as_str(),
        aliases: &aliases,
        certificate_path: &certificate_path,
        certificate_key_path: &certificate_key_path,
        // The same bytes the port-80 block gets, not a second assembly of them.
        server_body: &body,
    });

    match &input.kind {
        SiteKind::Php { .. } => PhpSite {
            domain: input.domain.as_str(),
            aliases: &aliases,
            document_root: &document_root,
            body: &body,
            ssl,
        }
        .render_config(),
        SiteKind::Static => StaticSite {
            domain: input.domain.as_str(),
            aliases: &aliases,
            document_root: &document_root,
            body: &body,
            ssl,
        }
        .render_config(),
        SiteKind::ReverseProxy { .. } => ProxySite {
            domain: input.domain.as_str(),
            aliases: &aliases,
            document_root: &document_root,
            body: &body,
            ssl,
        }
        .render_config(),
    }
    .map_err(render_failed)
}

/// The unix socket of the php-fpm pool serving this account at this version.
///
/// One pool per account × version (spec §11), so the socket is named after
/// both. The directory is the agent's own
/// [`AgentPaths::PHP_FPM_SOCKET_DIRECTORY`], never either family's packaged
/// one — the agent renders the pool that listens here, so both ends of the
/// socket come from the same constant and cannot disagree.
fn fpm_socket_path(input: &CreateSiteInput, version: &str) -> String {
    format!(
        "{}/{}-{version}.sock",
        AgentPaths::PHP_FPM_SOCKET_DIRECTORY,
        input.account.as_str()
    )
}

/// Turns a template failure into this area's typed error.
fn render_failed(error: maran_templates::render_error::RenderError) -> SitesOpError {
    SitesOpError::Render {
        reason: error.to_string(),
    }
}
