//! Byte-for-byte comparison of every rendered artifact against its golden.
//!
//! The golden diff is the review artifact for a template change: a reviewer
//! reads what the web server or php-fpm will actually be told, not a
//! template's intention (rules/testing.md).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::nginx::php_site::PhpSite;
use maran_templates::nginx::proxy_site::ProxySite;
use maran_templates::nginx::site_body::SiteBody;
use maran_templates::nginx::ssl_block::SslBlock;
use maran_templates::nginx::static_site::StaticSite;
use maran_templates::nginx::suspended_site::SuspendedSite;
use maran_templates::php_fpm::pool::Pool;
use maran_templates::php_fpm::pool_override::PoolOverride;

/// Reads a golden by the name of the render type that produces it.
fn golden(relative: &str) -> String {
    std::fs::read_to_string(format!("tests/golden/{relative}"))
        .unwrap_or_else(|error| panic!("golden {relative} is missing: {error}"))
}

/// The rendered locations of the PHP site both PHP goldens describe.
fn php_body() -> String {
    SiteBody {
        access_log: "/home/acme/logs/example.com.access.log",
        error_log: "/home/acme/logs/example.com.error.log",
        document_root: "/home/acme/sites/example.com",
        fpm_socket: Some("/run/maran/php/acme-8.3.sock"),
        upstream: None,
    }
    .render_config()
    .unwrap()
}

#[test]
fn a_php_site_renders_its_golden() {
    let aliases = vec!["www.example.com".to_owned()];
    let body = php_body();
    let site = PhpSite {
        domain: "example.com",
        aliases: &aliases,
        document_root: "/home/acme/sites/example.com",
        body: &body,
        ssl: None,
    };

    assert_eq!(site.render_config().unwrap(), golden("nginx/php_site.conf"));
}

#[test]
fn a_php_site_with_ssl_renders_its_golden() {
    let aliases = vec!["www.example.com".to_owned()];
    let body = php_body();
    let ssl = SslBlock {
        domain: "example.com",
        aliases: &aliases,
        certificate_path: "/etc/maran/certs/example.com/fullchain.pem",
        certificate_key_path: "/etc/maran/certs/example.com/privkey.pem",
        server_body: &body,
    };
    let site = PhpSite {
        domain: "example.com",
        aliases: &aliases,
        document_root: "/home/acme/sites/example.com",
        body: &body,
        ssl: Some(ssl),
    };

    assert_eq!(
        site.render_config().unwrap(),
        golden("nginx/php_site_ssl.conf")
    );
}

#[test]
fn a_static_site_renders_its_golden() {
    let aliases = vec!["www.static.example".to_owned()];
    let body = SiteBody {
        access_log: "/home/acme/logs/static.example.access.log",
        error_log: "/home/acme/logs/static.example.error.log",
        document_root: "/home/acme/sites/static.example",
        fpm_socket: None,
        upstream: None,
    }
    .render_config()
    .unwrap();
    let site = StaticSite {
        domain: "static.example",
        aliases: &aliases,
        document_root: "/home/acme/sites/static.example",
        body: &body,
        ssl: None,
    };

    assert_eq!(
        site.render_config().unwrap(),
        golden("nginx/static_site.conf")
    );
}

#[test]
fn a_proxy_site_renders_its_golden() {
    let aliases: Vec<String> = vec![];
    let body = SiteBody {
        access_log: "/home/acme/logs/app.example.access.log",
        error_log: "/home/acme/logs/app.example.error.log",
        document_root: "/home/acme/sites/app.example",
        fpm_socket: None,
        upstream: Some("127.0.0.1:3000"),
    }
    .render_config()
    .unwrap();
    let site = ProxySite {
        domain: "app.example",
        aliases: &aliases,
        document_root: "/home/acme/sites/app.example",
        body: &body,
        ssl: None,
    };

    assert_eq!(
        site.render_config().unwrap(),
        golden("nginx/proxy_site.conf")
    );
}

#[test]
fn a_suspended_site_renders_its_golden() {
    let aliases: Vec<String> = vec![];
    let site = SuspendedSite {
        domain: "gone.example",
        aliases: &aliases,
        document_root: "/home/acme/sites/gone.example",
        access_log: "/home/acme/logs/gone.example.access.log",
        error_log: "/home/acme/logs/gone.example.error.log",
    };

    assert_eq!(
        site.render_config().unwrap(),
        golden("nginx/suspended_site.conf")
    );
}

#[test]
fn a_pool_renders_its_golden() {
    let overrides = vec![PoolOverride {
        name: "upload_max_filesize",
        value: "32M",
    }];
    let pool = Pool {
        pool_name: "acme-8.3",
        account: "acme",
        socket_path: "/run/maran/php/acme-8.3.sock",
        web_server_user: "www-data",
        max_children: 10,
        start_servers: 2,
        min_spare_servers: 1,
        max_spare_servers: 3,
        home_directory: "/home/acme",
        session_directory: "/home/acme/.maran/sessions",
        upload_temporary_directory: "/home/acme/.maran/tmp",
        request_terminate_timeout: 300,
        overrides: &overrides,
    };

    assert_eq!(pool.render_config().unwrap(), golden("php_fpm/pool.conf"));
}
