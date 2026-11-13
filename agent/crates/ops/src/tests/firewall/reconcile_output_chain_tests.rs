//! The startup reconcile of the output chain.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::slice;

use maran_agent_core::utils::host_addresses_error::HostAddressesError;
use maran_templates::nftables::nftables_protocol::NftablesProtocol;

use crate::firewall::fake_firewall_host::{
    FakeFirewallHost, distro, restricted_rule, ruleset_path,
};
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::model::output_chain_outcome::OutputChainOutcome;
use crate::firewall::reconcile_output_chain::reconcile_output_chain;

const FIB_TRIE: &str = "/proc/net/fib_trie";

/// The ruleset a release without the output chain wrote: the current render cut
/// before the sets and closed again.
fn legacy_file(current: &str) -> String {
    let cut = current
        .find("\n    # Every address this host answers on")
        .unwrap();

    format!("{}\n}}\n", &current[..cut])
}

/// A host with no ruleset file has nothing to reconcile, and nothing is written.
#[test]
fn a_host_that_was_never_seeded_is_left_alone() {
    let host = FakeFirewallHost::new();

    assert_eq!(
        reconcile_output_chain(&host, distro()),
        Ok(OutputChainOutcome::NotSeeded)
    );
    assert!(host.applies().is_empty());
}

/// The review defect (R6): a host upgraded from a file without the chain gets the
/// chain at startup, with its rules untouched, and it is loaded.
#[test]
fn a_file_without_the_output_chain_is_rewritten_and_loaded_at_startup() {
    let rule = restricted_rule(3306, NftablesProtocol::Tcp, "10.0.0.0/8");
    let current = FakeFirewallHost::with_rules(slice::from_ref(&rule))
        .file(&ruleset_path())
        .unwrap();
    let host = FakeFirewallHost::new();
    host.put_file(&ruleset_path(), &legacy_file(&current));

    assert_eq!(
        reconcile_output_chain(&host, distro()),
        Ok(OutputChainOutcome::Rewritten)
    );

    let live = host.file(&ruleset_path()).unwrap();
    assert_eq!(
        live, current,
        "the rule is carried over, the chain is added"
    );
    assert!(live.contains("chain output"));
    assert_eq!(host.applies(), [ruleset_path()]);
}

/// Inverse control: a current file whose chain is loaded is neither written nor
/// loaded again.
#[test]
fn a_current_file_with_its_chain_loaded_is_left_alone() {
    let host = FakeFirewallHost::with_rules(&[]).with_rules_loaded();

    assert_eq!(
        reconcile_output_chain(&host, distro()),
        Ok(OutputChainOutcome::Current)
    );
    assert!(host.applies().is_empty());
}

/// A current file whose chain the kernel does not have (never loaded, flushed) is
/// loaded: the firewall reports healthy only when the chain is live.
#[test]
fn a_missing_live_chain_is_loaded_from_the_current_file() {
    let host = FakeFirewallHost::with_rules(&[]);

    assert_eq!(
        reconcile_output_chain(&host, distro()),
        Ok(OutputChainOutcome::Loaded)
    );
    assert_eq!(host.applies(), [ruleset_path()]);
    // Idempotent: the second run finds it live.
    assert_eq!(
        reconcile_output_chain(&host, distro()),
        Ok(OutputChainOutcome::Current)
    );
}

/// The addresses moved since the file was rendered: the next start brings the file
/// and the kernel up to date.
#[test]
fn a_changed_address_is_written_into_the_file_at_startup() {
    let host = FakeFirewallHost::with_rules(&[]).with_rules_loaded();
    host.put_file(
        FIB_TRIE,
        "Local:\n     |-- 203.0.113.7\n        /32 host LOCAL\n     |-- 127.0.0.1\n        /32 host LOCAL\n",
    );

    assert_eq!(
        reconcile_output_chain(&host, distro()),
        Ok(OutputChainOutcome::Rewritten)
    );
    assert!(
        host.file(&ruleset_path())
            .unwrap()
            .contains("elements = { 127.0.0.0/8, 203.0.113.7 }")
    );
}

/// A file this agent did not write is reported and never overwritten.
#[test]
fn a_foreign_file_is_refused_and_untouched() {
    let host = FakeFirewallHost::new();
    host.put_file(&ruleset_path(), "table inet other {}\n");

    assert_eq!(
        reconcile_output_chain(&host, distro()),
        Err(FirewallError::ForeignRuleset)
    );
    assert_eq!(
        host.file(&ruleset_path()).as_deref(),
        Some("table inet other {}\n")
    );
    assert!(host.applies().is_empty());
}

/// Host facts that cannot be read are an error, and the file is not rewritten
/// from a guess.
#[test]
fn unreadable_host_addresses_leave_the_file_as_it_was() {
    let current = FakeFirewallHost::with_rules(&[])
        .file(&ruleset_path())
        .unwrap();
    let host = FakeFirewallHost::new();
    host.put_file(&ruleset_path(), &legacy_file(&current));
    host.remove_file(FIB_TRIE);

    assert_eq!(
        reconcile_output_chain(&host, distro()),
        Err(FirewallError::HostAddressesUnreadable(
            HostAddressesError::Ipv4Missing
        ))
    );
    assert_eq!(host.file(&ruleset_path()).unwrap(), legacy_file(&current));
}
