//! Tests for [`create_site`].
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside
//! the unit they exercise (rules/testing.md). `create_site.rs` declares this
//! file with `#[path]`, which keeps it a child module and therefore able to
//! reach private items.
//!
//! What is tested here is what `create_site` DECIDES: which content it writes,
//! when it refuses to write at all, and what it leaves behind when nginx says
//! no.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::{Path, PathBuf};

use maran_agent_core::validation::web::domain::Domain;
use maran_agent_core::validation::web::upstream::Upstream;

use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::site_certificate::SiteCertificate;

use crate::sites::model::site_kind::SiteKind;
use crate::sites::{SitesOpError, create_site};

use crate::php::fake_php_host::FakePhpHost;
use crate::sites::fake_site_host::{
    FakeSiteHost, TEST_WORKERS, create_test_site, distro, php_input,
};

/// Counts the non-overlapping occurrences of `needle` in `haystack`.
fn occurrences(haystack: &str, needle: &str) -> usize {
    haystack.matches(needle).count()
}

#[test]
fn a_php_site_is_written_with_its_own_fastcgi_pass() {
    let host = FakeSiteHost::passing();
    let input = php_input();

    let created = create_test_site(&host, &input).unwrap();

    assert_eq!(created.document_root, "/srv/homes/acme/sites/example.com");
    assert_eq!(
        created.config_path,
        "/etc/maran/nginx/sites/example.com.conf"
    );
    let vhost = host.config(Path::new(&created.config_path)).unwrap();
    assert!(
        vhost.contains("fastcgi_pass unix:/run/maran/php/acme-8.3.sock;"),
        "the vhost must point at this account's pool for this version: {vhost}"
    );
    assert!(vhost.contains("server_name example.com www.example.com;"));
}

#[test]
fn a_site_creates_its_document_root_and_log_directory_as_the_account() {
    let host = FakeSiteHost::passing();

    create_test_site(&host, &php_input()).unwrap();

    // Both are inside the customer's home, so both are created by the
    // privilege-dropping seam and never by the root daemon
    // (rules/security.md).
    assert_eq!(
        host.created(),
        vec![
            PathBuf::from("/home/acme/sites/example.com"),
            PathBuf::from("/home/acme/logs"),
        ]
    );
}

#[test]
fn creating_a_site_that_exists_reports_already_exists_and_rewrites_nothing() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    let after_first = host.config(Path::new("/etc/maran/nginx/sites/example.com.conf"));

    let second = create_test_site(&host, &input);

    match second {
        Err(SitesOpError::AlreadyExists { domain }) => assert_eq!(domain, "example.com"),
        other => panic!("expected AlreadyExists, got {other:?}"),
    }
    // The retry must not have touched the file: a re-render would drop a TLS
    // block an unrelated certificate installation had added since.
    assert_eq!(host.writes(), 1);
    assert_eq!(
        host.config(Path::new("/etc/maran/nginx/sites/example.com.conf")),
        after_first
    );
}

#[test]
fn a_rejected_configuration_leaves_no_vhost_behind_and_reports_the_tool_output() {
    let host = FakeSiteHost::passing();
    host.reject_validation("nginx: [emerg] duplicate server_name");

    let outcome = create_test_site(&host, &php_input());

    match outcome {
        Err(SitesOpError::NginxValidation { stderr }) => {
            assert!(stderr.contains("duplicate server_name"), "got {stderr}");
        }
        other => panic!("expected NginxValidation, got {other:?}"),
    }
    assert!(
        host.config(Path::new("/etc/maran/nginx/sites/example.com.conf"))
            .is_none(),
        "a refused configuration must not survive as a file nginx reads next"
    );
}

#[test]
fn a_site_with_a_certificate_serves_the_same_body_on_both_ports() {
    let host = FakeSiteHost::passing();
    let mut input = php_input();
    input.certificate = Some(SiteCertificate::for_domain(&input.domain));

    create_test_site(&host, &input).unwrap();

    let vhost = host
        .config(Path::new("/etc/maran/nginx/sites/example.com.conf"))
        .unwrap();
    // The rule that must never exist on only one of the two server blocks.
    // Assembling the TLS body by hand is what would put it on one: nothing
    // fails, nginx starts, and only the half a browser reaches is wrong.
    let denials = occurrences(&vhost, "location ~ /\\. {");
    assert_eq!(
        denials, 1,
        "the plain-HTTP block redirects, so the dotfile denial belongs to the TLS block: {vhost}"
    );
    assert!(vhost.contains("return 301 https://$host$request_uri;"));
    assert!(vhost.contains("listen 443 ssl;"));
    // The TLS block serves files, so it must carry the document root: a 443
    // block without one serves from nginx's compiled-in default.
    let ssl_block = vhost.split("listen 443 ssl;").nth(1).unwrap();
    assert!(
        ssl_block.contains("root /srv/homes/acme/sites/example.com;"),
        "the TLS block must serve the same root: {ssl_block}"
    );
    assert!(ssl_block.contains("fastcgi_pass unix:/run/maran/php/acme-8.3.sock;"));
    // The logs are the same seam one directive higher: port 80 only redirects,
    // so a log declared there records nothing and every real request lands in
    // nginx's shared, root-owned default file — one file holding every
    // tenant's HTTPS traffic, in a product whose isolation story is per-account
    // ownership.
    assert!(
        ssl_block.contains("access_log /home/acme/logs/example.com.access.log;"),
        "the TLS block must write the site's own access log: {ssl_block}"
    );
    assert!(
        ssl_block.contains("error_log /home/acme/logs/example.com.error.log;"),
        "the TLS block must write the site's own error log: {ssl_block}"
    );
}

