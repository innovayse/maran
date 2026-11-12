//! The refresh of the output chain when the host's addresses move.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_agent_core::utils::host_addresses_error::HostAddressesError;
use maran_agent_core::utils::ipv4_prefix::Ipv4Prefix;

use crate::firewall::fake_firewall_host::{FakeFirewallHost, distro, ruleset_path};
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::firewall_host::FirewallHost;
use crate::firewall::firewall_lock::firewall_lock;
use crate::firewall::model::self_sets_outcome::SelfSetsOutcome;
use crate::firewall::refresh_self_sets::refresh_self_sets;

const FIB_TRIE: &str = "/proc/net/fib_trie";
const IF_INET6: &str = "/proc/net/if_inet6";
const LOGIN_DEFS: &str = "/etc/login.defs";

/// Makes the fake kernel's address tables list `addresses` (and the loopback) and the
/// local `ranges`, and returns the same facts as the reading a caller would hold.
fn set_host(fake: &FakeFirewallHost, addresses: &[&str], ranges: &[(&str, u8)]) -> HostAddresses {
    let mut fib = String::from("Local:\n     |-- 127.0.0.1\n        /32 host LOCAL\n");
    let mut inet6 = String::new();
    let mut reading = HostAddresses::default();
    for text in addresses {
        let address: std::net::IpAddr = text.parse().unwrap();
        match address {
            std::net::IpAddr::V4(v4) => {
                fib.push_str(&format!("     |-- {v4}\n        /32 host LOCAL\n"));
            }
            std::net::IpAddr::V6(v6) => {
                inet6.push_str(&format!("{:032x} 02 40 00 80 eth0\n", u128::from(v6)));
            }
        }
        reading.addresses.push(address);
    }
    for (network, length) in ranges {
        fib.push_str(&format!(
            "     |-- {network}\n        /{length} universe LOCAL\n"
        ));
        reading
            .local_prefixes
            .push(Ipv4Prefix::new(network.parse().unwrap(), *length).unwrap());
    }
    fake.put_file(FIB_TRIE, &fib);
    fake.put_file(IF_INET6, &inet6);

    reading
}

/// A host with the firewall loaded and its ruleset file in place, rendered for a host
/// that carries the loopback only.
fn loaded_host() -> FakeFirewallHost {
    FakeFirewallHost::with_rules(&[]).with_rules_loaded()
}

/// The position of the last step of the fake's log that satisfies `wanted`.
fn last_step(fake: &FakeFirewallHost, wanted: impl Fn(&str) -> bool) -> Option<usize> {
    fake.steps().iter().rposition(|step| wanted(step))
}

/// The issue: an address gained after the last firewall change reaches the kernel
/// sets AND the ruleset file, with no rule reloaded.
#[test]
fn a_gained_address_reaches_both_sets_and_the_file_without_reloading_the_rules() {
    let fake = loaded_host();
    let reading = set_host(&fake, &["192.0.2.10", "2001:db8::5"], &[]);
    let file_before = fake.file(&ruleset_path()).unwrap();

    let outcome = refresh_self_sets(&fake, distro(), &reading);

    assert_eq!(outcome, Ok(SelfSetsOutcome::Refreshed));
    let scripts = fake.loaded_texts();
    assert_eq!(scripts.len(), 1);
    assert!(scripts[0].contains("add element inet maran self_v4 { 127.0.0.0/8, 192.0.2.10 }"));
    assert!(scripts[0].contains("add element inet maran self_v6 { ::1, 2001:db8::5 }"));
    let file = fake.file(&ruleset_path()).unwrap();
    assert_ne!(file, file_before, "the file follows the kernel");
    assert!(file.contains("elements = { 127.0.0.0/8, 192.0.2.10 }"));
    assert!(file.contains("elements = { ::1, 2001:db8::5 }"));
    assert!(
        !fake.applies().contains(&ruleset_path()),
        "the rules are not reloaded"
    );
}

