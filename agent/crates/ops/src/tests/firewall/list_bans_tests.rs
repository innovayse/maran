//! Reading the bans back out of `nft`'s own JSON.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::validation::web::ban_address::BanAddress;

use crate::firewall::ensure_bans_table::{BANNED_V4_SET, BANNED_V6_SET};
use crate::firewall::fake_firewall_host::{FakeFirewallHost, distro};
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::list_bans::list_bans;

/// A validated address.
fn address(text: &str) -> BanAddress {
    BanAddress::parse(text).expect("a valid address")
}

/// Both family sets are read, and a timed ban reports what is LEFT of it
/// rather than what it was created with.
#[test]
fn both_sets_are_listed_with_their_remaining_lifetimes() {
    let host = FakeFirewallHost::new()
        .with_bans_table()
        .with_element(BANNED_V4_SET, "198.51.100.7", Some(3600))
        .with_element(BANNED_V6_SET, "2001:db8::1", Some(900));

    let bans = list_bans(&host, distro()).expect("listed");

    assert_eq!(bans.len(), 2);
    assert_eq!(bans[0].address, address("198.51.100.7"));
    assert_eq!(bans[0].expires_in, Some(Duration::from_secs(3600)));
    assert_eq!(bans[1].address, address("2001:db8::1"));
    assert_eq!(bans[1].expires_in, Some(Duration::from_secs(900)));
}

/// A ban with no timeout is read from the bare-string shape `nft` writes it
/// in, and reports no expiry.
#[test]
fn a_permanent_ban_is_listed_with_no_expiry() {
    let host =
        FakeFirewallHost::new()
            .with_bans_table()
            .with_element(BANNED_V4_SET, "198.51.100.7", None);

    let bans = list_bans(&host, distro()).expect("listed");

    assert_eq!(bans.len(), 1);
    assert_eq!(bans[0].expires_in, None);
}

/// An empty set carries no `elem` key at all, which is no bans rather than a
/// malformed answer.
#[test]
fn an_empty_set_reports_no_bans() {
    let host = FakeFirewallHost::new().with_bans_table();

    assert!(list_bans(&host, distro()).expect("listed").is_empty());
}

/// A host with no bans table has no bans: `nft` runs, exits non-zero because
/// the set is not there, and that is an answer.
#[test]
fn a_host_with_no_bans_table_reports_no_bans() {
    let host = FakeFirewallHost::new();

    assert!(list_bans(&host, distro()).expect("listed").is_empty());
}

/// An `nft` that cannot be RUN is a failure, not an empty ban list.
///
/// The difference between "there are no bans" and "I could not find out" is
/// exactly the difference a panel would otherwise paper over — and it would
/// paper it over in the direction of believing an attacker is blocked.
#[test]
fn an_nft_that_cannot_be_run_is_not_an_empty_ban_list() {
    let host = FakeFirewallHost::new().with_bans_table();
    host.lose_nft();

    assert_eq!(
        list_bans(&host, distro()),
        Err(FirewallError::NftFailed {
            stderr: String::from("could not run nft"),
        })
    );
}

/// Output that is not the JSON this agent knows is refused rather than read
/// as an empty ban list.
#[test]
fn json_this_agent_cannot_read_is_refused() {
    let host = FakeFirewallHost::new().with_bans_table();
    host.answer_bans_with("{\"something-else\":[]}");

    assert_eq!(
        list_bans(&host, distro()),
        Err(FirewallError::UnreadableNftOutput)
    );
}

/// A member whose shape this agent does not know is refused, for the same
/// reason: silence here is a panel believing an attacker is blocked.
#[test]
fn a_set_member_this_agent_cannot_read_is_refused() {
    let host = FakeFirewallHost::new().with_bans_table();
    host.answer_bans_with(
        "{\"nftables\":[{\"set\":{\"name\":\"banned_v4\",\"elem\":[{\"unexpected\":1}]}}]}",
    );

    assert_eq!(
        list_bans(&host, distro()),
        Err(FirewallError::UnreadableNftOutput)
    );
}

/// A remaining lifetime this agent cannot read is refused rather than
/// reported as "no expiry" — which the panel would read as a permanent ban.
#[test]
fn a_remaining_lifetime_this_agent_cannot_read_is_refused() {
    let host = FakeFirewallHost::new().with_bans_table();
    host.answer_bans_with(
        "{\"nftables\":[{\"set\":{\"name\":\"banned_v4\",\"elem\":[{\"elem\":\
         {\"val\":\"198.51.100.7\",\"expires\":\"1h30m\"}}]}}]}",
    );

    assert_eq!(
        list_bans(&host, distro()),
        Err(FirewallError::UnreadableNftOutput)
    );
}

