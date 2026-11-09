//! Tests for the `render_vhost` module: how a site's options reach the vhost it
//! is rendered into.
//!
//! The byte-for-byte text of each option is pinned by the goldens in the
//! templates crate; what is decided HERE is which options reach which blocks.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::client_body_size::ClientBodySize;
use maran_agent_core::validation::web::request_rate::RequestRate;
use maran_agent_core::validation::web::upstream::Upstream;

use super::render_vhost;
use crate::sites::SitesOpError;
use crate::sites::fake_site_host::{php_input, php_input_with_options};
use crate::sites::model::canonical_host::CanonicalHost;
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::site_certificate::SiteCertificate;
use crate::sites::model::site_kind::SiteKind;
use crate::sites::model::site_options::SiteOptions;
use crate::sites::model::site_paths::SitePaths;

/// Renders `input` against the paths its own account and domain name.
fn render(input: &CreateSiteInput) -> Result<String, SitesOpError> {
    render_vhost(input, &SitePaths::for_site(&input.account, &input.domain))
}

/// `input` with a certificate installed.
fn secured(input: CreateSiteInput) -> CreateSiteInput {
    let certificate = SiteCertificate::for_domain(&input.domain);

    CreateSiteInput {
        certificate: Some(certificate),
        ..input
    }
}

/// `input` with only a canonical host chosen.
fn with_canonical(input: CreateSiteInput, host: CanonicalHost) -> CreateSiteInput {
    let options = SiteOptions::new(Some(host), None, None, vec![], vec![], None).unwrap();

    CreateSiteInput { options, ..input }
}

#[test]
fn a_site_without_options_renders_the_default_upload_ceiling_and_no_access_rules() {
    let rendered = render(&php_input()).unwrap();

    assert!(rendered.contains("client_max_body_size 512m;"));
    assert!(!rendered.contains("auth_basic"));
    assert!(!rendered.contains("allow "));
    assert_eq!(rendered.matches("server {").count(), 1);
}

#[test]
fn every_option_reaches_the_plain_block_of_a_site_without_a_certificate() {
    let rendered = render(&php_input_with_options()).unwrap();

    assert!(rendered.contains("client_max_body_size 100m;"));
    assert!(rendered.contains("auth_basic_user_file /etc/maran/nginx/auth/example.com.htpasswd;"));
    assert!(rendered.contains("allow 203.0.113.0/24;"));
    assert!(rendered.contains("location = /old {"));
    assert!(rendered.contains("expires 30d;"));
    // The credential file's CONTENT is never part of a vhost.
    assert!(!rendered.contains("$2y$"));
}

#[test]
fn every_option_reaches_the_tls_block_of_a_site_with_a_certificate() {
    // The block a visitor reaches is the 443 one; an option that only reached
    // the redirecting port-80 block would leave the secure half unrestricted.
    let rendered = render(&secured(php_input_with_options())).unwrap();
    let secure = &rendered[rendered.find("listen 443").unwrap()..];

    assert!(secure.contains("client_max_body_size 100m;"));
    assert!(secure.contains("auth_basic \"Restricted\";"));
    assert!(secure.contains("allow 203.0.113.0/24;"));
    assert!(secure.contains("deny all;"));
    assert!(secure.contains("location = /old {"));
    assert!(secure.contains("expires 30d;"));
}

#[test]
fn the_challenge_location_answers_everyone_whatever_the_site_restricts() {
    let rendered = render(&php_input_with_options()).unwrap();
    let start = rendered
        .find("location ^~ /.well-known/acme-challenge/")
        .unwrap();
    let challenge = &rendered[start..start + rendered[start..].find("\n    }\n").unwrap()];

    assert!(challenge.contains("auth_basic off;"), "{challenge}");
    assert!(challenge.contains("allow all;"), "{challenge}");
}

#[test]
fn a_canonical_www_host_serves_the_www_name_and_redirects_the_bare_one_in_its_own_block() {
    let rendered = render(&with_canonical(php_input(), CanonicalHost::Www)).unwrap();

    assert_eq!(rendered.matches("server {").count(), 2);
    // The site's own block answers to the canonical name only.
    assert!(rendered.contains("    server_name www.example.com;\n"));
    // The redirect block answers to the other, keeps the challenge, and goes
    // to the canonical name over the scheme the site is served on.
    assert!(rendered.contains("    server_name example.com;\n"));
    assert!(rendered.contains("return 301 http://www.example.com$request_uri;"));
    assert_eq!(
        rendered
            .matches("location ^~ /.well-known/acme-challenge/")
            .count(),
        2
    );
}