/// D1, scenario B: a later reload of the ruleset FILE (`systemctl reload nftables`)
/// must bring back the current addresses. Applying the file the refresh left is exactly
/// that reload, and the sets it loads are the refreshed ones.
#[test]
fn reloading_the_file_after_a_refresh_does_not_revert_the_sets() {
    let fake = loaded_host();
    let reading = set_host(&fake, &["192.0.2.10"], &[]);
    refresh_self_sets(&fake, distro(), &reading).unwrap();

    let file = fake.file(&ruleset_path()).unwrap();
    let kernel_script = fake.loaded_texts().pop().unwrap();

    // Both name the same elements: the file a reload applies and the script the kernel got.
    assert!(file.contains("elements = { 127.0.0.0/8, 192.0.2.10 }"));
    assert!(kernel_script.contains("self_v4 { 127.0.0.0/8, 192.0.2.10 }"));
}

/// The file is committed before the kernel gets the sets, so a failure between the two
/// leaves a file ahead of the kernel and never a kernel a reload would revert.
#[test]
fn the_file_is_written_before_the_kernel_is_loaded() {
    let fake = loaded_host();
    let reading = set_host(&fake, &["192.0.2.10"], &[]);

    refresh_self_sets(&fake, distro(), &reading).unwrap();

    let commit = last_step(&fake, |step| step == "commit").unwrap();
    let load = last_step(&fake, |step| {
        step.contains(" -f ") && !step.contains("--check")
    })
    .unwrap();
    assert!(commit < load);
}

/// Only the two sets are in the kernel script: no table, chain or rule statement.
#[test]
fn the_script_replaces_the_two_sets_and_nothing_else() {
    let fake = loaded_host();
    let reading = set_host(&fake, &["192.0.2.10"], &[]);

    refresh_self_sets(&fake, distro(), &reading).unwrap();

    let script = &fake.loaded_texts()[0];
    let statements: Vec<&str> = script
        .lines()
        .filter(|line| !line.starts_with('#'))
        .collect();
    assert_eq!(statements.len(), 4);
    assert!(
        statements
            .iter()
            .all(|line| line.starts_with("flush set inet maran self_v")
                || line.starts_with("add element inet maran self_v"))
    );
    assert!(
        statements
            .iter()
            .all(|line| !line.contains("chain") && !line.contains("delete table"))
    );
}

/// A routed block reaches the set as a range.
#[test]
fn a_local_range_reaches_the_ipv4_set_as_a_range() {
    let fake = loaded_host();
    let reading = set_host(&fake, &["192.0.2.10"], &[("203.0.113.0", 24)]);

    refresh_self_sets(&fake, distro(), &reading).unwrap();

    assert!(fake.loaded_texts()[0].contains("self_v4 { 127.0.0.0/8, 203.0.113.0/24, 192.0.2.10 }"));
}

/// The script is checked before it is loaded, and the staged file does not outlive
/// the call.
#[test]
fn the_script_is_checked_then_loaded_and_the_staged_file_is_gone() {
    let fake = loaded_host();
    let reading = set_host(&fake, &["192.0.2.10"], &[]);

    refresh_self_sets(&fake, distro(), &reading).unwrap();

    let check = last_step(&fake, |step| step.contains("--check -f")).unwrap();
    let load = last_step(&fake, |step| {
        step.contains(" -f ") && !step.contains("--check")
    })
    .unwrap();
    assert!(check < load);
    assert_eq!(fake.steps().last().map(String::as_str), Some("discard"));
}

/// Inverse control: a host whose file already holds its addresses is not rewritten
/// (no commit), though the kernel is still brought to them (it may have been reverted).
#[test]
fn a_file_that_is_current_is_not_rewritten_but_the_kernel_is_still_loaded() {
    let fake = loaded_host();
    let reading = set_host(&fake, &["192.0.2.10"], &[]);
    refresh_self_sets(&fake, distro(), &reading).unwrap();
    let commits = fake.steps().iter().filter(|step| *step == "commit").count();

    let outcome = refresh_self_sets(&fake, distro(), &reading);

    assert_eq!(outcome, Ok(SelfSetsOutcome::Refreshed));
    assert_eq!(
        fake.steps().iter().filter(|step| *step == "commit").count(),
        commits
    );
    assert_eq!(fake.loaded_texts().len(), 2);
}

