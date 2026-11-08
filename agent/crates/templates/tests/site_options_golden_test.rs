//! Byte-for-byte goldens for a vhost rendered with per-site options (issue #151),
//! and the guards that hold the options to their promises.
//!
//! The golden diff is the review artifact: a reviewer reads what nginx will be
//! told for each option group, not a template's intention (rules/testing.md).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::nginx::canonical_redirect_body::CanonicalRedirectBody;
use maran_templates::nginx::ip_rules::IpRules;
use maran_templates::nginx::php_site::PhpSite;
use maran_templates::nginx::proxy_site::ProxySite;
use maran_templates::nginx::redirect_line::RedirectLine;
use maran_templates::nginx::redirect_site::RedirectSite;
use maran_templates::nginx::site_body::SiteBody;
use maran_templates::nginx::site_options_block::SiteOptionsBlock;
use maran_templates::nginx::ssl_block::SslBlock;
use maran_templates::nginx::static_site::StaticSite;

/// Reads a golden by the name of the render type that produces it.
fn golden(relative: &str) -> String {
    std::fs::read_to_string(format!("tests/golden/{relative}"))
        .unwrap_or_else(|error| panic!("golden {relative} is missing: {error}"))
}

/// The credential file the PHP fixtures name.
const PHP_AUTH_FILE: &str = "/etc/maran/nginx/auth/example.com.htpasswd";

/// The networks of the allow-list fixture.
fn allowed_networks() -> Vec<String> {
    vec!["203.0.113.0/24".to_owned(), "2001:db8::/32".to_owned()]
}

/// The two redirect rules of the PHP fixture: an exact one and a prefix one.
const PHP_REDIRECTS: [RedirectLine<'static>; 2] = [
    RedirectLine {
        path: "/old",
        prefix: false,
        target: "/new",
        permanent: true,
    },
    RedirectLine {
        path: "/blog/",
        prefix: true,
        target: "https://blog.example.org/",
        permanent: false,
    },
];

/// Renders the PHP fixture's body for `options`.
fn php_body(options: &SiteOptionsBlock<'_>) -> String {
    SiteBody {
        access_log: "/var/log/maran/sites/acme/example.com.access.log",
        error_log: "/var/log/maran/sites/acme/example.com.error.log",
        document_root: "/home/acme/sites/example.com",
        fpm_socket: Some("/run/maran-php/acme-8.3.sock"),
        upstream: None,
        options,
    }
    .render_config()
    .unwrap()
}

/// Renders a PHP site, with or without a certificate, for `options`.
fn php_site(options: &SiteOptionsBlock<'_>, tls: bool) -> String {
    let aliases = vec!["www.example.com".to_owned()];
    let body = php_body(options);
    let ssl = tls.then_some(SslBlock {
        domain: "example.com",
        aliases: &aliases,
        certificate_path: "/etc/maran/certs/example.com/fullchain.pem",
        certificate_key_path: "/etc/maran/certs/example.com/privkey.pem",
        server_body: &body,
    });
    PhpSite {
        domain: "example.com",
        aliases: &aliases,
        document_root: "/home/acme/sites/example.com",
        body: &body,
        options,
        ssl,
    }
    .render_config()
    .unwrap()
}

#[test]
fn a_php_site_with_every_option_renders_its_golden() {
    let networks = allowed_networks();
    let options = SiteOptionsBlock {
        client_max_body_size_mib: Some(100),
        static_cache_days: Some(30),
        redirects: &PHP_REDIRECTS,
        auth_user_file: Some(PHP_AUTH_FILE),
        ip_rules: Some(IpRules {
            allow_list: true,
            networks: &networks,
        }),
        request_rate: None,
    };

    assert_eq!(
        php_site(&options, false),
        golden("nginx/php_site_options.conf")
    );
}