#[test]
fn a_canonical_host_on_a_site_with_a_certificate_redirects_to_https_on_both_ports() {
    let rendered = render(&secured(with_canonical(php_input(), CanonicalHost::Apex))).unwrap();

    // Own blocks: port 80 and 443. Redirect blocks: port 80 and 443.
    assert_eq!(rendered.matches("server {").count(), 4);
    assert_eq!(rendered.matches("listen 443 ssl;").count(), 2);
    assert!(rendered.contains("server_name www.example.com;"));
    assert_eq!(
        rendered
            .matches("return 301 https://example.com$request_uri;")
            .count(),
        2
    );
    assert!(!rendered.contains("http://example.com$request_uri"));
}

#[test]
fn a_canonical_host_the_site_does_not_serve_is_refused_before_anything_is_rendered() {
    let options =
        SiteOptions::new(Some(CanonicalHost::Www), None, None, vec![], vec![], None).unwrap();
    let only_the_domain = CreateSiteInput {
        aliases: vec![],
        options,
        ..php_input()
    };

    assert!(matches!(
        render(&only_the_domain),
        Err(SitesOpError::InvalidOptions(_))
    ));
}

#[test]
fn an_upload_size_on_a_static_site_and_a_cache_rule_on_a_proxied_one_are_refused() {
    let size = SiteOptions::new(
        None,
        Some(ClientBodySize::parse(10).unwrap()),
        None,
        vec![],
        vec![],
        None,
    )
    .unwrap();
    let static_site = CreateSiteInput {
        kind: SiteKind::Static,
        options: size,
        ..php_input()
    };
    let cache = SiteOptions::new(
        None,
        None,
        Some(
            maran_agent_core::validation::web::static_cache_days::StaticCacheDays::parse(5)
                .unwrap(),
        ),
        vec![],
        vec![],
        None,
    )
    .unwrap();
    let proxied = CreateSiteInput {
        kind: SiteKind::ReverseProxy {
            upstream: Upstream::parse("127.0.0.1:3000").unwrap(),
        },
        options: cache,
        ..php_input()
    };

    assert!(matches!(
        render(&static_site),
        Err(SitesOpError::InvalidOptions(_))
    ));
    assert!(matches!(
        render(&proxied),
        Err(SitesOpError::InvalidOptions(_))
    ));
}

/// `input` with a request limit of `per_second` and `burst`.
fn with_request_rate(input: CreateSiteInput, per_second: u32, burst: u32) -> CreateSiteInput {
    let options = SiteOptions::default()
        .with_request_rate(Some(RequestRate::parse(per_second, burst).unwrap()));

    CreateSiteInput { options, ..input }
}

#[test]
fn a_site_without_a_request_limit_renders_no_limit_directive() {
    let rendered = render(&php_input()).unwrap();

    assert!(!rendered.contains("limit_req"), "{rendered}");
}

#[test]
fn a_request_limit_declares_a_zone_named_after_the_primary_domain_and_names_it_once() {
    let rendered = render(&with_request_rate(php_input(), 20, 40)).unwrap();

    assert!(
        rendered.contains("limit_req_zone $binary_remote_addr zone=rl_example.com:1m rate=20r/s;")
    );
    assert!(rendered.contains("limit_req zone=rl_example.com burst=40 nodelay;"));
    assert_eq!(rendered.matches("limit_req zone=").count(), 1, "{rendered}");
}

#[test]
fn a_request_limit_reaches_the_block_that_serves_when_a_certificate_is_installed() {
    let rendered = render(&secured(with_request_rate(php_input(), 20, 40))).unwrap();

    assert_eq!(rendered.matches("limit_req_zone").count(), 1, "{rendered}");
    assert_eq!(rendered.matches("limit_req zone=").count(), 1, "{rendered}");
    assert!(rendered.find("limit_req zone=").unwrap() > rendered.find("listen 443").unwrap());
}

#[test]
fn a_canonical_host_does_not_rename_the_zone() {
    // The canonical host changes which name leads `server_name`; the zone is
    // the site's, and renaming it on a canonical change would leave nginx with
    // a zone for each name and a budget for each.
    let options = SiteOptions::new(Some(CanonicalHost::Www), None, None, vec![], vec![], None)
        .unwrap()
        .with_request_rate(Some(RequestRate::parse(5, 0).unwrap()));
    let rendered = render(&CreateSiteInput {
        options,
        ..php_input()
    })
    .unwrap();

    assert_eq!(rendered.matches("limit_req_zone").count(), 1, "{rendered}");
    assert!(rendered.contains("zone=rl_example.com:1m"), "{rendered}");
}
