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
use maran_templates::vsftpd::vsftpd_daemon_config::VsftpdDaemonConfig;

/// Reads a golden by the name of the render type that produces it.
fn golden(relative: &str) -> String {
    std::fs::read_to_string(format!("tests/golden/{relative}"))
        .unwrap_or_else(|error| panic!("golden {relative} is missing: {error}"))
}

/// The rendered locations of the PHP site both PHP goldens describe.
fn php_body() -> String {
    SiteBody {
        access_log: "/var/log/maran/sites/acme/example.com.access.log",
        error_log: "/var/log/maran/sites/acme/example.com.error.log",
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
        access_log: "/var/log/maran/sites/acme/static.example.access.log",
        error_log: "/var/log/maran/sites/acme/static.example.error.log",
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
        access_log: "/var/log/maran/sites/acme/app.example.access.log",
        error_log: "/var/log/maran/sites/acme/app.example.error.log",
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
        access_log: "/var/log/maran/sites/acme/gone.example.access.log",
        error_log: "/var/log/maran/sites/acme/gone.example.error.log",
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
        mount_point: "/var/lib/maran-sftp/acme/home",
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
        port_to: None,
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
                port_to: None,
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
                        port_to: None,
                        protocol: NftablesProtocol::Tcp,
                        source_cidr: "203.0.113.0/24".to_owned(),
                        source_is_any: false,
                        family_keyword: "ip",
                    },
                    NftablesAllow {
                        port: 22,
                        port_to: None,
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
                port_to: None,
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
                port_to: None,
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
                port_to: None,
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

/// One allow open to every source over a RANGE of ports.
fn range_from_anywhere(port: u16, port_to: u16, protocol: NftablesProtocol) -> NftablesAllow {
    NftablesAllow {
        port,
        port_to: Some(port_to),
        protocol,
        source_cidr: String::new(),
        source_is_any: true,
        family_keyword: "",
    }
}

#[test]
fn a_ruleset_with_port_ranges_renders_its_golden() {
    let ruleset = NftablesRuleset {
        // One ssh port with no rules of its own. Its block is a bare
        // `tcp dport 2222 accept` in this golden too, which is what pins that
        // the range syntax reached the ALLOW loop and not the ssh loop: an
        // upper bound rendered there would open a range nobody asked for on the
        // one port an operator cannot afford to be wrong about.
        ssh_ports: vec![NftablesSshPort {
            port: 2222,
            rules: Vec::new(),
        }],
        panel_port: 8443,
        allows: vec![
            // A range open to everyone. 41000-41099 and not the product's own
            // 30000-30099: a template that hardcoded the FTPS default would
            // render bytes identical to a golden built from it (Ruling 26).
            range_from_anywhere(41000, 41099, NftablesProtocol::Tcp),
            // A source-restricted range, and a UDP one. Both branches of the
            // allow loop render the bound, and without a restricted fixture a
            // template that appended the range only to the open branch would
            // pass — silently collapsing every scoped range to its first port.
            NftablesAllow {
                port: 52000,
                port_to: Some(52049),
                protocol: NftablesProtocol::Udp,
                source_cidr: "2001:db8::/32".to_owned(),
                source_is_any: false,
                family_keyword: "ip6",
            },
            // A single port AFTER the ranges. The absent bound must render
            // nothing at all — no separator, no repeated port — beside rules
            // that do carry one.
            allow_from_anywhere(8080, NftablesProtocol::Tcp),
        ],
    };

    assert_eq!(
        ruleset.render_config().unwrap(),
        golden("nftables/ruleset_port_range.nft")
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

// The FTPS daemon's configuration. Four goldens and not six: the TLS key
// spelling varies independently of the two axes the other goldens exist for
// (listen mode, passive address), so a per-family pair of all three would show
// the SAME two-line difference three times. The golden diff is the review
// artifact (rules/testing.md), and three copies of one diff teach a reviewer
// less than one — while each copy is another file that must move in lockstep
// for any unrelated template change.
//
// Ruling 26 applies to every number below. The product's own defaults are
// 30000-30099 and max_clients=100 (the panel's `FtpsDefaults`), so a template
// that hardcoded those in place of `{{ passive_port_min }}` would render bytes
// identical to a golden built from them, and the golden would pin nothing about
// the parameters. These fixtures deliberately carry other values.

/// The daemon configuration the three Debian-spelling goldens describe, with
/// only the axis under test varied by the caller.
fn daemon_config(
    ipv4_only: bool,
    passive_address: Option<String>,
    tls_v1: &'static str,
    tls_v1_1: &'static str,
    tls_v1_2: &'static str,
) -> VsftpdDaemonConfig {
    VsftpdDaemonConfig {
        certificate_path: "/etc/maran/certs/ftps/fullchain.pem".to_owned(),
        private_key_path: "/etc/maran/certs/ftps/privkey.pem".to_owned(),
        passive_port_min: 41000,
        passive_port_max: 41049,
        passive_address,
        max_clients: 37,
        log_path: "/var/log/maran/ftps/vsftpd.log".to_owned(),
        ipv4_only,
        tls_v1_key: tls_v1,
        tls_v1_1_key: tls_v1_1,
        tls_v1_2_key: tls_v1_2,
    }
}

/// The Debian family's spelling of the three TLS version options, as
/// `DistroAdapter::vsftpd_tls_version_keys` answers it there. Written out here
/// rather than imported: `maran-templates` does not depend on `maran-distro`,
/// and the point of this crate is that it never learns which family it renders
/// for.
fn debian_tls_keys() -> (&'static str, &'static str, &'static str) {
    ("ssl_tlsv1", "ssl_tlsv11", "ssl_tlsv12")
}

/// The RHEL family's spelling of the same three options.
fn rhel_tls_keys() -> (&'static str, &'static str, &'static str) {
    ("ssl_tlsv1", "ssl_tlsv1_1", "ssl_tlsv1_2")
}

/// The default dual-stack render, carrying the Debian family's TLS key
/// spelling — named so, because which family a golden's keys belong to is a
/// fact a reader must not have to infer.
#[test]
fn the_default_render_carries_the_debian_family_tls_keys() {
    let (v1, v1_1, v1_2) = debian_tls_keys();
    let config = daemon_config(false, None, v1, v1_1, v1_2);

    assert_eq!(
        config.render_config().unwrap(),
        golden("vsftpd/vsftpd.conf")
    );
}

/// A host behind NAT advertises a passive address the socket is not bound to.
#[test]
fn a_daemon_behind_nat_renders_its_advertised_passive_address() {
    let (v1, v1_1, v1_2) = debian_tls_keys();
    let config = daemon_config(false, Some("203.0.113.7".to_owned()), v1, v1_1, v1_2);

    assert_eq!(
        config.render_config().unwrap(),
        golden("vsftpd/vsftpd_passive_address.conf")
    );
}

/// The listen pair the enable path falls back to where the kernel refuses an
/// IPv6 bind. The two pairs are mutually exclusive, so this is a different
/// file and not a line added to the default one.
#[test]
fn a_host_with_ipv6_disabled_renders_the_ipv4_listen_pair() {
    let (v1, v1_1, v1_2) = debian_tls_keys();
    let config = daemon_config(true, None, v1, v1_1, v1_2);

    assert_eq!(
        config.render_config().unwrap(),
        golden("vsftpd/vsftpd_ipv4_only.conf")
    );
}

/// The RHEL family's render is the default one with two words changed, and the
/// test says WHICH two rather than counting them.
///
/// Two and not three: `ssl_tlsv1` is spelled the same on both families today,
/// and it is precisely the key that tempts a contributor to type it as a
/// literal. This is the test that catches the tidy-up — one family's spelling
/// "corrected" into the other's, or a key hardcoded into the template — because
/// either makes a line that must differ stop differing, or makes a line that
/// must not start.
#[test]
fn the_rhel_family_render_differs_only_in_its_tls_version_keys() {
    let (v1, v1_1, v1_2) = debian_tls_keys();
    let debian = daemon_config(false, None, v1, v1_1, v1_2)
        .render_config()
        .unwrap();
    let (v1, v1_1, v1_2) = rhel_tls_keys();
    let rhel = daemon_config(false, None, v1, v1_1, v1_2)
        .render_config()
        .unwrap();

    assert_eq!(rhel, golden("vsftpd/vsftpd_rhel_tls_keys.conf"));

    // Asserted BEFORE the zip below, which pairs lines positionally and would
    // silently ignore whatever a longer render carried past the end of the
    // shorter one — the blind spot that makes a difference count look sound.
    assert_eq!(
        debian.lines().count(),
        rhel.lines().count(),
        "the two families' renders have different numbers of lines"
    );

    let differences: Vec<(&str, &str)> = debian
        .lines()
        .zip(rhel.lines())
        .filter(|(debian_line, rhel_line)| debian_line != rhel_line)
        .collect();

    // The value, not a bound: these exact two pairs, in this order, and
    // nothing else. A count would pass on two lines that had drifted apart for
    // some other reason entirely.
    assert_eq!(
        differences,
        vec![
            ("ssl_tlsv12=YES", "ssl_tlsv1_2=YES"),
            ("ssl_tlsv11=NO", "ssl_tlsv1_1=NO"),
        ]
    );
}

/// A line appended to the rendered configuration is a KEY OF ITS OWN, not a
/// suffix of the last key already in it.
///
/// This is the test that would have caught the defect it was written for: the
/// render ended `require_ssl_reuse=NO` with no trailing newline, so appending
/// `force_local_logins_ssl=NO` produced
/// `require_ssl_reuse=NOforce_local_logins_ssl=NO` — ONE unrecognised variable,
/// on which the Debian family's vsftpd exits 2 printing nothing.
///
/// Written as an append-then-read rather than as an assertion about the final
/// BYTE, and the byte version was the alternative considered. The byte is
/// cheaper and exactly as sensitive, and the four goldens above already pin it
/// now that they carry the newline. What it does not carry is the REASON: a
/// reader who finds `assert_eq!(rendered.as_bytes().last(), Some(&b'\n'))`
/// failing learns that a byte moved, while this one names the consequence the
/// byte exists for, in the same words as the incident. Both fail on the same
/// change; only one of them explains itself.
#[test]
fn a_line_appended_to_the_rendered_configuration_is_a_key_of_its_own() {
    let (v1, v1_1, v1_2) = debian_tls_keys();
    let rendered = daemon_config(false, None, v1, v1_1, v1_2)
        .render_config()
        .unwrap();

    let appended = format!("{rendered}force_local_logins_ssl=NO\n");

    let last_two: Vec<&str> = appended.lines().rev().take(2).collect();
    assert_eq!(
        last_two,
        vec!["force_local_logins_ssl=NO", "require_ssl_reuse=NO"],
        "a line appended to the render must be its own key; without a trailing \
         newline it fuses with the last one into a single unrecognised variable"
    );
}

/// Each of the three TLS version keys is rendered from its own field, the one
/// spelled alike on both families included.
///
/// The goldens cannot see that last one. `ssl_tlsv1` is the same word on both
/// families today, so a contributor who typed it into the template as a literal
/// would render bytes identical to every golden here — a mutation this file
/// otherwise survives, and precisely the trap the adapter carries a third field
/// to avoid. Three implausible sentinel spellings make the question observable
/// without a fifth golden: if a key is a literal, its sentinel never appears.
#[test]
fn every_tls_version_key_is_rendered_from_its_own_field() {
    let config = daemon_config(
        false,
        None,
        "sentinel_tls_v1",
        "sentinel_tls_v1_1",
        "sentinel_tls_v1_2",
    );

    let rendered = config.render_config().unwrap();

    assert!(rendered.contains("\nsentinel_tls_v1_2=YES\n"), "{rendered}");
    assert!(rendered.contains("\nsentinel_tls_v1_1=NO\n"), "{rendered}");
    assert!(rendered.contains("\nsentinel_tls_v1=NO\n"), "{rendered}");
}

/// Every golden in the tree ends with a newline — the check a byte-exact
/// golden cannot perform on itself.
///
/// The four vsftpd goldens and `nginx/suspended_site.conf` were all written
/// without one, and every byte-exact test above passed on all five for as long
/// as they existed. That is not a gap in those tests, it is their shape: a
/// golden is generated FROM the render, so whatever the render's last byte is,
/// the golden agrees with it. The one property no such pair can observe is
/// whether that byte is right — which is why a template added tomorrow with the
/// same defect would arrive green.
///
/// What the missing byte costs is different for each family and neither cost is
/// cosmetic. For vsftpd, a line appended to the file fuses with the last key
/// into one unrecognised variable, on which the Debian family's daemon exits 2
/// printing nothing. For the suspended vhost, `ops::sites::is_suspended_vhost`
/// decides whether a site is suspended by comparing the file with a fresh
/// render byte for byte, so a file that gains the newline from anything at all
/// reads as not suspended — and the next renewal puts the suspended customer's
/// site back on the air.
///
/// The count guard is the vacuity guard, and it is on the axis that can go
/// blind: a walk that finds nothing passes loudest, and a `tests/golden` that
/// had moved or been renamed would produce exactly that. It is a lower bound
/// rather than an equality so that adding a golden does not edit this test —
/// the number only ever grows.
#[test]
fn every_golden_ends_with_a_newline() {
    /// Collects every file under `directory`, recursing into its subdirectories.
    fn collect(directory: &std::path::Path, into: &mut Vec<std::path::PathBuf>) {
        let entries = std::fs::read_dir(directory)
            .unwrap_or_else(|error| panic!("{} is unreadable: {error}", directory.display()));
        for entry in entries {
            let path = entry.expect("a directory entry is readable").path();
            if path.is_dir() {
                collect(&path, into);
            } else {
                into.push(path);
            }
        }
    }

    let mut goldens = Vec::new();
    collect(std::path::Path::new("tests/golden"), &mut goldens);
    goldens.sort();

    assert!(
        goldens.len() >= 16,
        "tests/golden holds {} files; this walk has gone blind",
        goldens.len()
    );

    let without: Vec<String> = goldens
        .iter()
        .filter(|path| {
            let bytes = std::fs::read(path).expect("a golden is readable");
            bytes.last() != Some(&b'\n')
        })
        .map(|path| path.display().to_string())
        .collect();

    assert_eq!(
        without,
        Vec::<String>::new(),
        "these renders do not end with a newline"
    );
}
