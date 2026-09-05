//! The host firewall against a real `nft` and a real kernel, which is the only
//! place `ops::firewall` means anything.
//!
//! Every claim this area makes is a claim about what a kernel does with a file,
//! and the central one is invisible to a fake host by construction: **`nft -f`
//! is ADDITIVE**. Re-applying a re-rendered ruleset with a rule removed leaves
//! that rule LIVE and duplicates everything else, unless the file deletes its
//! own table first. The area's first design did not, passed every fake-host
//! test, and reported a successful deny on a port that stayed open.
//!
//! What is settled here and nowhere else:
//!
//! - The ruleset the kernel HOLDS after an apply — its policy, its ordering,
//!   and the two unconditional accepts — rendered at runtime by the operation
//!   under test rather than replayed from a golden file. A golden proves the
//!   template's bytes; only this proves the builder still produces them.
//! - That a deny really removes: no rule for the port, and a rule count back
//!   at what it was before the allow.
//! - That the replace idiom is load-bearing, by reproducing the failure without
//!   it. This is the one assertion in the suite that goes red if somebody
//!   deletes those two lines believing them redundant.
//! - That an operator's own rule for the SSH port displaces the unconditional
//!   fallback, and that removing it brings the fallback back — R2's fail-open,
//!   observed rather than inferred from rendered bytes.
//! - A source-restricted UDP allow and an `ip6 saddr` allow, neither of which
//!   any golden covers.
//! - The bans chain's own ordering, and that a ban outlives a rules re-apply
//!   and expires on its own.
//!
//! What none of it settles, and what nothing automated can: **a ruleset that
//! locks an operator out passes every syntax check there is.** That is what the
//! two host ports on every mutation are for, and it is a threat note rather
//! than a test.
//!
//! These tests need `docker run --privileged` (or at least
//! `--cap-add NET_ADMIN`): without it `nft` cannot initialise its cache and
//! every apply fails. The fixture says so and fails, rather than letting the
//! failure read as a code defect.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/polygon_firewall.rs"]
mod polygon_firewall;

use std::net::{Ipv4Addr, TcpListener, TcpStream};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::{Duration, Instant};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::web::ban_address::BanAddress;
use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::source_cidr::SourceCidr;
use maran_distro::{DistroAdapter, DistroFamily, adapter_for, detect};
use maran_ops::firewall::{
    FirewallError, FirewallRule, NftablesProtocol, ProcessFirewallHost, RulesetPorts, RulesetState,
    allow_port, ban_address, deny_port, list_bans, list_rules, unban_address,
};

use polygon_firewall::{BANS_TABLE, PolygonFirewall, RULES_TABLE};

/// The SSH port every ruleset in this suite is rendered for.
const SSH_PORT: u16 = 22;

/// A SECOND port the host's sshd listens on, for the multi-port propositions.
///
/// Not a hypothetical shape: sshd listens on every `Port` directive and on
/// every `ListenAddress host:port`, across the main config and everything its
/// `Include` pulls in — which on the Debian family is exactly where a port
/// override is written.
const SECOND_SSH_PORT: u16 = 2022;

/// The panel port every ruleset in this suite is rendered for.
///
/// Different from [`SSH_PORT`] so the two unconditional accepts can never be
/// confused for one another in a listing.
const PANEL_PORT: u16 = 8443;

/// The unconditional SSH accept, as the kernel spells it back.
const SSH_FALLBACK: &str = "tcp dport 22 accept";

/// The loopback exemption, as the kernel spells it back.
const LOOPBACK: &str = "iif \"lo\" accept";

/// The port the deny regression is run against — the same one the review
/// measured `nft -f`'s additive behaviour with.
const REGRESSION_PORT: u16 = 3306;

/// How long a test waits for the kernel to collect an expired set element.
///
/// Expiry is not collection: measured on nftables v1.0.9, an element with a 5
/// second timeout is still listed at 6 seconds and gone by 9. The wait is a
/// DEADLINE polled to, not a sleep — a fixed sleep tuned to one observation is
/// how a suite becomes flaky on a loaded runner (rules/testing.md).
const EXPIRY_DEADLINE: Duration = Duration::from_secs(60);

/// Gap between two looks at a set while waiting for an element to go.
const EXPIRY_POLL: Duration = Duration::from_millis(500);

/// How long the loopback proposition waits for a connection to its own listener.
///
/// A connect over loopback either completes immediately or is dropped by a
/// filter; the timeout is what turns the second case into a failed assertion
/// rather than a hung test, and it is generous because a loaded runner is slow,
/// not because the connection is.
const LOOPBACK_CONNECT_TIMEOUT: Duration = Duration::from_secs(5);

/// The distribution adapter for the polygon this suite is running in.
///
/// # Panics
///
/// Panics when the host is outside the support matrix, which a polygon image
/// never is.
fn polygon_distro() -> &'static dyn DistroAdapter {
    adapter_for(
        detect()
            .expect("a polygon image is a supported host")
            .family,
    )
}

/// The host ports every mutation in this suite carries: one SSH port.
fn ports() -> RulesetPorts {
    ports_for(&[SSH_PORT])
}

