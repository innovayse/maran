//! The table that keeps this host's own users off port 25.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_agent_core::utils::ipv4_prefix::Ipv4Prefix;

use crate::mail::render_self_clients::render_self_clients;

fn addresses(list: &[&str]) -> HostAddresses {
    HostAddresses {
        addresses: list.iter().map(|text| text.parse().unwrap()).collect(),
        local_prefixes: Vec::new(),
    }
}

fn with_range(mut host: HostAddresses, network: &str, length: u8) -> HostAddresses {
    host.local_prefixes
        .push(Ipv4Prefix::new(network.parse().unwrap(), length).unwrap());
    host
}

fn entries(rendered: &str) -> Vec<String> {
    rendered
        .lines()
        .filter(|line| !line.starts_with('#'))
        .map(|line| line.split_whitespace().next().unwrap().to_owned())
        .collect()
}

#[test]
fn every_interface_address_and_both_loopbacks_are_refused() {
    let rendered =
        render_self_clients(&addresses(&["127.0.0.1", "192.0.2.10", "2001:db8::5"])).unwrap();

    assert_eq!(
        entries(&rendered),
        ["127.0.0.0/8", "::1/128", "192.0.2.10/32", "2001:db8::5/128"]
    );
}

#[test]
fn an_address_the_host_does_not_carry_is_not_in_the_table() {
    // The inverse control: a client at an address that is none of the host's is not
    // refused, so mail from the rest of the internet still reaches the service.
    let rendered = render_self_clients(&addresses(&["192.0.2.10"])).unwrap();
    let listed = entries(&rendered);

    assert!(!listed.contains(&"192.0.2.11/32".to_owned()));
    assert!(!listed.contains(&"198.51.100.7/32".to_owned()));
    assert!(!listed.contains(&"0.0.0.0/0".to_owned()));
    assert!(listed.iter().all(|entry| entry.contains('/')));
}

#[test]
fn a_loopback_address_is_not_listed_twice() {
    let rendered = render_self_clients(&addresses(&["127.0.0.1", "127.0.1.1", "::1"])).unwrap();

    assert_eq!(entries(&rendered), ["127.0.0.0/8", "::1/128"]);
}

#[test]
fn the_order_the_kernel_lists_addresses_in_does_not_change_the_bytes() {
    let one = render_self_clients(&addresses(&["198.51.100.7", "192.0.2.10", "192.0.2.10"]));
    let other = render_self_clients(&addresses(&["192.0.2.10", "198.51.100.7"]));

    assert_eq!(one.unwrap(), other.unwrap());
}

#[test]
fn a_host_gaining_an_address_changes_the_table() {
    let before = render_self_clients(&addresses(&["192.0.2.10"])).unwrap();
    let after = render_self_clients(&addresses(&["192.0.2.10", "198.51.100.7"])).unwrap();

    assert_ne!(before, after);
    assert!(after.contains("198.51.100.7/32 REJECT"));
}

/// Issue #535: a routed block (`ip route add local 203.0.113.0/24 dev lo`) answers
/// on this host at every address, so the table refuses the block and not only the
/// interface addresses.
#[test]
fn a_local_range_is_refused_as_a_whole_and_swallows_the_addresses_inside_it() {
    let host = with_range(addresses(&["192.0.2.10", "203.0.113.5"]), "203.0.113.0", 24);

    let listed = entries(&render_self_clients(&host).unwrap());

    assert!(listed.contains(&"203.0.113.0/24".to_owned()));
    assert!(listed.contains(&"192.0.2.10/32".to_owned()));
    assert!(!listed.contains(&"203.0.113.5/32".to_owned()));
}

/// Inverse control: the loopback range the kernel lists is the template's constant,
/// not a second entry.
#[test]
fn the_loopback_range_from_the_routing_table_is_not_listed_twice() {
    let host = with_range(addresses(&["192.0.2.10"]), "127.0.0.0", 8);

    assert_eq!(
        entries(&render_self_clients(&host).unwrap()),
        ["127.0.0.0/8", "::1/128", "192.0.2.10/32"]
    );
}

/// The review gap: a wide local route AROUND the loopback network (a host that answers
/// everywhere) used to be listed here and dropped by the firewall, so the layers
/// disagreed, and a `0.0.0.0/0 REJECT` line would have refused every client. Both layers
/// now drop it.
#[test]
fn a_range_around_the_loopback_network_is_not_listed() {
    let host = with_range(addresses(&["192.0.2.10"]), "0.0.0.0", 0);

    assert_eq!(
        entries(&render_self_clients(&host).unwrap()),
        ["127.0.0.0/8", "::1/128", "192.0.2.10/32"]
    );
}

/// The two layers read one host the same way: whichever ranges the mail table lists
/// beyond its constants are exactly the ranges the firewall's sets hold.
#[test]
fn the_mail_table_and_the_firewall_sets_list_the_same_ranges() {
    use crate::firewall::model::self_address_sets::SelfAddressSets;

    let mut host = addresses(&["192.0.2.10"]);
    for (network, length) in [
        ("0.0.0.0", 1),
        ("127.0.0.0", 8),
        ("127.2.0.0", 16),
        ("203.0.113.0", 24),
    ] {
        host = with_range(host, network, length);
    }

    let mail: Vec<String> = entries(&render_self_clients(&host).unwrap())
        .into_iter()
        .filter(|entry| entry.ends_with("/24"))
        .collect();
    let firewall: Vec<String> = SelfAddressSets::new(&host)
        .v4_prefixes()
        .iter()
        .map(ToString::to_string)
        .collect();

    assert_eq!(mail, firewall);
    assert_eq!(firewall, ["203.0.113.0/24"]);
}
