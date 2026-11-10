//! Byte-for-byte comparison of every rendered Apache artifact against its golden.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::apache::canonical_redirect_body::CanonicalRedirectBody;
use maran_templates::apache::panel_acme_site::PanelAcmeSite;
use maran_templates::apache::php_site::PhpSite;
use maran_templates::apache::proxy_site::ProxySite;
use maran_templates::apache::redirect_site::RedirectSite;
use maran_templates::apache::site_body::SiteBody;
use maran_templates::apache::site_options_block::SiteOptionsBlock;
use maran_templates::apache::ssl_block::SslBlock;
use maran_templates::apache::static_site::StaticSite;
use maran_templates::apache::suspended_site::SuspendedSite;
use maran_templates::nginx::ip_rules::IpRules;
use maran_templates::nginx::redirect_line::RedirectLine;
use maran_templates::systemd::node_site_unit::NodeSiteUnit;

const ROOT: &str = "/home/acme/sites/example.com";
const ACCESS: &str = "/var/log/maran/sites/acme/example.com.access.log";
const ERROR: &str = "/var/log/maran/sites/acme/example.com.error.log";
const SOCKET: &str = "/run/maran-php/acme-8.3.sock";
const AUTH: &str = "/etc/maran/nginx/auth/example.com.htpasswd";

/// Reads a golden by the name of the render type that produces it.
fn golden(relative: &str) -> String {
    std::fs::read_to_string(format!("tests/golden/{relative}"))
        .unwrap_or_else(|error| panic!("golden {relative} is missing: {error}"))
}

/// Which arm of the body a test renders.
#[derive(Clone, Copy)]
enum Arm {
    Static,
    Php,
    Proxy,
}

/// The one rendering of a body, as the operation hands it to both halves.
fn body(arm: Arm, options: &SiteOptionsBlock<'_>) -> String {
    SiteBody {
        access_log: ACCESS,
        error_log: ERROR,
        document_root: ROOT,
        fpm_socket: matches!(arm, Arm::Php).then_some(SOCKET),
        upstream: matches!(arm, Arm::Proxy).then_some("127.0.0.1:3000"),
        options,
    }
    .render_config()
    .unwrap()
}

fn ssl<'a>(aliases: &'a [String], server_body: &'a str) -> SslBlock<'a> {
    SslBlock {
        domain: "example.com",
        aliases,
        certificate_path: "/etc/maran/certs/example.com/fullchain.pem",
        certificate_key_path: "/etc/maran/certs/example.com/privkey.pem",
        server_body,
    }
}

/// Renders the site template of `arm`.
fn render(arm: Arm, options: &SiteOptionsBlock<'_>, with_ssl: bool) -> String {
    let aliases = vec!["www.example.com".to_owned()];
    let body = body(arm, options);
    let tls = with_ssl.then(|| ssl(&aliases, &body));
    match arm {
        Arm::Static => StaticSite {
            domain: "example.com",
            aliases: &aliases,
            document_root: ROOT,
            body: &body,
            options,
            ssl: tls,
        }
        .render_config(),
        Arm::Php => PhpSite {
            domain: "example.com",
            aliases: &aliases,
            document_root: ROOT,
            body: &body,
            options,
            ssl: tls,
        }
        .render_config(),
        Arm::Proxy => ProxySite {
            domain: "example.com",
            aliases: &aliases,
            document_root: ROOT,
            body: &body,
            options,
            ssl: tls,
        }
        .render_config(),
    }
    .unwrap()
}

#[test]
fn the_plain_sites_render_their_goldens() {
    let none = SiteOptionsBlock::NONE;

    assert_eq!(
        render(Arm::Static, &none, false),
        golden("apache/static_site.conf")
    );
    assert_eq!(
        render(Arm::Php, &none, false),
        golden("apache/php_site.conf")
    );
    assert_eq!(
        render(Arm::Proxy, &none, false),
        golden("apache/proxy_site.conf")
    );
}

#[test]
fn the_sites_with_a_certificate_render_their_goldens() {
    let none = SiteOptionsBlock::NONE;

    assert_eq!(
        render(Arm::Static, &none, true),
        golden("apache/static_site_ssl.conf")
    );
    assert_eq!(
        render(Arm::Php, &none, true),
        golden("apache/php_site_ssl.conf")
    );
    assert_eq!(
        render(Arm::Proxy, &none, true),
        golden("apache/proxy_site_ssl.conf")
    );
}