#[test]
fn a_php_site_with_every_option_and_a_certificate_renders_its_golden() {
    let networks = allowed_networks();
    let options = SiteOptionsBlock {
        client_max_body_size_mib: Some(100),
        static_cache_days: Some(30),
        redirects: &PHP_REDIRECTS,
        auth_user_file: Some(PHP_AUTH_FILE),
        ip_rules: Some(IpRules {
            allow_list: true,
            networks: &networks,
        }),
        request_rate: None,
    };

    assert_eq!(
        php_site(&options, true),
        golden("nginx/php_site_options_ssl.conf")
    );
}

#[test]
fn a_static_site_with_a_deny_list_a_cache_rule_and_a_root_redirect_renders_its_golden() {
    let aliases = vec!["www.static.example".to_owned()];
    let networks = vec!["198.51.100.0/24".to_owned()];
    let redirects = [RedirectLine {
        path: "/",
        prefix: true,
        target: "https://new.example.org/",
        permanent: true,
    }];
    let options = SiteOptionsBlock {
        client_max_body_size_mib: None,
        static_cache_days: Some(7),
        redirects: &redirects,
        auth_user_file: None,
        ip_rules: Some(IpRules {
            allow_list: false,
            networks: &networks,
        }),
        request_rate: None,
    };
    let body = SiteBody {
        access_log: "/var/log/maran/sites/acme/static.example.access.log",
        error_log: "/var/log/maran/sites/acme/static.example.error.log",
        document_root: "/home/acme/sites/static.example",
        fpm_socket: None,
        upstream: None,
        options: &options,
    }
    .render_config()
    .unwrap();
    let site = StaticSite {
        domain: "static.example",
        aliases: &aliases,
        document_root: "/home/acme/sites/static.example",
        body: &body,
        options: &options,
        ssl: None,
    };

    assert_eq!(
        site.render_config().unwrap(),
        golden("nginx/static_site_options.conf")
    );
}

#[test]
fn a_proxy_site_with_a_password_a_body_size_and_a_redirect_renders_its_golden() {
    let aliases: Vec<String> = vec![];
    let redirects = [RedirectLine {
        path: "/status",
        prefix: false,
        target: "/maintenance",
        permanent: false,
    }];
    let options = SiteOptionsBlock {
        client_max_body_size_mib: Some(5),
        static_cache_days: None,
        redirects: &redirects,
        auth_user_file: Some("/etc/maran/nginx/auth/app.example.htpasswd"),
        ip_rules: None,
        request_rate: None,
    };
    let body = SiteBody {
        access_log: "/var/log/maran/sites/acme/app.example.access.log",
        error_log: "/var/log/maran/sites/acme/app.example.error.log",
        document_root: "/home/acme/sites/app.example",
        fpm_socket: None,
        upstream: Some("127.0.0.1:3000"),
        options: &options,
    }
    .render_config()
    .unwrap();
    let site = ProxySite {
        domain: "app.example",
        aliases: &aliases,
        document_root: "/home/acme/sites/app.example",
        body: &body,
        options: &options,
        ssl: None,
    };

    assert_eq!(
        site.render_config().unwrap(),
        golden("nginx/proxy_site_options.conf")
    );
}

/// Renders the canonical-host redirect for `example.com` → `www.example.com`.
fn redirect_site(tls: bool) -> String {
    let names = vec!["example.com".to_owned()];
    let aliases: Vec<String> = vec![];
    let body = CanonicalRedirectBody {
        canonical: "www.example.com",
        secure: tls,
    }
    .render_config()
    .unwrap();
    let ssl = tls.then_some(SslBlock {
        domain: "example.com",
        aliases: &aliases,
        certificate_path: "/etc/maran/certs/example.com/fullchain.pem",
        certificate_key_path: "/etc/maran/certs/example.com/privkey.pem",
        server_body: &body,
    });
    RedirectSite {
        names: &names,
        document_root: "/home/acme/sites/example.com",
        body: &body,
        ssl,
    }
    .render_config()
    .unwrap()
}