#[test]
fn a_static_site_and_a_proxied_site_render_their_own_shapes() {
    let host = FakeSiteHost::passing();

    let mut statics = php_input();
    statics.kind = SiteKind::Static;
    statics.domain = Domain::parse("static.example").unwrap();
    statics.aliases = Vec::new();
    create_test_site(&host, &statics).unwrap();

    let mut proxied = php_input();
    proxied.kind = SiteKind::ReverseProxy {
        upstream: Upstream::parse("127.0.0.1:3000").unwrap(),
    };
    proxied.domain = Domain::parse("app.example").unwrap();
    proxied.aliases = Vec::new();
    create_test_site(&host, &proxied).unwrap();

    let static_vhost = host
        .config(Path::new("/etc/maran/nginx/sites/static.example.conf"))
        .unwrap();
    assert!(static_vhost.contains("try_files $uri $uri/ =404;"));
    assert!(!static_vhost.contains("fastcgi_pass"));

    let proxy_vhost = host
        .config(Path::new("/etc/maran/nginx/sites/app.example.conf"))
        .unwrap();
    assert!(proxy_vhost.contains("proxy_pass http://127.0.0.1:3000;"));
    // A proxied site serves no file of its own, so its body carries no `index`
    // and no `root` — the only `root` in the vhost is the one inside the ACME
    // location, which exists so a certificate can still be issued and renewed.
    assert!(!proxy_vhost.contains("index "));
    assert_eq!(
        occurrences(&proxy_vhost, "root /srv/homes/acme/sites/app.example;"),
        1
    );
}

#[test]
fn a_new_php_site_gets_the_pool_its_own_vhost_points_at() {
    // The defect this closes: for as long as `update_site_php_version` was the
    // only writer of a pool anywhere in the agent, a PHP site that was created
    // and never switched had a `fastcgi_pass` naming a socket nothing had
    // bound. The panel reported the creation as a success and every request to
    // the site was a 502; switching the version and switching back was the only
    // way to make it serve.
    let host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3"]);
    let input = php_input();

    let created = create_site(&host, &php_host, distro(), &input, TEST_WORKERS, &[]).unwrap();

    let vhost = host.config(Path::new(&created.config_path)).unwrap();
    let pool = php_host
        .config(Path::new("/etc/php/8.3/fpm/pool.d/acme.conf"))
        .expect("creating a PHP site must write the pool its vhost will point at");
    assert!(
        vhost.contains("fastcgi_pass unix:/run/maran/php/acme-8.3.sock;"),
        "{vhost}"
    );
    assert!(
        pool.contains("listen = /run/maran/php/acme-8.3.sock"),
        "the pool must listen on exactly the socket the vhost names: {pool}"
    );
}

#[test]
fn a_new_php_sites_pool_carries_the_plans_worker_budget() {
    let host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3"]);

    create_site(&host, &php_host, distro(), &php_input(), 3, &[]).unwrap();

    let pool = php_host
        .config(Path::new("/etc/php/8.3/fpm/pool.d/acme.conf"))
        .unwrap();
    assert!(pool.contains("pm.max_children = 3"), "{pool}");
}

#[test]
fn a_static_site_gets_no_pool_because_it_speaks_to_no_php_master() {
    let host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3"]);
    let statics = CreateSiteInput {
        kind: SiteKind::Static,
        ..php_input()
    };

    create_site(&host, &php_host, distro(), &statics, TEST_WORKERS, &[]).unwrap();

    assert_eq!(
        php_host.writes(),
        0,
        "a static site names no fastcgi_pass, so writing it a pool would reload a php-fpm master \
         for a site that never speaks to one"
    );
}

#[test]
fn a_php_version_this_host_does_not_have_is_refused_before_any_vhost_is_written() {
    // Refused where nothing has been written yet, so a failed creation leaves
    // no vhost claiming a domain the panel then believes is taken.
    let host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3"]);
    let input = CreateSiteInput {
        kind: SiteKind::Php {
            version: maran_agent_core::validation::web::php_version::PhpVersion::parse("8.4")
                .unwrap(),
        },
        ..php_input()
    };

    let refusal = create_site(&host, &php_host, distro(), &input, TEST_WORKERS, &[]);

    match refusal {
        Err(SitesOpError::PhpVersionNotInstalled { version }) => assert_eq!(version, "8.4"),
        other => panic!("expected PhpVersionNotInstalled, got {other:?}"),
    }
    assert_eq!(host.writes(), 0, "no vhost may be left behind");
}