/// An `elem` key that is present but is not an array is refused, and is NOT
/// reported as an empty ban list.
///
/// The two are distinguishable and the difference matters: a set with no
/// members omits the key entirely (verified against real nft v1.0.9), so a key
/// that is there and unreadable means this agent and this `nft` disagree about
/// the format. Collapsing it into "no bans" is the exact silence
/// `UnreadableNftOutput` exists to prevent — an operator reading an empty list
/// would conclude their bans had expired.
#[test]
fn an_elem_key_that_is_not_an_array_is_refused_rather_than_read_as_no_bans() {
    let host = FakeFirewallHost::new().with_bans_table();
    host.answer_bans_with(
        "{\"nftables\":[{\"set\":{\"name\":\"banned_v4\",\"elem\":\"198.51.100.7\"}}]}",
    );

    assert_eq!(
        list_bans(&host, distro()),
        Err(FirewallError::UnreadableNftOutput)
    );
}

/// A set with no members carries no `elem` key at all, and that IS no bans —
/// the other half of the distinction above, so neither answer can drift into
/// the other.
#[test]
fn a_set_with_no_elem_key_is_read_as_no_bans() {
    let host = FakeFirewallHost::new().with_bans_table();
    host.answer_bans_with("{\"nftables\":[{\"set\":{\"name\":\"banned_v4\"}}]}");

    assert!(list_bans(&host, distro()).expect("listed").is_empty());
}

/// A document carrying no readable set at all is refused, not read as no
/// bans.
///
/// This agent asks `nft` for ONE set by name, and `nft` exits non-zero when it
/// does not have it — so a successful answer with no set in it means the two
/// disagree about the format. It is the same argument as the `elem` handling,
/// one level up, and the same wrong answer if it is not made: an operator
/// reading an empty ban list would conclude their bans expired.
#[test]
fn a_document_with_no_set_object_is_refused_rather_than_read_as_no_bans() {
    let host = FakeFirewallHost::new().with_bans_table();
    host.answer_bans_with("{\"nftables\":[{\"metainfo\":{\"version\":\"1.0.9\"}}]}");

    assert_eq!(
        list_bans(&host, distro()),
        Err(FirewallError::UnreadableNftOutput)
    );
}

/// An empty `nftables` array carries no set either, and is refused for the
/// same reason.
#[test]
fn an_empty_nftables_array_is_refused_rather_than_read_as_no_bans() {
    let host = FakeFirewallHost::new().with_bans_table();
    host.answer_bans_with("{\"nftables\":[]}");

    assert_eq!(
        list_bans(&host, distro()),
        Err(FirewallError::UnreadableNftOutput)
    );
}

/// A `set` key that is present but is not an object is refused.
///
/// `Value::get` answers `None` on a string, a number, an array and a null
/// alike, so without an explicit check every one of them would take the same
/// path as the metainfo object and be silently skipped.
#[test]
fn a_set_that_is_not_an_object_is_refused() {
    let host = FakeFirewallHost::new().with_bans_table();
    host.answer_bans_with("{\"nftables\":[{\"set\":\"banned_v4\"}]}");

    assert_eq!(
        list_bans(&host, distro()),
        Err(FirewallError::UnreadableNftOutput)
    );
}

/// An unreadable set beside a readable one is still refused.
///
/// This is what tells the two guards in `read_set` apart. The "did I read any
/// set at all" guard catches a document whose ONLY set is unreadable, so on
/// its own it would let this shape through — the readable set would satisfy
/// it and the junk one would be skipped in silence, reporting a ban list that
/// is missing whatever the second set held. Only the per-set refusal sees
/// this. Without a case that separates them, one of the two would be
/// decoration that no test could distinguish from a protection.
#[test]
fn a_readable_set_beside_an_unreadable_one_is_refused() {
    let host = FakeFirewallHost::new().with_bans_table();
    host.answer_bans_with(
        "{\"nftables\":[{\"set\":{\"name\":\"banned_v4\",\"elem\":[\"198.51.100.7\"]}},\
         {\"set\":\"banned_v6\"}]}",
    );

    assert_eq!(
        list_bans(&host, distro()),
        Err(FirewallError::UnreadableNftOutput)
    );
}

/// A member that is not an address this agent could have banned is refused
/// too — nothing else writes to these sets.
#[test]
fn a_set_member_that_is_not_an_address_is_refused() {
    let host = FakeFirewallHost::new().with_bans_table();
    host.answer_bans_with(
        "{\"nftables\":[{\"set\":{\"name\":\"banned_v4\",\"elem\":[\"not-an-address\"]}}]}",
    );

    assert_eq!(
        list_bans(&host, distro()),
        Err(FirewallError::UnreadableNftOutput)
    );
}