/// D1: a kernel without the set is reported as such, nothing is written, and the file
/// stays as it was. (Never "refreshed": the caller must ask again.)
#[test]
fn a_kernel_without_the_output_chain_is_not_written_to() {
    let fake = FakeFirewallHost::with_rules(&[]);
    let reading = set_host(&fake, &["192.0.2.10"], &[]);
    let file_before = fake.file(&ruleset_path()).unwrap();

    let outcome = refresh_self_sets(&fake, distro(), &reading);

    assert_eq!(outcome, Ok(SelfSetsOutcome::NotLoaded));
    assert!(fake.loaded_texts().is_empty());
    assert!(!fake.steps().iter().any(|step| step == "stage"));
    assert_eq!(fake.file(&ruleset_path()).unwrap(), file_before);
}

/// D1: `nft list` failing for any reason but the set's absence is an ERROR, not "nothing
/// to refresh". The inverse control is the test above, where the same failing exit with the
/// absent-object message is `NotLoaded`.
#[test]
fn a_transient_nft_failure_is_an_error_and_not_a_missing_set() {
    let fake = loaded_host();
    let reading = set_host(&fake, &["192.0.2.10"], &[]);
    fake.fail_listing_with("Error: Operation not permitted");

    let outcome = refresh_self_sets(&fake, distro(), &reading);

    assert!(matches!(outcome, Err(FirewallError::NftFailed { .. })));
    assert!(fake.loaded_texts().is_empty());
    assert!(!fake.steps().iter().any(|step| step == "commit"));
}

/// A host the installer never seeded has no file: nothing to follow, nothing written.
#[test]
fn a_host_without_a_ruleset_file_is_not_seeded() {
    let fake = FakeFirewallHost::new().with_rules_loaded();
    let reading = set_host(&fake, &["192.0.2.10"], &[]);

    assert_eq!(
        refresh_self_sets(&fake, distro(), &reading),
        Ok(SelfSetsOutcome::NotSeeded)
    );
    assert!(fake.loaded_texts().is_empty());
}

/// The review gap (inert chain): the chain was seeded when `login.defs` could not be read,
/// so its rule carries the default uid floor. Once the floor is readable (500) the watch
/// re-renders the chain, not only the sets: the whole ruleset is written and loaded.
#[test]
fn a_chain_seeded_with_the_wrong_uid_floor_is_repaired_whole() {
    let fake = loaded_host();
    fake.put_file(LOGIN_DEFS, "UID_MIN 500\n");
    let reading = set_host(&fake, &["192.0.2.10"], &[]);

    let outcome = refresh_self_sets(&fake, distro(), &reading);

    assert_eq!(outcome, Ok(SelfSetsOutcome::Repaired));
    let file = fake.file(&ruleset_path()).unwrap();
    assert!(file.contains("meta skuid >= 500 ip daddr @self_v4"));
    assert!(file.contains("elements = { 127.0.0.0/8, 192.0.2.10 }"));
    assert_eq!(
        fake.applies(),
        [ruleset_path()],
        "the rule itself was reloaded"
    );
}

/// Inverse control: with the right uid floor only the sets are stale, and that is a
/// refresh (no reload of the rules), not a repair.
#[test]
fn stale_sets_alone_are_a_refresh_and_not_a_repair() {
    let fake = loaded_host();
    let reading = set_host(&fake, &["192.0.2.10"], &[]);

    assert_eq!(
        refresh_self_sets(&fake, distro(), &reading),
        Ok(SelfSetsOutcome::Refreshed)
    );
    assert!(fake.applies().iter().all(|path| *path != ruleset_path()));
}

/// Facts that cannot be read write nothing: a chain filled from a guess would leave an
/// address open.
#[test]
fn unreadable_host_facts_write_nothing() {
    let fake = loaded_host();
    let reading = set_host(&fake, &["192.0.2.10"], &[]);
    fake.remove_file(FIB_TRIE);
    let file_before = fake.file(&ruleset_path()).unwrap();

    let outcome = refresh_self_sets(&fake, distro(), &reading);

    assert_eq!(
        outcome,
        Err(FirewallError::HostAddressesUnreadable(
            HostAddressesError::Ipv4Missing
        ))
    );
    assert!(fake.loaded_texts().is_empty());
    assert_eq!(fake.file(&ruleset_path()).unwrap(), file_before);
}

