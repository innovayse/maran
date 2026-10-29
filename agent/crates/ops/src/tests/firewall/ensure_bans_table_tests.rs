//! The table bans live in is loaded once, and two callers cannot load it
//! twice.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Arc;
use std::thread;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::call_class::CallClass;

use crate::firewall::ensure_bans_table::{BANNED_V4_SET, ensure_bans_table};
use crate::firewall::fake_firewall_host::{FakeFirewallHost, bans_path, distro};
use crate::firewall::firewall_error::FirewallError;

/// A host with no bans table gets one.
#[test]
fn a_host_with_no_bans_table_gets_one() {
    let host = FakeFirewallHost::new();

    ensure_bans_table(&host, distro()).expect("ensured");

    assert_eq!(host.applies(), vec![bans_path()]);
}

/// A table that is already loaded is left completely alone.
///
/// The bans file carries the same create-delete-redeclare idiom the ruleset
/// does, so **re-applying it erases every ban in force** — measured on
/// nftables v1.0.9 and modelled by the fake, whose load of that path clears
/// its element list. The element in this test is the whole point: an
/// implementation that re-applied "just to be sure" would pass an assertion
/// about the table existing afterwards, and would silently have released the
/// address the panel believes is blocked.
#[test]
fn ensure_bans_table_never_reapplies_over_an_existing_table() {
    let host = FakeFirewallHost::new().with_bans_table().with_element(
        BANNED_V4_SET,
        "198.51.100.7",
        Some(3600),
    );

    ensure_bans_table(&host, distro()).expect("ensured");

    assert!(
        host.applies().is_empty(),
        "the bans file must not be applied over a table that is already there"
    );
    assert_eq!(
        host.elements().len(),
        1,
        "the ban in force must survive: {:?}",
        host.elements()
    );
}

/// A bans file `nft` refuses is reported with its own answer, and no table is
/// loaded.
#[test]
fn a_bans_table_nft_refuses_is_reported() {
    let host = FakeFirewallHost::new();
    host.refuse_check_with("Error: syntax error");

    let outcome = ensure_bans_table(&host, distro());

    assert_eq!(
        outcome,
        Err(FirewallError::RuleRefusedByNft {
            stderr: String::from("Error: syntax error"),
        })
    );
    assert!(host.applies().is_empty());
}

/// Two callers racing an absent bans table load it exactly once.
///
/// Without the module lock both find the table absent, both apply the file,
/// and the second apply erases the ban the first one was there to install —
/// while the panel records both. The fake makes the race REPRODUCIBLE rather
/// than leaving it to the scheduler: the first caller to reach the table
/// check waits for a second one, so a build without the lock interleaves
/// every time, and a build with it never does (the second caller is parked on
/// the lock, the wait times out, and the assertion below is what tells the
/// two apart).
#[test]
fn concurrent_mutations_serialise_on_the_module_lock() {
    let host = Arc::new(FakeFirewallHost::new().with_arrival_gate());

    let racers: Vec<_> = (0..2)
        .map(|_| {
            let host = Arc::clone(&host);

            thread::spawn(move || ensure_bans_table(host.as_ref(), distro()))
        })
        .collect();
    for racer in racers {
        racer.join().expect("the thread finished").expect("ensured");
    }

    assert_eq!(
        host.applies(),
        vec![bans_path()],
        "two concurrent first-bans must load the bans table exactly once"
    );
}

/// A table listing killed at its deadline loads NOTHING.
///
/// Reading the timeout as "the table is not there" would apply the bans file,
/// and applying it over a loaded table erases every ban in force. The ban in
/// this test is the thing that must survive.
#[test]
fn a_table_listing_that_times_out_never_reapplies_the_bans_file() {
    let host = FakeFirewallHost::new().with_bans_table().with_element(
        BANNED_V4_SET,
        "198.51.100.7",
        Some(3600),
    );
    host.time_out_on("list");

    let outcome = ensure_bans_table(&host, distro());

    assert!(
        matches!(
            outcome,
            Err(FirewallError::Bound(BoundExceeded::TimedOut { .. }))
        ),
        "expected the typed timeout, got {outcome:?}"
    );
    assert!(host.applies().is_empty());
    assert_eq!(host.elements().len(), 1);
}

/// The table question is a query behind an end-of-options marker, and the
/// operands are still all there after it.
#[test]
fn the_table_listing_is_a_guarded_query() {
    let host = FakeFirewallHost::new().with_bans_table();

    ensure_bans_table(&host, distro()).expect("ensured");

    let argv = host.nft_call_starting_with("list").expect("a listing");
    assert_eq!(argv.get(1).map(String::as_str), Some("--"));
    assert_eq!(argv.get(2).map(String::as_str), Some("list"));
    assert_eq!(argv.last().map(String::as_str), Some("input"));
    assert_eq!(host.class_of_call("list"), Some(CallClass::Query));
}

/// The existence probe lists the table's chain and not the table.
///
/// `nft list table` prints every ban element, so its output grows with the
/// ban count and a large set could exceed the stdout cap just to answer
/// "is it loaded". A chain listing is the same few lines at any size. This
/// pins the argv, not `nft`'s behaviour: that a chain listing stays small on
/// an element-heavy table is observed on the firewall polygon.
#[test]
fn the_existence_probe_lists_the_chain_not_the_table() {
    let host = FakeFirewallHost::new().with_bans_table();

    ensure_bans_table(&host, distro()).expect("ensured");

    let argv = host.nft_call_starting_with("list").expect("a listing");
    assert!(argv.iter().any(|argument| argument == "chain"), "{argv:?}");
    assert!(
        !argv.iter().any(|argument| argument == "table"),
        "the probe must not list the whole table: {argv:?}"
    );
}