/// The host ports for a host whose sshd listens on `ssh_ports`.
fn ports_for(ssh_ports: &[u16]) -> RulesetPorts {
    RulesetPorts {
        ssh_ports: ssh_ports
            .iter()
            .map(|port| Port::parse(u32::from(*port)).expect("a valid port"))
            .collect(),
        panel_port: Port::parse(u32::from(PANEL_PORT)).expect("a valid port"),
    }
}

/// One rule, built the way the service layer builds one.
fn rule(port: u16, protocol: NftablesProtocol, source: &str) -> FirewallRule {
    FirewallRule {
        port: Port::parse(u32::from(port)).expect("a valid port"),
        protocol,
        source: SourceCidr::parse(source).expect("a valid source network"),
    }
}

/// A rule open to every source.
fn open(port: u16, protocol: NftablesProtocol) -> FirewallRule {
    rule(port, protocol, "0.0.0.0/0")
}

/// Applies one allow through the operation under test.
///
/// # Panics
///
/// Panics when the operation refuses — including when `nft` refused the
/// rendered file, whose own message is carried in the error.
fn allow(rule: &FirewallRule) {
    allow_with(&ports(), rule);
}

/// Applies one allow for a host whose sshd listens on `ports`.
///
/// # Panics
///
/// Panics when the operation refuses.
fn allow_with(ports: &RulesetPorts, rule: &FirewallRule) {
    allow_port(&ProcessFirewallHost::new(), polygon_distro(), ports, rule)
        .unwrap_or_else(|error| panic!("allowing a port must succeed in the polygon: {error}"));
}

/// How many rule lines a listing holds.
///
/// Counted as the lines that end in a verdict, which is every rule and nothing
/// else: the chain's `type filter hook …` line, the braces and the table header
/// carry none.
fn rule_lines(listing: &str) -> usize {
    listing
        .lines()
        .map(str::trim)
        .filter(|line| line.ends_with("accept") || line.ends_with("drop"))
        .count()
}

#[test]
#[ignore = "loads a real nftables ruleset into the kernel: polygon only"]
fn the_ruleset_the_kernel_holds_after_an_apply_is_the_policy_the_plan_specifies() {
    let firewall = PolygonFirewall::start();

    // Rendered at RUNTIME by the operation under test and loaded by the real
    // `nft`. A golden file proves the template's bytes; this proves the builder
    // still produces them and that the kernel takes them.
    allow(&rule(REGRESSION_PORT, NftablesProtocol::Tcp, "10.0.0.0/8"));
    let listing = firewall.listing(RULES_TABLE);

    // The default. Everything else in this area is a hole punched in it, so a
    // ruleset that lost this line would report every rule correctly and protect
    // nothing.
    assert!(
        listing.contains("policy drop"),
        "the input chain must default to drop:\n{listing}"
    );

    // Loopback FIRST, asserted by position rather than by presence: the panel's
    // own web-server-to-application hop is loopback traffic, and a rule that
    // dropped before reaching it would sever the panel from itself. A `contains`
    // would pass with the line anywhere at all.
    let rules: Vec<&str> = listing
        .lines()
        .map(str::trim)
        .filter(|line| line.ends_with("accept") || line.ends_with("drop"))
        .collect();
    assert_eq!(
        rules.first().copied(),
        Some(LOOPBACK),
        "loopback must be accepted before anything is dropped:\n{listing}"
    );

    // The rest of R1's order, and the two unconditional accepts. The SSH one is
    // what stops an apply locking the operator out of the host; the panel one
    // has no override at all, because a panel lockout has no remote recovery.
    for expected in [
        "ct state invalid drop",
        "ct state established,related accept",
        "ip protocol icmp accept",
        // The template writes `meta l4proto 58`, and the KERNEL lists it back
        // by name. That difference is the point of asserting the listing
        // rather than the file: the number is what makes the ruleset loadable
        // on a host with no /etc/protocols, and this says the number still
        // means ICMPv6 once loaded. On a host that cannot resolve the name,
        // nft lists `58` and this assertion would be the wrong one — which is
        // why the images assert /etc/protocols exists at build time.
        "meta l4proto ipv6-icmp accept",
        SSH_FALLBACK,
        "tcp dport 8443 accept",
        "tcp dport 3306 ip saddr 10.0.0.0/8 accept",
    ] {
        assert!(
            listing.contains(expected),
            "the loaded ruleset must hold {expected:?}:\n{listing}"
        );
    }

    // And the agent reads its own store back as the one rule an operator added.
    // The two unconditional accepts are NOT rules: reporting them would show an
    // administrator a rule nobody created, and denying it would report success
    // while the template rendered it straight back.
    let recorded =
        list_rules(&ProcessFirewallHost::new(), &ports()).expect("the store must read back");
    assert_eq!(recorded.len(), 1);
    assert_eq!(recorded[0].port.value(), REGRESSION_PORT);
}

