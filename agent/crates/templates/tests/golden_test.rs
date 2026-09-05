//! Byte-for-byte comparison of every rendered artifact against its golden.
//!
//! The golden diff is the review artifact for a template change: a reviewer
//! reads what the web server or php-fpm will actually be told, not a
//! template's intention (rules/testing.md).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::nftables::nftables_allow::NftablesAllow;
use maran_templates::nftables::nftables_bans_table::NftablesBansTable;
use maran_templates::nftables::nftables_protocol::NftablesProtocol;
use maran_templates::nftables::nftables_ruleset::NftablesRuleset;
use maran_templates::nftables::nftables_ssh_port::NftablesSshPort;
use maran_templates::nginx::php_site::PhpSite;
use maran_templates::nginx::proxy_site::ProxySite;
use maran_templates::nginx::site_body::SiteBody;
use maran_templates::nginx::ssl_block::SslBlock;
use maran_templates::nginx::static_site::StaticSite;
use maran_templates::nginx::suspended_site::SuspendedSite;
use maran_templates::php_fpm::pool::Pool;
use maran_templates::php_fpm::pool_override::PoolOverride;
use maran_templates::systemd::unit::MountUnit;

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

#[test]
fn a_jail_mount_unit_renders_its_golden() {
    let unit = MountUnit {
        account: "acme",
        source_directory: "/home/acme",
        mount_point: "/var/lib/maran/sftp/acme/home",
    };

    assert_eq!(
        unit.render_config().unwrap(),
        golden("systemd/mount_unit.mount")
    );
}

// Ruling 26 — a golden's parameter value must never equal what a
// literal-substitution mutant would render in its place.
//
// Replacing `{{ ssh_port }}` with a bare `22`, or `{{ panel_port }}` with
// `8443`, is the edit a developer actually makes. If the fixture ALSO says 22
// or 8443, the substitution renders identical bytes: the golden then pins the
// surrounding text and nothing whatever about the parameter. Both holes were
// live in this file and were found by mutation, not by reading — and they sat
// on the two values whose loss costs remote access to the host.
//
// So the fixtures below avoid the realistic default on purpose: `ssh_port` is
// 2222 rather than 22, and one ruleset golden carries a `panel_port` of 9443
// so 8443 is not the only value that line ever renders. The instinct to reach
// for "what a real host would show" is exactly the instinct that blinds the
// test. These are fixtures, not the installer's seed — the installer renders
// through the agent with detected values.

/// An allow open to every source, as the builder fills one: the two source
/// fields carry nothing because the template never reads them in that branch.
fn allow_from_anywhere(port: u16, protocol: NftablesProtocol) -> NftablesAllow {
    NftablesAllow {
        port,
        protocol,
        source_cidr: String::new(),
        source_is_any: true,
        family_keyword: "",
    }
}

#[test]
fn a_firewall_ruleset_renders_its_golden() {
    let ruleset = NftablesRuleset {
        // TWO ssh ports, neither with a rule of its own. sshd listens on every
        // `Port` directive and every `ListenAddress host:port`, so a host can
        // serve SSH on several at once — and the policy must carry a fallback
        // for EACH. With one port here, a template that rendered only the
        // first would be indistinguishable from a correct one.
        ssh_ports: vec![
            NftablesSshPort {
                port: 2222,
                rules: Vec::new(),
            },
            NftablesSshPort {
                port: 2022,
                rules: Vec::new(),
            },
        ],
        panel_port: 8443,
        allows: vec![
            allow_from_anywhere(80, NftablesProtocol::Tcp),
            allow_from_anywhere(443, NftablesProtocol::Tcp),
            NftablesAllow {
                port: 3306,
                protocol: NftablesProtocol::Tcp,
                source_cidr: "10.0.0.0/8".to_owned(),
                source_is_any: false,
                family_keyword: "ip",
            },
        ],
    };

    assert_eq!(
        ruleset.render_config().unwrap(),
        golden("nftables/ruleset.nft")
    );
}

