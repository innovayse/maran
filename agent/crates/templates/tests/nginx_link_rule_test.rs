//! Every `root` a rendered vhost writes must be covered by the symbolic-link rule.
//!
//! The goldens pin the exact bytes; this walk pins the PROPERTY, so a new site
//! shape or a new `root` written without the rule fails here even when someone
//! regenerates a golden to match it. The walk reads the render the way nginx
//! resolves the directive: a `location` inherits the enclosing `server`'s rule
//! unless it sets its own, and only `if_not_owner` with no `from=` counts.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::nginx::php_site::PhpSite;
use maran_templates::nginx::proxy_site::ProxySite;
use maran_templates::nginx::site_body::SiteBody;
use maran_templates::nginx::ssl_block::SslBlock;
use maran_templates::nginx::static_site::StaticSite;
use maran_templates::nginx::suspended_site::SuspendedSite;

/// The document root every shape renders.
const ROOT: &str = "/home/acme/sites/example.com";

/// The only directive value that refuses a link another account owns.
const REFUSING: &str = "if_not_owner";

/// The site shapes that can be rendered as a vhost.
#[derive(Clone, Copy, Debug)]
enum Shape {
    Php,
    Static,
    Proxy,
}

/// A `location` block as the walk sees it.
struct Location {
    has_root: bool,
    rule: Option<String>,
}

/// A `server` block as the walk sees it.
struct Server {
    has_root: bool,
    rule: Option<String>,
    locations: Vec<Location>,
}

/// Reads one directive into the block it sits in.
fn read_directive(line: &str, depth: usize, servers: &mut [Server]) {
    let has_root = line.starts_with("root ");
    let rule = line
        .strip_prefix("disable_symlinks ")
        .map(|value| value.trim_end_matches(';').trim().to_owned());
    let Some(server) = servers.last_mut() else {
        return;
    };
    if depth == 1 {
        server.has_root |= has_root;
        if rule.is_some() {
            server.rule = rule;
        }
    } else if let Some(location) = server.locations.last_mut() {
        location.has_root |= has_root;
        if rule.is_some() {
            location.rule = rule;
        }
    }
}

/// Parses a render into its server blocks, ignoring comments and blank lines.
fn parse(render: &str) -> Vec<Server> {
    let mut servers: Vec<Server> = Vec::new();
    let mut depth = 0usize;
    for raw in render.lines() {
        let line = raw.trim();
        if line.is_empty() || line.starts_with('#') {
            continue;
        }
        if line == "}" {
            depth = depth.saturating_sub(1);
        } else if line.ends_with('{') {
            depth += 1;
            if depth == 1 {
                servers.push(Server {
                    has_root: false,
                    rule: None,
                    locations: Vec::new(),
                });
            } else if let Some(server) = servers.last_mut() {
                server.locations.push(Location {
                    has_root: false,
                    rule: None,
                });
            }
        } else {
            read_directive(line, depth, &mut servers);
        }
    }
    servers
}

/// How many `root` directives the render writes and how many of them are not
/// covered by the refusing rule.
fn walk(render: &str) -> (usize, usize) {
    let mut roots = 0;
    let mut uncovered = 0;
    for server in parse(render) {
        if server.has_root {
            roots += 1;
            if server.rule.as_deref() != Some(REFUSING) {
                uncovered += 1;
            }
        }
        for location in &server.locations {
            if location.has_root {
                roots += 1;
                let effective = location.rule.as_ref().or(server.rule.as_ref());
                if effective.map(String::as_str) != Some(REFUSING) {
                    uncovered += 1;
                }
            }
        }
    }
    (roots, uncovered)
}