#[test]
#[ignore = "loads real rulesets and asserts what the kernel dropped: polygon only"]
fn a_denied_port_is_really_gone_from_the_kernel_and_nothing_is_duplicated() {
    let firewall = PolygonFirewall::start();
    let mysql = rule(REGRESSION_PORT, NftablesProtocol::Tcp, "10.0.0.0/8");

    // The seeded state: one allow, so the file exists and the table is loaded.
    allow(&open(80, NftablesProtocol::Tcp));
    let seeded = rule_lines(&firewall.listing(RULES_TABLE));

    allow(&mysql);
    let with_mysql = firewall.listing(RULES_TABLE);
    assert_eq!(
        PolygonFirewall::count(&with_mysql, "dport 3306"),
        1,
        "the allow must be live before the deny can mean anything:\n{with_mysql}"
    );
    assert_eq!(rule_lines(&with_mysql), seeded + 1);

    deny_port(
        &ProcessFirewallHost::new(),
        polygon_distro(),
        &ports(),
        &mysql,
    )
    .expect("denying a rule that exists must succeed");

    // THE regression. `nft -f` is additive: without the create-delete-redeclare
    // idiom in the rendered file, this listing would still hold the 3306 rule
    // and would hold two of everything else. The panel would have reported a
    // successful deny on a port that stayed open.
    let after = firewall.listing(RULES_TABLE);
    assert_eq!(
        PolygonFirewall::count(&after, "dport 3306"),
        0,
        "a denied port must be GONE from the kernel, not merely from the file:\n{after}"
    );
    assert_eq!(
        rule_lines(&after),
        seeded,
        "the ruleset must converge on what it was, with nothing duplicated:\n{after}"
    );
    assert_eq!(
        PolygonFirewall::count(&after, LOOPBACK),
        1,
        "a duplicated loopback line is the additive-apply signature:\n{after}"
    );

    // Denying it again converges rather than failing: a caller cannot tell a
    // lost response from a lost request, so it retries.
    let again = deny_port(
        &ProcessFirewallHost::new(),
        polygon_distro(),
        &ports(),
        &mysql,
    );
    assert!(
        matches!(again, Err(FirewallError::NotFound)),
        "a repeated deny must converge, got {again:?}"
    );
}

#[test]
#[ignore = "reproduces an additive apply against the real kernel: polygon only"]
fn without_the_replace_idiom_the_denied_rule_stays_live_and_everything_else_duplicates() {
    let firewall = PolygonFirewall::start();

    // A tripwire, and the only assertion in this suite that goes red if
    // somebody deletes the idiom's two lines believing them redundant. It
    // reproduces the failure ON PURPOSE, from the same templates the agent
    // uses, and asserts the numbers the review measured on nftables v1.0.9:
    // WITH the idiom 3306 rules: 0, loopback rules: 1; WITHOUT it 3306 rules: 1,
    // loopback rules: 2.
    let mysql = rule(REGRESSION_PORT, NftablesProtocol::Tcp, "10.0.0.0/8");
    let full = RulesetState::empty()
        .with(&mysql)
        .render(&ports())
        .expect("the ruleset must render");
    let without_mysql = RulesetState::empty()
        .render(&ports())
        .expect("the ruleset must render");

    let directory = std::env::temp_dir();
    let full_path = directory.join("maran-polygon-full.nft");
    let denied_path = directory.join("maran-polygon-denied.nft");
    let additive_path = directory.join("maran-polygon-additive.nft");
    std::fs::write(&full_path, &full).expect("a ruleset to apply");
    std::fs::write(&denied_path, &without_mysql).expect("a ruleset to apply");
    // The same file with ONLY the create-delete pair stripped out, exactly as
    // the review's measurement stripped it.
    std::fs::write(
        &additive_path,
        without_mysql
            .lines()
            .filter(|line| {
                let line = line.trim();
                line != "table inet maran {}" && line != "delete table inet maran"
            })
            .map(|line| format!("{line}\n"))
            .collect::<String>(),
    )
    .expect("a ruleset to apply");

    let apply = |path: &std::path::Path| {
        let applied = firewall.nft(&["-f", path.to_str().expect("a utf-8 path")]);
        assert!(
            applied.status.success(),
            "nft must accept {}: {}",
            path.display(),
            String::from_utf8_lossy(&applied.stderr)
        );
    };

    // WITH the idiom: apply, then apply the re-render that lost the rule.
    apply(&full_path);
    apply(&denied_path);
    let with_idiom = firewall.listing(RULES_TABLE);
    assert_eq!(PolygonFirewall::count(&with_idiom, "dport 3306"), 0);
    assert_eq!(PolygonFirewall::count(&with_idiom, LOOPBACK), 1);

    // WITHOUT it: the same two applies, from a file that only lost those two
    // lines. If this ever stops reproducing, `nft -f` has changed and the
    // area's central defence needs re-deriving rather than trusting.
    firewall.reset();
    apply(&full_path);
    apply(&additive_path);
    let without_idiom = firewall.listing(RULES_TABLE);
    assert_eq!(
        PolygonFirewall::count(&without_idiom, "dport 3306"),
        1,
        "without the idiom the removed rule must still be LIVE — that is the \
         defect the idiom exists for:\n{without_idiom}"
    );
    assert_eq!(
        PolygonFirewall::count(&without_idiom, LOOPBACK),
        2,
        "without the idiom every other rule must be duplicated:\n{without_idiom}"
    );

    for path in [&full_path, &denied_path, &additive_path] {
        let _ = std::fs::remove_file(path);
    }
}