#[test]
fn a_ruleset_with_a_restricted_ssh_rule_renders_its_golden() {
    let ruleset = NftablesRuleset {
        // Two ssh ports where only the FIRST has rules of its own. That pins
        // the property no single-port fixture can: an explicit rule for one
        // ssh port replaces THAT port's fallback and leaves the other port's
        // exactly where it was. A template that suppressed every fallback as
        // soon as any ssh rule existed would close 2022 on a host sshd is
        // listening on.
        //
        // Both of the first port's rules carry port 22 while its own port is
        // 2222, which is a state the builder does not produce on purpose: it
        // is what a regression in its routing would produce, and the golden
        // pins that the template still renders the PORT'S number and ignores
        // the rule's own. That is the fail-safe — a mis-routed rule opens SSH
        // rather than closing it — and without two different numbers here
        // nothing tells `{{ ssh.port }}` and `{{ rule.port }}` apart. The two
        // rules differ in address family for the same reason.
        ssh_ports: vec![
            NftablesSshPort {
                port: 2222,
                rules: vec![
                    NftablesAllow {
                        port: 22,
                        protocol: NftablesProtocol::Tcp,
                        source_cidr: "203.0.113.0/24".to_owned(),
                        source_is_any: false,
                        family_keyword: "ip",
                    },
                    NftablesAllow {
                        port: 22,
                        protocol: NftablesProtocol::Tcp,
                        source_cidr: "2001:db8:1::/48".to_owned(),
                        source_is_any: false,
                        family_keyword: "ip6",
                    },
                ],
            },
            NftablesSshPort {
                port: 2022,
                rules: Vec::new(),
            },
        ],
        panel_port: 8443,
        allows: vec![
            allow_from_anywhere(443, NftablesProtocol::Udp),
            NftablesAllow {
                port: 5432,
                protocol: NftablesProtocol::Tcp,
                source_cidr: "2001:db8::/32".to_owned(),
                source_is_any: false,
                family_keyword: "ip6",
            },
            // A source-restricted UDP allow. Without one, nothing distinguishes
            // the restricted branch's `{{ allow.protocol }}` from a literal
            // `tcp` — and that drift would leave the requested UDP port closed
            // while opening a TCP port nobody asked for, under `policy drop`.
            NftablesAllow {
                port: 51820,
                protocol: NftablesProtocol::Udp,
                source_cidr: "198.51.100.0/24".to_owned(),
                source_is_any: false,
                family_keyword: "ip",
            },
        ],
    };

    assert_eq!(
        ruleset.render_config().unwrap(),
        golden("nftables/ruleset_ssh_restricted.nft")
    );
}

#[test]
fn a_ruleset_with_an_any_source_ssh_rule_renders_its_golden() {
    let ruleset = NftablesRuleset {
        // 9443, not 8443: this is the only golden whose panel port differs, and
        // without a second value nothing tells `{{ panel_port }}` apart from a
        // hardcoded 8443 (Ruling 26). This golden has no panel-port claim of
        // its own to protect, so it is the cheapest place to carry the odd one.
        panel_port: 9443,
        // An admin's explicit "SSH from anywhere" rule. It renders a line that
        // reads like the accept-from-anywhere fallback, which is why it cannot
        // live in `ruleset_ssh_restricted.nft` — that golden exists to show the
        // fallback is GONE. Here the claim is the other one: an any-source ssh
        // rule renders at the CONFIGURED port. Hence `port: 22` against
        // the port's own 2222 — under the template the line reads 2222, and
        // nothing but two different numbers tells `{{ ssh.port }}` apart from
        // `{{ rule.port }}` in this branch. Getting it wrong costs remote
        // access to the host, which is what R2's fail-safe is for.
        ssh_ports: vec![NftablesSshPort {
            port: 2222,
            rules: vec![NftablesAllow {
                port: 22,
                protocol: NftablesProtocol::Tcp,
                source_cidr: String::new(),
                source_is_any: true,
                family_keyword: "",
            }],
        }],
        // Empty on purpose: no other golden renders the allow loop with zero
        // iterations, and a fresh host before its first allow looks exactly
        // like this.
        allows: Vec::new(),
    };

    assert_eq!(
        ruleset.render_config().unwrap(),
        golden("nftables/ruleset_ssh_any_source.nft")
    );
}

#[test]
fn the_bans_table_renders_its_golden() {
    let table = NftablesBansTable {};

    assert_eq!(
        table.render_config().unwrap(),
        golden("nftables/bans_table.nft")
    );
}
