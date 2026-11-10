//! Rendering a site's Apache vhost, with ONE rendering of the rules it serves.

use maran_agent_core::agent_paths::AgentPaths;
use maran_templates::apache::canonical_redirect_body::CanonicalRedirectBody;
use maran_templates::apache::php_site::PhpSite;
use maran_templates::apache::proxy_site::ProxySite;
use maran_templates::apache::redirect_site::RedirectSite;
use maran_templates::apache::site_body::SiteBody;
use maran_templates::apache::site_options_block::SiteOptionsBlock;
use maran_templates::apache::ssl_block::SslBlock;
use maran_templates::apache::static_site::StaticSite;
use maran_templates::nginx::ip_rules::IpRules;

use crate::sites::SitesOpError;
use crate::sites::model::canonical_split::CanonicalSplit;
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::ip_access_mode::IpAccessMode;
use crate::sites::model::site_kind::SiteKind;
use crate::sites::model::site_paths::SitePaths;
use crate::sites::render_vhost::{
    fpm_socket_path, ip_networks, redirect_lines, render_failed, upstream_of,
};

/// Renders the complete Apache vhost for `input`.
///
/// The Apache twin of `render_nginx_vhost` and held to the same invariant: the site's body is
/// rendered ONCE, into a single string, and that same string is handed to the port-80 virtual
/// host and to [`SslBlock::server_body`], so the two halves of a TLS site cannot serve
/// different rules. A canonical host adds a second set of virtual hosts for the names that
/// redirect, written after the site's own in the same file, so removing the site removes both.
///
/// A Node.js site renders as a proxied one: its application listens on a loopback port and the
/// vhost forwards to it. The unit that runs the application is a separate file, written by
/// `write_node_unit`.
///
/// A site with a request limit is refused before this is reached (`render_vhost`), because
/// Apache has no such limit to render.
///
/// # Errors
///
/// Returns [`SitesOpError::InvalidOptions`] when the options do not fit the site and
/// [`SitesOpError::Render`] when a template fails, which can only happen if a template and its
/// render type have drifted apart.
pub(crate) fn render_apache_vhost(
    input: &CreateSiteInput,
    paths: &SitePaths,
) -> Result<String, SitesOpError> {
    input
        .options
        .check_against(&input.kind, &input.domain, &input.aliases)?;
    let split = input
        .options
        .canonical_split(&input.domain, &input.aliases)?;

    let document_root = paths.document_root.display().to_string();
    let access_log = paths.access_log.display().to_string();
    let error_log = paths.error_log.display().to_string();
    // The names the site's own virtual hosts answer to, the first one as `ServerName`: the
    // domain and its aliases, or, when a canonical host redirects one of them away, the
    // names that stay.
    let served: Vec<String> = match &split {
        Some(split) => split.served.clone(),
        None => std::iter::once(&input.domain)
            .chain(&input.aliases)
            .map(|name| name.as_str().to_owned())
            .collect(),
    };
    let (domain, aliases) = served
        .split_first()
        .map_or(("", &[][..]), |(first, rest)| (first.as_str(), rest));

    let fpm_socket = match &input.kind {
        SiteKind::Php { version } => Some(fpm_socket_path(input, version.as_str())),
        SiteKind::Static | SiteKind::ReverseProxy { .. } | SiteKind::Node { .. } => None,
    };
    let upstream = upstream_of(&input.kind);

    let redirects = redirect_lines(input);
    let networks = ip_networks(input);
    let auth_file = (!input.options.basic_auth().is_empty()).then(|| {
        AgentPaths::site_auth_file(&input.domain)
            .display()
            .to_string()
    });
    let options = SiteOptionsBlock {
        client_max_body_size_mib: input
            .options
            .client_max_body_size()
            .map(|size| size.mebibytes()),
        static_cache_days: input.options.static_cache_days().map(|days| days.days()),
        redirects: &redirects,
        auth_user_file: auth_file.as_deref(),
        ip_rules: input.options.ip_access().map(|access| IpRules {
            allow_list: access.mode() == IpAccessMode::AllowList,
            networks: &networks,
        }),
    };

    // The one rendering of what this site serves. Everything below embeds `body`; nothing
    // below re-derives it.
    let body = SiteBody {
        access_log: &access_log,
        error_log: &error_log,
        document_root: &document_root,
        fpm_socket: fpm_socket.as_deref(),
        upstream: upstream.as_deref(),
        options: &options,
    }
    .render_config()
    .map_err(render_failed)?;

    let (certificate_path, certificate_key_path) = certificate_paths(input);
    let ssl = input.certificate.as_ref().map(|_| SslBlock {
        domain,
        aliases,
        certificate_path: &certificate_path,
        certificate_key_path: &certificate_key_path,
        // The same bytes the port-80 block gets, not a second assembly of them.
        server_body: &body,
    });

    let site = match &input.kind {
        SiteKind::Php { .. } => PhpSite {
            domain,
            aliases,
            document_root: &document_root,
            body: &body,
            options: &options,
            ssl,
        }
        .render_config(),
        SiteKind::Static => StaticSite {
            domain,
            aliases,
            document_root: &document_root,
            body: &body,
            options: &options,
            ssl,
        }
        .render_config(),
        SiteKind::ReverseProxy { .. } | SiteKind::Node { .. } => ProxySite {
            domain,
            aliases,
            document_root: &document_root,
            body: &body,
            options: &options,
            ssl,
        }
        .render_config(),
    }
    .map_err(render_failed)?;

    match split {
        Some(split) => {
            let redirect = render_redirect_site(input, &split, &document_root)?;
            // One blank line between the site's own virtual hosts and the redirect's.
            Ok(format!("{site}\n{redirect}"))
        }
        None => Ok(site),
    }
}

/// The certificate chain and key paths of the site, or two empty strings when it has none.
fn certificate_paths(input: &CreateSiteInput) -> (String, String) {
    input.certificate.as_ref().map_or_else(
        || (String::new(), String::new()),
        |certificate| {
            (
                certificate.certificate_path().display().to_string(),
                certificate.key_path().display().to_string(),
            )
        },
    )
}

/// Renders the virtual hosts of the names that redirect to the canonical host.
///
/// # Errors
///
/// Returns [`SitesOpError::Render`] when a template fails.
fn render_redirect_site(
    input: &CreateSiteInput,
    split: &CanonicalSplit,
    document_root: &str,
) -> Result<String, SitesOpError> {
    let body = CanonicalRedirectBody {
        canonical: &split.canonical,
        // A visitor is never sent from a secure site to a plain one.
        secure: input.certificate.is_some(),
    }
    .render_config()
    .map_err(render_failed)?;

    let (certificate_path, certificate_key_path) = certificate_paths(input);
    let (first, rest) = split
        .redirected
        .split_first()
        .map_or(("", &[][..]), |(first, rest)| (first.as_str(), rest));
    let ssl = input.certificate.as_ref().map(|_| SslBlock {
        domain: first,
        aliases: rest,
        certificate_path: &certificate_path,
        certificate_key_path: &certificate_key_path,
        server_body: &body,
    });

    RedirectSite {
        names: &split.redirected,
        document_root,
        body: &body,
        ssl,
    }
    .render_config()
    .map_err(render_failed)
}

#[cfg(test)]
#[path = "../tests/sites/render_apache_vhost_tests.rs"]
mod tests;