#[test]
#[ignore = "loads real rulesets and reads the ssh rule back: polygon only"]
fn an_operator_rule_for_the_ssh_port_displaces_the_fallback_and_removing_it_brings_it_back() {
    let firewall = PolygonFirewall::start();
    let restricted = rule(SSH_PORT, NftablesProtocol::Tcp, "203.0.113.0/24");

    // Before: the fallback is what lets an operator in.
    allow(&open(80, NftablesProtocol::Tcp));
    assert_eq!(
        PolygonFirewall::count(&firewall.listing(RULES_TABLE), SSH_FALLBACK),
        1,
        "an untouched ruleset must accept SSH from anywhere"
    );

    // After: the operator's own rule renders INSTEAD. Asserted in the kernel's
    // listing rather than in the rendered file, because "the fallback is gone"
    // is a claim about what is loaded.
    allow(&restricted);
    let narrowed = firewall.listing(RULES_TABLE);
    assert!(
        narrowed.contains("tcp dport 22 ip saddr 203.0.113.0/24 accept"),
        "the operator's own ssh rule must be live:\n{narrowed}"
    );
    assert_eq!(
        PolygonFirewall::count(&narrowed, SSH_FALLBACK),
        0,
        "the unconditional fallback must be displaced, not kept beside it:\n{narrowed}"
    );

    // A UDP rule for the same NUMBER is an ordinary allow and never displaces
    // the fallback — a UDP rule taking its place would close the TCP port the
    // operator is connected on. Here the TCP rule already displaced it, so what
    // this pins is that the UDP rule is rendered as a UDP rule.
    //
    // It carries the SAME source as the restricted TCP rule above, deliberately:
    // the two then differ in protocol and in nothing else, which is what makes
    // the assertion after the deny a protocol assertion. With different sources
    // a deny that ignored the protocol entirely would still not have matched
    // this rule, and the check below could not have failed.
    let udp_twin = rule(SSH_PORT, NftablesProtocol::Udp, "203.0.113.0/24");
    allow(&udp_twin);
    let with_udp = firewall.listing(RULES_TABLE);
    assert!(
        with_udp.contains("udp dport 22 ip saddr 203.0.113.0/24 accept"),
        "a udp rule for the ssh port is an ordinary allow:\n{with_udp}"
    );

    // And removing the last TCP ssh rule returns the fallback: fail-open for
    // SSH, by design, because the alternative is a host nobody can reach.
    deny_port(
        &ProcessFirewallHost::new(),
        polygon_distro(),
        &ports(),
        &restricted,
    )
    .expect("denying the ssh rule must succeed");

    let restored = firewall.listing(RULES_TABLE);
    assert_eq!(
        PolygonFirewall::count(&restored, SSH_FALLBACK),
        1,
        "removing the last tcp ssh rule must bring the fallback back:\n{restored}"
    );
    // Asserted on `restored` — the listing taken AFTER the deny — and not on
    // `with_udp`, which was captured before it. That distinction is the whole
    // assertion: the two rules differ only in protocol, so a deny that matched
    // without comparing protocols takes them both, and a check that looked at
    // the earlier listing could not see it happen. It reads as a duplicate of
    // the check above and is the opposite: that one says the UDP rule arrived,
    // this one says the deny left it alone.
    assert_eq!(
        PolygonFirewall::count(&restored, "udp dport 22 ip saddr 203.0.113.0/24 accept"),
        1,
        "denying the tcp rule must not take the udp rule that differs from it \
         only in protocol:\n{restored}"
    );
    // And the store agrees with the kernel: the port 80 allow this test opened
    // with, and the udp twin. The restricted TCP rule is the only one gone.
    let remaining =
        list_rules(&ProcessFirewallHost::new(), &ports()).expect("the store must read back");
    assert_eq!(
        remaining,
        vec![open(80, NftablesProtocol::Tcp), udp_twin],
        "the deny must have taken the tcp rule and nothing else"
    );
}

#[test]
#[ignore = "loads real rulesets carrying a udp and an ipv6 restriction: polygon only"]
fn a_source_restricted_udp_allow_and_an_ipv6_allow_reach_the_kernel_as_written() {
    let firewall = PolygonFirewall::start();

    // Neither shape is covered by any golden, and both fail in a way a
    // rendered-bytes assertion would not catch. A source-restricted UDP allow
    // that rendered as TCP does two wrong things at once under a drop policy:
    // the UDP port the operator asked for stays closed, and a TCP port they
    // never asked for opens to the restricted source.
    allow(&rule(443, NftablesProtocol::Udp, "10.0.0.0/8"));
    // And an `ip6 saddr` rule had never been fed to a real kernel by any layer
    // of this project before this suite.
    allow(&rule(5432, NftablesProtocol::Tcp, "2001:db8::/32"));

    let listing = firewall.listing(RULES_TABLE);
    assert!(
        listing.contains("udp dport 443 ip saddr 10.0.0.0/8 accept"),
        "a source-restricted udp allow must be live as UDP:\n{listing}"
    );
    assert!(
        listing.contains("tcp dport 5432 ip6 saddr 2001:db8::/32 accept"),
        "an ipv6-restricted allow must be live with an ip6 saddr clause:\n{listing}"
    );

    // Both read back out of the agent's own store as what they are, so a panel
    // that lists them can deny them again by sending back what it was shown.
    let recorded =
        list_rules(&ProcessFirewallHost::new(), &ports()).expect("the store must read back");
    assert!(
        recorded
            .iter()
            .any(|held| held.port.value() == 443 && held.protocol == NftablesProtocol::Udp),
        "the udp rule must survive the round trip through the file: {recorded:?}"
    );
    assert!(
        recorded
            .iter()
            .any(|held| held.source.to_string() == "2001:db8::/32"),
        "the ipv6 source must survive the round trip through the file: {recorded:?}"
    );
}