/// The host moved between the caller's reading and the lock: what is applied is the
/// fresh reading, and the answer says the caller's reading is not what is held.
#[test]
fn a_host_that_moved_since_the_callers_reading_is_applied_fresh_and_reported() {
    let fake = loaded_host();
    let stale = set_host(&fake, &["192.0.2.10"], &[]);
    set_host(&fake, &["192.0.2.11"], &[]);

    let outcome = refresh_self_sets(&fake, distro(), &stale);

    assert_eq!(outcome, Ok(SelfSetsOutcome::HostMoved));
    assert!(fake.file(&ruleset_path()).unwrap().contains("192.0.2.11"));
    assert!(!fake.file(&ruleset_path()).unwrap().contains("192.0.2.10"));
}

/// The facts are read INSIDE the lock: every reading of the kernel's address table the refresh makes is
/// made while the firewall lock is held, so a reading taken while another operation held the lock cannot be
/// the one applied. Observed from inside the host, with no thread and no sleep.
#[test]
fn the_facts_are_read_while_the_firewall_lock_is_held() {
    let fake = loaded_host();
    let reading = set_host(&fake, &["192.0.2.10"], &[]);

    let outcome = refresh_self_sets(&fake, distro(), &reading);

    assert_eq!(outcome, Ok(SelfSetsOutcome::Refreshed));
    let (under_the_lock, outside_it) = fake.address_reads();
    assert!(
        under_the_lock >= 1,
        "the refresh never read the address table"
    );
    assert_eq!(outside_it, 0, "the address table was read outside the lock");
}

/// The control of the observation: a read made while this thread holds the lock is counted as held, and a
/// read made with the lock free is counted as outside it, so the count above cannot be satisfied by a probe
/// that reports one answer for everything.
#[test]
fn the_probe_tells_a_read_under_the_lock_from_one_outside_it() {
    let fake = loaded_host();
    {
        let _guard = firewall_lock();
        let _ = fake.read_file(std::path::Path::new("/proc/net/fib_trie"));
    }
    assert_eq!(fake.address_reads().0, 1);
}

/// A refusal by `nft` of what is to be written is an error; the file and the kernel are
/// untouched and the staged file is removed.
#[test]
fn a_script_nft_refuses_is_an_error_and_loads_nothing() {
    let fake = loaded_host();
    let reading = set_host(&fake, &["192.0.2.10"], &[]);
    let file_before = fake.file(&ruleset_path()).unwrap();
    fake.refuse_check_with("Error: conflicting intervals");

    let outcome = refresh_self_sets(&fake, distro(), &reading);

    assert!(matches!(
        outcome,
        Err(FirewallError::RuleRefusedByNft { .. })
    ));
    assert!(fake.loaded_texts().is_empty());
    assert_eq!(fake.file(&ruleset_path()).unwrap(), file_before);
    assert_eq!(fake.steps().last().map(String::as_str), Some("discard"));
}

/// A load that fails is an error. The file was committed first, so it is ahead of the
/// kernel, and the next attempt finishes the job.
#[test]
fn a_load_that_fails_is_an_error_and_the_file_is_ahead_of_the_kernel() {
    let fake = loaded_host();
    let reading = set_host(&fake, &["192.0.2.10"], &[]);
    fake.refuse_load();

    let outcome = refresh_self_sets(&fake, distro(), &reading);

    assert!(matches!(outcome, Err(FirewallError::NftFailed { .. })));
    assert!(fake.file(&ruleset_path()).unwrap().contains("192.0.2.10"));
}

/// A file this agent did not write is reported and never overwritten.
#[test]
fn a_foreign_file_is_refused_and_untouched() {
    let fake = FakeFirewallHost::new().with_rules_loaded();
    fake.put_file(&ruleset_path(), "table inet other {}\n");
    let reading = set_host(&fake, &["192.0.2.10"], &[]);

    let outcome = refresh_self_sets(&fake, distro(), &reading);

    assert_eq!(outcome, Err(FirewallError::ForeignRuleset));
    assert_eq!(
        fake.file(&ruleset_path()).as_deref(),
        Some("table inet other {}\n")
    );
    assert!(fake.loaded_texts().is_empty());
}