#[test]
fn a_canonical_host_redirect_renders_its_golden() {
    assert_eq!(redirect_site(false), golden("nginx/redirect_site.conf"));
}

#[test]
fn a_canonical_host_redirect_with_a_certificate_renders_its_golden() {
    assert_eq!(redirect_site(true), golden("nginx/redirect_site_ssl.conf"));
}

/// The text of the certificate-challenge location of a rendered vhost: from its
/// `location` line to the first closing brace at the same indentation.
fn challenge_location(rendered: &str) -> &str {
    let start = rendered
        .find("    location ^~ /.well-known/acme-challenge/ {")
        .expect("the vhost has a challenge location");
    let end = rendered[start..]
        .find("\n    }\n")
        .expect("the challenge location is closed")
        + start;
    &rendered[start..end]
}

#[test]
fn the_challenge_location_stays_open_under_every_access_restriction() {
    // Every combination of the two restrictions, with and without a
    // certificate: the location must say it answers everyone whenever the
    // body restricts anybody, and must be the original text when it does not.
    let networks = allowed_networks();
    let unrestricted = php_site(&SiteOptionsBlock::NONE, false);
    let open_text = challenge_location(&unrestricted);
    assert!(!open_text.contains("auth_basic"), "{open_text}");

    for password in [false, true] {
        for list in [false, true] {
            for tls in [false, true] {
                let options = SiteOptionsBlock {
                    client_max_body_size_mib: None,
                    static_cache_days: None,
                    redirects: &[],
                    auth_user_file: password.then_some(PHP_AUTH_FILE),
                    ip_rules: list.then_some(IpRules {
                        allow_list: true,
                        networks: &networks,
                    }),
                    request_rate: None,
                };
                let rendered = php_site(&options, tls);
                let location = challenge_location(&rendered);

                if password || list {
                    assert!(
                        location.contains("auth_basic off;") && location.contains("allow all;"),
                        "password={password} list={list} tls={tls}: {location}"
                    );
                    assert!(
                        location.starts_with(open_text),
                        "password={password} list={list} tls={tls}: the original lines must stay"
                    );
                } else {
                    assert_eq!(location, open_text, "tls={tls}");
                }
            }
        }
    }
}

#[test]
fn the_restrictions_reach_the_tls_block_too() {
    // The 443 block is the one a visitor reaches; a restriction written only
    // in the port-80 body would leave the secure half open.
    let networks = allowed_networks();
    let options = SiteOptionsBlock {
        client_max_body_size_mib: None,
        static_cache_days: None,
        redirects: &[],
        auth_user_file: Some(PHP_AUTH_FILE),
        ip_rules: Some(IpRules {
            allow_list: true,
            networks: &networks,
        }),
        request_rate: None,
    };
    let rendered = php_site(&options, true);
    let secure = &rendered[rendered.find("listen 443").unwrap()..];

    assert!(secure.contains("auth_basic \"Restricted\";"));
    assert!(secure.contains(&format!("auth_basic_user_file {PHP_AUTH_FILE};")));
    assert!(secure.contains("allow 203.0.113.0/24;"));
    assert!(secure.contains("deny all;"));
}

#[test]
fn a_site_without_options_renders_the_same_bytes_as_before_they_existed() {
    // The existing goldens pin this too; this one names it.
    assert_eq!(
        php_site(&SiteOptionsBlock::NONE, false),
        golden("nginx/php_site.conf")
    );
    assert_eq!(
        php_site(&SiteOptionsBlock::NONE, true),
        golden("nginx/php_site_ssl.conf")
    );
}

#[test]
fn the_upload_size_replaces_the_default_and_never_repeats_the_directive() {
    let options = SiteOptionsBlock {
        client_max_body_size_mib: Some(8),
        ..SiteOptionsBlock::NONE
    };
    let body = php_body(&options);

    assert_eq!(body.matches("client_max_body_size").count(), 1, "{body}");
    assert!(body.contains("client_max_body_size 8m;"));
    assert!(!body.contains("512m"));
}