#[test]
#[ignore = "adds a real ban to the real kernel and waits for it to expire: polygon only"]
fn a_ban_is_dropped_ahead_of_the_rules_survives_a_re_apply_and_expires_on_its_own() {
    let firewall = PolygonFirewall::start();
    let address = BanAddress::parse("198.51.100.7").expect("a valid address");

    ban_address(
        &ProcessFirewallHost::new(),
        polygon_distro(),
        &address,
        Some(Duration::from_secs(5)),
    )
    .expect("banning an address must succeed");

    // 1. The element is in the kernel's own set, not merely in a file.
    let listed = firewall.nft(&["-j", "list", "set", "inet", BANS_TABLE, "banned_v4"]);
    assert!(listed.status.success(), "the bans set must be readable");
    assert!(
        String::from_utf8_lossy(&listed.stdout).contains("198.51.100.7"),
        "the banned address must be an element of banned_v4"
    );

    // 2. The bans chain's OWN ordering. It hooks at a lower priority than the
    //    rules chain, so it runs FIRST — which means its loopback exemption has
    //    to come ahead of both set drops, or a ban on a loopback alias would
    //    sever the panel's own web-server-to-application hop before the rules
    //    chain's exemption was ever reached.
    let chain = firewall.listing(BANS_TABLE);
    let loopback = chain
        .find(LOOPBACK)
        .expect("the bans chain exempts loopback");
    let v4_drop = chain
        .find("ip saddr @banned_v4 drop")
        .expect("the bans chain drops banned v4 traffic");
    let v6_drop = chain
        .find("ip6 saddr @banned_v6 drop")
        .expect("the bans chain drops banned v6 traffic");
    assert!(
        loopback < v4_drop && loopback < v6_drop,
        "loopback must be accepted before either ban set is consulted:\n{chain}"
    );
    assert!(
        chain.contains("priority filter - 5") || chain.contains("priority -5"),
        "the bans chain must hook below the rules chain:\n{chain}"
    );

    // 3. The agent reads its own ban back, with the life it has left.
    let bans = list_bans(&ProcessFirewallHost::new(), polygon_distro())
        .expect("listing bans must succeed");
    assert_eq!(bans.len(), 1);
    assert_eq!(bans[0].address, address);
    assert!(
        bans[0].expires_in.is_some_and(|left| left.as_secs() <= 5),
        "a timed ban must report the life it has left: {:?}",
        bans[0].expires_in
    );

    // 4. A rules re-apply does not take it. This is the entire reason bans live
    //    in a second table: the rules table is DELETED and redeclared on every
    //    apply, so a ban kept in it would be erased by every rule change.
    allow(&open(80, NftablesProtocol::Tcp));
    let survivors = list_bans(&ProcessFirewallHost::new(), polygon_distro())
        .expect("listing bans must succeed");
    assert_eq!(
        survivors.len(),
        1,
        "a ban must survive a rules re-apply: {survivors:?}"
    );

    // 5. And it goes on its own, without anybody unbanning it. Polled to a
    //    deadline rather than slept on: expiry is not collection, and an
    //    element with a 5 second timeout was measured still listed at 6 seconds
    //    on this nft.
    let deadline = Instant::now() + EXPIRY_DEADLINE;
    let mut remaining = usize::MAX;
    while Instant::now() < deadline {
        remaining = list_bans(&ProcessFirewallHost::new(), polygon_distro())
            .expect("listing bans must succeed")
            .len();
        if remaining == 0 {
            break;
        }

        std::thread::sleep(EXPIRY_POLL);
    }
    assert_eq!(
        remaining, 0,
        "a ban with a timeout must expire without anybody lifting it"
    );

    // 6. Unbanning one that is no longer there converges rather than failing.
    let again = unban_address(&ProcessFirewallHost::new(), polygon_distro(), &address);
    assert!(
        matches!(again, Err(FirewallError::NotFound)),
        "a repeated unban must converge, got {again:?}"
    );
}

