//! What a port range is allowed to be, and how it is written and read back.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::firewall::fake_firewall_host::port;
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::model::port_span::PortSpan;

#[test]
fn a_single_port_span_carries_that_port_and_no_upper_bound() {
    let span = PortSpan::single(port(8080));

    assert_eq!(span.lower(), port(8080));
    assert_eq!(span.upper(), None);
    assert_eq!(span.to_string(), "8080");
}

#[test]
fn a_range_carries_both_bounds_and_writes_them_around_one_separator() {
    let span = PortSpan::new(port(30000), Some(port(30099))).unwrap();

    assert_eq!(span.lower(), port(30000));
    assert_eq!(span.upper(), Some(port(30099)));
    assert_eq!(span.to_string(), "30000-30099");
}

#[test]
fn an_upper_bound_below_the_lower_one_is_refused() {
    assert_eq!(
        PortSpan::new(port(30099), Some(port(30000))),
        Err(FirewallError::InvalidRange)
    );
}

#[test]
fn an_upper_bound_equal_to_the_lower_one_is_refused() {
    // The dangerous half: `nft` accepts `dport 30000-30000`, so nothing
    // downstream would catch it. It would be a second spelling of the single
    // port 30000, and a later deny naming that port matches rules by their
    // whole value — it would find nothing and report success while the port
    // stayed open.
    assert_eq!(
        PortSpan::new(port(30000), Some(port(30000))),
        Err(FirewallError::InvalidRange)
    );
}

#[test]
fn a_span_of_one_port_is_not_the_same_value_as_a_range_that_starts_there() {
    // What makes a deny for a range name both bounds: the two are different
    // rules, so `contains` and `without` cannot confuse them.
    assert_ne!(
        PortSpan::single(port(30000)),
        PortSpan::new(port(30000), Some(port(30099))).unwrap()
    );
}

#[test]
fn a_rendered_single_port_is_read_back_as_one() {
    assert_eq!(
        PortSpan::parse_rendered("8443"),
        Some(PortSpan::single(port(8443)))
    );
}

#[test]
fn a_rendered_range_is_read_back_with_both_bounds() {
    assert_eq!(
        PortSpan::parse_rendered("30000-30099"),
        PortSpan::new(port(30000), Some(port(30099))).ok()
    );
}

#[test]
fn a_rendered_range_this_agent_would_refuse_to_write_is_not_read_back() {
    // Parse and render are inverses: a hand-edited file must not be able to
    // introduce a span the constructor refuses.
    assert_eq!(PortSpan::parse_rendered("30099-30000"), None);
    assert_eq!(PortSpan::parse_rendered("30000-30000"), None);
}

#[test]
fn a_rendered_bound_with_a_leading_zero_is_not_read_back() {
    assert_eq!(PortSpan::parse_rendered("08443"), None);
    assert_eq!(PortSpan::parse_rendered("30000-030099"), None);
}

#[test]
fn a_rendered_bound_with_a_leading_sign_is_not_read_back() {
    assert_eq!(PortSpan::parse_rendered("+8443"), None);
    assert_eq!(PortSpan::parse_rendered("30000-+30099"), None);
}

#[test]
fn a_rendered_bound_outside_the_port_numbers_is_not_read_back() {
    assert_eq!(PortSpan::parse_rendered("0"), None);
    assert_eq!(PortSpan::parse_rendered("1-65536"), None);
}

#[test]
fn a_rendered_span_that_is_not_two_numbers_is_not_read_back() {
    assert_eq!(PortSpan::parse_rendered("30000-30050-30099"), None);
    assert_eq!(PortSpan::parse_rendered("30000-"), None);
    assert_eq!(PortSpan::parse_rendered("-30099"), None);
    assert_eq!(PortSpan::parse_rendered(""), None);
}