#[test]
fn the_sites_with_every_option_render_their_goldens() {
    let redirects = [
        RedirectLine {
            path: "/old",
            prefix: false,
            target: "/new",
            permanent: true,
        },
        RedirectLine {
            path: "/legacy/",
            prefix: true,
            target: "/modern/",
            permanent: false,
        },
    ];
    let networks = vec!["203.0.113.0/24".to_owned(), "198.51.100.7/32".to_owned()];
    let options = SiteOptionsBlock {
        client_max_body_size_mib: Some(100),
        static_cache_days: Some(30),
        redirects: &redirects,
        auth_user_file: Some(AUTH),
        ip_rules: Some(IpRules {
            allow_list: true,
            networks: &networks,
        }),
    };

    assert_eq!(
        render(Arm::Static, &options, false),
        golden("apache/static_site_options.conf")
    );
    assert_eq!(
        render(Arm::Php, &options, false),
        golden("apache/php_site_options.conf")
    );
    assert_eq!(
        render(Arm::Proxy, &options, false),
        golden("apache/proxy_site_options.conf")
    );
}

#[test]
fn a_deny_list_grants_everyone_else_and_a_password_alone_has_no_address_rule() {
    let networks = vec!["192.0.2.0/24".to_owned()];
    let deny = SiteOptionsBlock {
        ip_rules: Some(IpRules {
            allow_list: false,
            networks: &networks,
        }),
        ..SiteOptionsBlock::NONE
    };
    let password = SiteOptionsBlock {
        auth_user_file: Some(AUTH),
        ..SiteOptionsBlock::NONE
    };

    assert_eq!(
        render(Arm::Static, &deny, false),
        golden("apache/static_site_deny_list.conf")
    );
    assert_eq!(
        render(Arm::Php, &password, false),
        golden("apache/php_site_auth_only.conf")
    );
}

#[test]
fn a_site_without_options_renders_no_access_rules_and_no_redirects() {
    let plain = render(Arm::Static, &SiteOptionsBlock::NONE, false);

    assert!(!plain.contains("AuthType"));
    assert!(!plain.contains("RequireAll"));
    assert!(!plain.contains("RedirectMatch"));
}

#[test]
fn the_redirect_sites_render_their_goldens() {
    let names = vec!["www.example.com".to_owned(), "old.example.com".to_owned()];
    for (secure, with_ssl, file) in [
        (false, false, "apache/redirect_site.conf"),
        (true, true, "apache/redirect_site_ssl.conf"),
    ] {
        let body = CanonicalRedirectBody {
            canonical: "example.com",
            secure,
        }
        .render_config()
        .unwrap();
        let tls = with_ssl.then(|| SslBlock {
            domain: "www.example.com",
            aliases: &names[1..],
            certificate_path: "/etc/maran/certs/example.com/fullchain.pem",
            certificate_key_path: "/etc/maran/certs/example.com/privkey.pem",
            server_body: &body,
        });
        let rendered = RedirectSite {
            names: &names,
            document_root: ROOT,
            body: &body,
            ssl: tls,
        }
        .render_config()
        .unwrap();

        assert_eq!(rendered, golden(file));
    }
}

#[test]
fn the_suspended_stub_renders_its_golden_and_ends_with_a_newline() {
    let aliases = vec!["www.example.com".to_owned()];
    let rendered = SuspendedSite {
        domain: "example.com",
        aliases: &aliases,
        document_root: ROOT,
        access_log: ACCESS,
        error_log: ERROR,
    }
    .render_config()
    .unwrap();

    assert_eq!(rendered, golden("apache/suspended_site.conf"));
    assert!(rendered.ends_with("</VirtualHost>\n"));
}

#[test]
fn the_panel_challenge_host_renders_its_golden() {
    let rendered = PanelAcmeSite {
        server_name: "panel.example.net",
        challenge_directory: "/var/lib/maran-acme",
        access_log: "/var/log/maran/apache-access.log",
        error_log: "/var/log/maran/apache-error.log",
    }
    .render_config()
    .unwrap();

    assert_eq!(rendered, golden("apache/panel_acme_site.conf"));
}

#[test]
fn the_node_unit_renders_its_golden_and_runs_the_validated_words_only() {
    let arguments = vec![
        "server.js".to_owned(),
        "--max-old-space-size=256".to_owned(),
    ];
    let rendered = NodeSiteUnit {
        account: "acme",
        domain: "example.com",
        node_major: "20",
        document_root: ROOT,
        home_directory: "/home/acme",
        port: 3000,
        executable: "/usr/bin/node",
        arguments: &arguments,
    }
    .render_config()
    .unwrap();

    assert_eq!(rendered, golden("systemd/node_site_unit.service"));
    assert_eq!(rendered.matches("ExecStart=").count(), 1);
}