#[test]
#[ignore = "installs a loopback ban element by hand and sends real loopback traffic: polygon only"]
fn a_loopback_ban_is_refused_by_the_agent_and_one_placed_by_hand_blocks_nothing() {
    let firewall = PolygonFirewall::start();

    // 1. The agent will not place it. This is the fix, at the only gate that
    //    survives both a panel that asked wrongly and a caller that asked
    //    wrongly, and the whole 127.0.0.0/8 block is refused rather than the
    //    one address, because `iif "lo"` exempts the interface.
    for candidate in ["127.0.0.1", "127.0.0.53", "::1"] {
        assert!(
            BanAddress::parse(candidate).is_err(),
            "{candidate} must be refused as a ban target"
        );
    }

    // 2. The inverse control, on this same real host: an ordinary address is
    //    still banned, so the refusal above is a refusal of loopback and not of
    //    everything. Banning it also loads the bans table, which step 3 needs.
    let ordinary = BanAddress::parse("198.51.100.9").expect("an ordinary address is bannable");
    ban_address(
        &ProcessFirewallHost::new(),
        polygon_distro(),
        &ordinary,
        None,
    )
    .expect("banning an ordinary address must succeed");

    // 3. WHY it is refused, observed rather than inferred from the template: a
    //    listener on loopback is still reachable with the ban element the old
    //    code would have installed sitting in the set. The element is added by
    //    hand here precisely because the agent no longer will.
    let listener = TcpListener::bind((Ipv4Addr::LOCALHOST, 0)).expect("a loopback listener");
    let listening = listener.local_addr().expect("the listener has an address");

    let added = firewall.nft(&[
        "add",
        "element",
        "inet",
        BANS_TABLE,
        "banned_v4",
        "{",
        "127.0.0.1",
        "}",
    ]);
    assert!(
        added.status.success(),
        "the element must be installable by hand: {}",
        String::from_utf8_lossy(&added.stderr)
    );

    let chain = firewall.listing(BANS_TABLE);
    assert!(
        chain.contains("127.0.0.1"),
        "the loopback element must really be in the set:\n{chain}"
    );

    let connected = TcpStream::connect_timeout(&listening, LOOPBACK_CONNECT_TIMEOUT);
    assert!(
        connected.is_ok(),
        "a ban on 127.0.0.1 blocks nothing — loopback is accepted before either \
         ban set is consulted, which is the whole reason the agent refuses to \
         place one: {connected:?}"
    );

    // 4. And such a leftover stays readable and liftable, which is why the read
    //    and unban paths take `parse_existing` rather than `parse`. A ban that
    //    could not be listed would make the WHOLE list unreadable, and one that
    //    could not be lifted would be permanent.
    let bans = list_bans(&ProcessFirewallHost::new(), polygon_distro())
        .expect("a loopback leftover must not make the ban list unreadable");
    let leftover = bans
        .iter()
        .find(|ban| ban.address.to_string() == "127.0.0.1")
        .expect("the hand-placed element must be listed");

    unban_address(
        &ProcessFirewallHost::new(),
        polygon_distro(),
        &leftover.address,
    )
    .expect("a loopback leftover must be liftable");

    let after = list_bans(&ProcessFirewallHost::new(), polygon_distro())
        .expect("listing bans must succeed");
    assert!(
        !after
            .iter()
            .any(|ban| ban.address.to_string() == "127.0.0.1"),
        "the leftover must be gone: {after:?}"
    );

    unban_address(&ProcessFirewallHost::new(), polygon_distro(), &ordinary)
        .expect("the ordinary ban must be liftable too");
}

#[test]
#[ignore = "reads back a ruleset this agent did not write: polygon only"]
fn a_ruleset_file_this_agent_did_not_write_is_refused_and_nothing_is_overwritten() {
    let firewall = PolygonFirewall::start();

    // An operator's own ruleset, at the path the agent uses. It is syntactically
    // fine and `nft` would take it — which is the point: the refusal is about
    // provenance, not validity. Replacing it with a policy inferred from it is
    // how a host loses the rules it was actually running.
    let foreign = "table inet maran {\n    chain input {\n        type filter hook input priority 0; policy accept;\n    }\n}\n";
    std::fs::write(AgentPaths::nftables_ruleset_path(), foreign)
        .expect("the agent's ruleset directory must be writable");

    let refused = allow_port(
        &ProcessFirewallHost::new(),
        polygon_distro(),
        &ports(),
        &open(80, NftablesProtocol::Tcp),
    );
    assert!(
        matches!(refused, Err(FirewallError::ForeignRuleset)),
        "a ruleset this agent did not render must be refused, got {refused:?}"
    );

    // And nothing was overwritten: the operator's file is byte-for-byte what it
    // was. An assertion on the error alone would pass even if the agent had
    // refused AFTER replacing the file.
    assert_eq!(
        std::fs::read_to_string(AgentPaths::nftables_ruleset_path())
            .expect("the file must still be there"),
        foreign
    );

    drop(firewall);
}

