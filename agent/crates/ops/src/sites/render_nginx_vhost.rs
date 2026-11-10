//! Rendering a site's nginx vhost, with ONE rendering of the rules it serves.

use maran_agent_core::agent_paths::AgentPaths;
use maran_templates::nginx::canonical_redirect_body::CanonicalRedirectBody;
use maran_templates::nginx::ip_rules::IpRules;
use maran_templates::nginx::php_site::PhpSite;
use maran_templates::nginx::proxy_site::ProxySite;
use maran_templates::nginx::redirect_site::RedirectSite;
use maran_templates::nginx::request_rate_line::RequestRateLine;
use maran_templates::nginx::site_body::SiteBody;
use maran_templates::nginx::site_options_block::SiteOptionsBlock;
use maran_templates::nginx::ssl_block::SslBlock;
use maran_templates::nginx::static_site::StaticSite;

use crate::sites::SitesOpError;
use crate::sites::model::canonical_split::CanonicalSplit;
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::ip_access_mode::IpAccessMode;
use crate::sites::model::site_kind::SiteKind;
use crate::sites::model::site_paths::SitePaths;
use crate::sites::render_vhost::{
    fpm_socket_path, ip_networks, redirect_lines, render_failed, upstream_of,
};

/// What a site's `limit_req_zone` name starts with; the primary domain follows.
const REQUEST_ZONE_PREFIX: &str = "rl_";

/// Renders the complete nginx vhost for `input`.
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
/// The site's options (issue #151), the request limit (issue #159) among them,
/// are rendered into that same body, so they reach both halves too; a site with none renders the bytes it rendered before
/// they existed. A canonical host adds a second set of `server` blocks, written
/// after the site's own in the same file, for the names that redirect; the
/// site's own blocks then serve the canonical name and the names that do not.
/// The file therefore still holds everything one site is, and removing the site
/// removes both.
///
/// # Errors
///
/// Returns [`SitesOpError::InvalidOptions`] when the options do not fit the
/// site: a canonical host it does not serve, an upload size on a static site,
/// a cache rule on a reverse-proxied one. Returns [`SitesOpError::Render`] when
/// a template fails, which can only happen if a template and its render type
/// have drifted apart.
pub(crate) fn render_nginx_vhost(
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
    // The names the site's own server blocks answer to, the first one written
    // as `server_name`'s head: the domain and its aliases, or, when a canonical
    // host redirects one of them away, the names that stay.
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
    let node_upstream = upstream_of(&input.kind);
    let upstream = node_upstream.as_deref();

    let redirects = redirect_lines(input);
    let networks = ip_networks(input);
    let auth_file = (!input.options.basic_auth().is_empty()).then(|| {
        AgentPaths::site_auth_file(&input.domain)
            .display()
            .to_string()
    });
    // The one place the zone's name is derived: the file's `limit_req_zone` and
    // the body's `limit_req` both read it from the options block, so they cannot
    // name different zones. Named after the primary domain, which is unique per
    // site, and not after the first served name, which a canonical host changes.
    let request_zone = format!("{REQUEST_ZONE_PREFIX}{}", input.domain.as_str());
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
        request_rate: input.options.request_rate().map(|rate| RequestRateLine {
            zone: &request_zone,
            per_second: rate.per_second(),
            burst: rate.burst(),
        }),
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
        options: &options,
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
            // One blank line between the site's own blocks and the redirect's.
            Ok(format!("{site}\n{redirect}"))
        }
        None => Ok(site),
    }
}

/// Renders the `server` blocks of the names that redirect to the canonical host.
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