/// Renders one site shape, with or without a certificate.
fn site(shape: Shape, tls: bool) -> String {
    let aliases = vec!["www.example.com".to_owned()];
    let body = SiteBody {
        access_log: "/var/log/maran/sites/acme/example.com.access.log",
        error_log: "/var/log/maran/sites/acme/example.com.error.log",
        document_root: ROOT,
        fpm_socket: match shape {
            Shape::Php => Some("/run/maran-php/acme-8.3.sock"),
            _ => None,
        },
        upstream: match shape {
            Shape::Proxy => Some("127.0.0.1:3000"),
            _ => None,
        },
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
    match shape {
        Shape::Php => PhpSite {
            domain: "example.com",
            aliases: &aliases,
            document_root: ROOT,
            body: &body,
            ssl,
        }
        .render_config()
        .unwrap(),
        Shape::Static => StaticSite {
            domain: "example.com",
            aliases: &aliases,
            document_root: ROOT,
            body: &body,
            ssl,
        }
        .render_config()
        .unwrap(),
        Shape::Proxy => ProxySite {
            domain: "example.com",
            aliases: &aliases,
            document_root: ROOT,
            body: &body,
            ssl,
        }
        .render_config()
        .unwrap(),
    }
}

/// Renders the suspended stub.
fn suspended() -> String {
    let aliases: Vec<String> = vec![];
    SuspendedSite {
        domain: "example.com",
        aliases: &aliases,
        document_root: ROOT,
        access_log: "/var/log/maran/sites/acme/example.com.access.log",
        error_log: "/var/log/maran/sites/acme/example.com.error.log",
    }
    .render_config()
    .unwrap()
}

#[test]
fn every_root_a_site_vhost_renders_refuses_links_to_another_owner() {
    // (shape, tls, roots the shape writes): the challenge location's root, plus
    // the body's own root for a file-serving shape. A proxied site opens no file
    // of its own, so its challenge location is its only root.
    let cases = [
        (Shape::Php, false, 2),
        (Shape::Php, true, 2),
        (Shape::Static, false, 2),
        (Shape::Static, true, 2),
        (Shape::Proxy, false, 1),
        (Shape::Proxy, true, 1),
    ];

    for (shape, tls, expected_roots) in cases {
        let render = site(shape, tls);
        let (roots, uncovered) = walk(&render);

        assert_eq!(
            roots, expected_roots,
            "{shape:?} tls={tls}: roots seen\n{render}"
        );
        assert_eq!(
            uncovered, 0,
            "{shape:?} tls={tls}: a root has no link rule\n{render}"
        );
    }
}

#[test]
fn a_certificate_adds_a_second_server_block_and_each_one_is_covered() {
    for shape in [Shape::Php, Shape::Static, Shape::Proxy] {
        assert_eq!(parse(&site(shape, false)).len(), 1);
        assert_eq!(parse(&site(shape, true)).len(), 2);
    }
}

#[test]
fn the_suspended_stub_has_one_root_and_it_is_covered() {
    assert_eq!(walk(&suspended()), (1, 0));
}

#[test]
fn a_root_with_no_link_rule_is_reported() {
    let text = "server {\n    root /a;\n    location / {\n        return 200;\n    }\n}\n";

    assert_eq!(walk(text), (1, 1));
}

#[test]
fn a_location_root_inherits_the_servers_rule() {
    let text = concat!(
        "server {\n",
        "    disable_symlinks if_not_owner;\n",
        "    location /x {\n",
        "        root /a;\n",
        "    }\n",
        "}\n",
    );

    assert_eq!(walk(text), (1, 0));
}

#[test]
fn a_location_that_turns_the_rule_off_is_reported() {
    let text = concat!(
        "server {\n",
        "    disable_symlinks if_not_owner;\n",
        "    location /x {\n",
        "        root /a;\n",
        "        disable_symlinks off;\n",
        "    }\n",
        "}\n",
    );

    assert_eq!(walk(text), (1, 1));
}

#[test]
fn a_rule_in_a_sibling_location_does_not_cover_a_root() {
    let text = concat!(
        "server {\n",
        "    location /x {\n",
        "        root /a;\n",
        "    }\n",
        "    location /y {\n",
        "        disable_symlinks if_not_owner;\n",
        "    }\n",
        "}\n",
    );

    assert_eq!(walk(text), (1, 1));
}

#[test]
fn a_rule_that_follows_links_or_exempts_a_prefix_is_not_accepted() {
    for rule in [
        "disable_symlinks off;",
        "disable_symlinks on;",
        "disable_symlinks if_not_owner from=$document_root;",
        "disable_symlinks on from=$document_root;",
    ] {
        let text = format!("server {{\n    root /a;\n    {rule}\n}}\n");

        assert_eq!(walk(&text), (1, 1), "{rule}");
    }
}

#[test]
fn a_commented_out_rule_does_not_cover_a_root() {
    let text = "server {\n    root /a;\n    # disable_symlinks if_not_owner;\n}\n";

    assert_eq!(walk(text), (1, 1));
}