#[test]
#[ignore = "loads a real ruleset for a host with two ssh ports: polygon only"]
fn every_ssh_port_keeps_its_own_fallback_and_a_rule_for_one_does_not_disturb_the_other() {
    let firewall = PolygonFirewall::start();
    let ports = ports_for(&[SSH_PORT, SECOND_SSH_PORT]);
    let second_fallback = format!("tcp dport {SECOND_SSH_PORT} accept");

    // A host can legitimately serve SSH on several ports at once — sshd
    // listens on every `Port` directive and every `ListenAddress host:port`,
    // across the main config and its includes. Sending one of them would open
    // that one and CLOSE the rest, and which one it was would depend on line
    // order in a config file. No unit test reaches this: only a real kernel
    // can be asked which ports are actually accepted.
    allow_with(&ports, &open(80, NftablesProtocol::Tcp));

    let both = firewall.listing(RULES_TABLE);
    assert_eq!(
        PolygonFirewall::count(&both, SSH_FALLBACK),
        1,
        "the first ssh port must be accepted:\n{both}"
    );
    assert_eq!(
        PolygonFirewall::count(&both, &second_fallback),
        1,
        "and so must the second — one port on the wire would have closed it:\n{both}"
    );

    // Now restrict ONE of them. The restricted port loses its unconditional
    // accept, which is R2's displacement — and the other port must not notice.
    // A template that suppressed every fallback as soon as any ssh rule
    // existed would close a port sshd is listening on, which is the lockout
    // this whole list exists to prevent.
    let restricted = rule(SSH_PORT, NftablesProtocol::Tcp, "203.0.113.0/24");
    allow_with(&ports, &restricted);

    let narrowed = firewall.listing(RULES_TABLE);
    assert!(
        narrowed.contains("tcp dport 22 ip saddr 203.0.113.0/24 accept"),
        "the operator's rule for the first ssh port must be live:\n{narrowed}"
    );
    assert_eq!(
        PolygonFirewall::count(&narrowed, SSH_FALLBACK),
        0,
        "the restricted port's own fallback must be displaced:\n{narrowed}"
    );
    assert_eq!(
        PolygonFirewall::count(&narrowed, &second_fallback),
        1,
        "THE assertion: restricting one ssh port must leave the other's \
         fallback exactly where it was:\n{narrowed}"
    );

    // And removing that rule returns the first port's fallback, still without
    // disturbing the second.
    deny_port(
        &ProcessFirewallHost::new(),
        polygon_distro(),
        &ports,
        &restricted,
    )
    .expect("denying the ssh rule must succeed");

    let restored = firewall.listing(RULES_TABLE);
    assert_eq!(PolygonFirewall::count(&restored, SSH_FALLBACK), 1);
    assert_eq!(PolygonFirewall::count(&restored, &second_fallback), 1);

    // The listing reads back exactly the one rule an operator added. Neither
    // fallback is a rule, and neither is the panel accept — with two ssh ports
    // the second port's fallback sits exactly where the panel accept used to,
    // so a parser that had not been told the ports would report the panel port
    // here as a rule nobody created.
    let recorded =
        list_rules(&ProcessFirewallHost::new(), &ports).expect("the store must read back");
    assert_eq!(
        recorded.len(),
        1,
        "expected only the port 80 allow: {recorded:?}"
    );
    assert_eq!(recorded[0].port.value(), 80);
}

/// The installer's include target, restored when the test ends however it ends.
///
/// The step writes the file a BOOT reads, so the test drives it against the real
/// path rather than a stand-in — that is the file whose parse decides whether a
/// host comes up with a firewall. Putting it back is what stops one test's
/// wiring being what a later one is really looking at (rules/testing.md, no
/// shared mutable fixtures).
struct IncludeTarget {
    /// The path the installer chose for this family.
    path: PathBuf,
    /// What was in it before, or `None` when the step created it.
    original: Option<String>,
}

impl Drop for IncludeTarget {
    /// Puts the file back, whether the test passed or panicked.
    fn drop(&mut self) {
        // A panic in `drop` during another panic aborts the process and hides
        // the real failure, so nothing here may assert.
        let restored = match &self.original {
            Some(text) => std::fs::write(&self.path, text),
            None => std::fs::remove_file(&self.path),
        };
        if let Err(error) = restored {
            eprintln!(
                "the polygon include target {} could not be restored: {error}",
                self.path.display()
            );
        }
    }
}

/// Runs `script` with `installer/lib/87-firewall.sh` sourced.
///
/// The step is SOURCED and its functions called, rather than `step_firewall`
/// being run whole: that function ends at a gate asking the service manager
/// whether `table inet maran` is loaded, and the polygon's `systemctl` stand-in
/// starts no unit, so it correctly fails in a container. What this test is for
/// is the two functions before it.
///
/// `MARAN_OS_FAMILY` is exported because the step reads it to choose the include
/// target. `pkg_install` is NOT stubbed and does not need to be: it is called
/// only from `step_firewall`, which nothing here invokes.
fn installer_step(script: &str) -> std::process::Output {
    let family = match polygon_distro().family() {
        DistroFamily::Debian => "debian",
        DistroFamily::Rhel => "rhel",
    };
    // From the crate rather than from a mount point: the suite must not care
    // where the repository is mounted in the container.
    let step = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../../installer/lib/87-firewall.sh")
        .canonicalize()
        .expect("the installer's firewall step must be readable from the agent crate");

    Command::new("bash")
        .arg("-c")
        .arg(format!(
            "set -euo pipefail\n\
             export MARAN_OS_FAMILY={family}\n\
             . {step}\n\
             {script}",
            step = step.display(),
        ))
        .output()
        .expect("the polygon image installs bash")
}

/// Everything a run of the installer step said, both streams.
fn said(outcome: &std::process::Output) -> String {
    format!(
        "{}{}",
        String::from_utf8_lossy(&outcome.stdout),
        String::from_utf8_lossy(&outcome.stderr)
    )
}

#[test]
#[ignore = "runs the installer's own firewall step against a real kernel: polygon only"]
fn the_installers_own_seeded_files_and_include_target_pass_nfts_check_and_load() {
    let firewall = PolygonFirewall::start();

    // WHY this test exists, and why it is here rather than in the image build.
    //
    // `87-firewall.sh` renders its candidate and asks `nft -c -f` about it
    // before writing. In the image BUILD that check cannot run: measured on this
    // image, a container with no added capabilities answers `rc 1,
    // netlink: Error: cache initialization failed: Operation not permitted` for
    // a VALID file. So the step tolerates a capability error and continues,
    // leaving its service gate as the backstop — a real narrowing, and this is
    // where the check goes back, because the polygon's firewall step runs
    // `--privileged` and `PolygonFirewall::start()` has already proved above
    // that the kernel is reachable.
    //
    // The neighbouring test proves the AGENT's own render loads. This one
    // proves the INSTALLER's does — the same templates reached through the
    // shell path a real host takes, which is the half nothing else covers.

    // The agent binary at the absolute path the step executes. Found from this
    // test's own location so it works under any CARGO_TARGET_DIR.
    let built = std::env::current_exe()
        .ok()
        .and_then(|exe| {
            exe.parent()?
                .parent()
                .map(|debug| debug.join("maran-agent"))
        })
        .filter(|path| path.exists())
        .expect("cargo test builds the agent binary beside the test binaries");
    let installed = Path::new("/usr/local/maran/agent/maran-agent");
    std::fs::create_dir_all(installed.parent().expect("the agent path has a parent"))
        .expect("the agent directory must be creatable");
    let _ = std::fs::remove_file(installed);
    std::fs::copy(&built, installed).expect("the agent binary must be installable");

    // The two host facts 60-config.sh detects, as it writes them. TWO ssh ports,
    // because that is the shape a single-port fixture cannot check: the step has
    // to turn the comma-separated list into one `--ssh-port` flag per port, and
    // a seed that dropped one would close a port sshd is listening on.
    std::fs::write(
        "/etc/maran/panel.env",
        "Firewall__SshPorts=22,2222\nFirewall__PanelPort=8443\n",
    )
    .expect("/etc/maran must be writable");

    let target = PathBuf::from(
        String::from_utf8_lossy(&installer_step("nftables_include_target").stdout)
            .trim()
            .to_owned(),
    );
    assert!(
        target.is_absolute(),
        "the step must name an absolute include target, got {target:?}"
    );
    let _restore = IncludeTarget {
        original: std::fs::read_to_string(&target).ok(),
        path: target.clone(),
    };

    // 1. The step's own two functions, run as the installer runs them.
    let seeded = installer_step("seed_firewall_files\nwire_firewall_includes");
    assert!(
        seeded.status.success(),
        "the installer's firewall step must seed and wire in the polygon:\n{}",
        said(&seeded)
    );

    // 2. The step's OWN check ran for real, which is the thing the image build
    //    cannot have. `wire_firewall_includes` prints a note and continues when
    //    `nft` answers "Operation not permitted", so on an unprivileged host a
    //    green step means only that the check was skipped. Its ABSENCE here is
    //    what says the candidate was really parsed before it was moved into
    //    place — assert it, or this whole test could pass in a container where
    //    nothing was ever checked.
    assert!(
        !said(&seeded).contains("cannot reach the kernel"),
        "the step must have really checked its candidate here, not skipped the \
         check for want of a capability:\n{}",
        said(&seeded)
    );

    //    And the same question asked independently. Under `--privileged` this is
    //    `nft` really parsing the file a boot will read, includes resolved —
    //    not the "could not reach the kernel" note the build has to accept.
    let checked = firewall.nft(&["-c", "-f", target.to_str().expect("a utf-8 path")]);
    assert!(
        checked.status.success(),
        "the include target the installer wrote must pass nft's own check:\n{}",
        String::from_utf8_lossy(&checked.stderr)
    );

    // 3. Loaded by hand, standing in for the init this container does not have.
    //    `step_firewall` would have `systemctl enable --now nftables` here and
    //    then asked the kernel whether the table arrived; the polygon's
    //    `systemctl` stand-in starts no unit, so that gate cannot pass in a
    //    container and this does its job explicitly instead. Without this the
    //    test would prove the file parses and never that it WORKS.
    let loaded = firewall.nft(&["-f", target.to_str().expect("a utf-8 path")]);
    assert!(
        loaded.status.success(),
        "the include target must load into the kernel:\n{}",
        String::from_utf8_lossy(&loaded.stderr)
    );

    // 4. And what arrived is the policy, for BOTH ssh ports. The bans table too:
    //    the step seeds it first precisely so the include naming it resolves,
    //    and an include whose target is missing aborts the whole load.
    let listing = firewall.listing(RULES_TABLE);
    assert!(
        listing.contains("policy drop"),
        "the seeded ruleset must default to drop:\n{listing}"
    );
    for fallback in [
        SSH_FALLBACK,
        "tcp dport 2222 accept",
        "tcp dport 8443 accept",
    ] {
        assert_eq!(
            PolygonFirewall::count(&listing, fallback),
            1,
            "the seed must accept {fallback:?} — one --ssh-port flag per port in \
             Firewall__SshPorts, and the panel's own:\n{listing}"
        );
    }
    let bans = firewall.listing(BANS_TABLE);
    assert!(
        bans.contains("ip saddr @banned_v4 drop"),
        "the bans table must be loaded by the same include block:\n{bans}"
    );

    // 5. And the agent reads its own seed back: the two web ports the seed opens
    //    are rules, and neither ssh fallback nor the panel accept is. A seed the
    //    agent could not parse would be a host whose first firewall change fails.
    let recorded = list_rules(&ProcessFirewallHost::new(), &ports_for(&[22, 2222]))
        .expect("the agent must read back the ruleset its own installer seeded");
    let ports: Vec<u16> = recorded.iter().map(|rule| rule.port.value()).collect();
    assert_eq!(
        ports,
        vec![80, 443],
        "expected only the seeded web ports: {recorded:?}"
    );
}
