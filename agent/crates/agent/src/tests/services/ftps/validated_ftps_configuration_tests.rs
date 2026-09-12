//! What an `EnableFtps` request may and may not carry.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::proto::{EnableFtpsRequest, ErrorCode};
use crate::services::ftps::validated_ftps_configuration::validated_ftps_configuration;

/// A request every field of which the agent accepts.
fn a_good_request() -> EnableFtpsRequest {
    EnableFtpsRequest {
        hostname: "ftp.example.test".to_owned(),
        passive_port_min: 30000,
        passive_port_max: 30099,
        passive_address: String::new(),
        max_clients: 50,
    }
}

#[test]
fn a_well_formed_request_becomes_the_configuration_the_operation_takes() {
    let configuration = validated_ftps_configuration(&a_good_request()).expect("accepted");

    assert_eq!(configuration.hostname.as_str(), "ftp.example.test");
    assert_eq!(configuration.passive_port_min.value(), 30000);
    assert_eq!(configuration.passive_port_max.value(), 30099);
    assert!(configuration.passive_address.is_none());
    assert_eq!(configuration.max_clients, 50);
}

#[test]
fn an_empty_passive_address_is_the_ordinary_host_and_not_a_refusal() {
    let configuration = validated_ftps_configuration(&a_good_request()).expect("accepted");

    assert!(
        configuration.passive_address.is_none(),
        "the key is then not written at all and the daemon answers with the \
         address the control connection arrived on"
    );
}

#[test]
fn a_passive_address_that_is_given_is_validated_as_a_dotted_quad() {
    let mut request = a_good_request();
    request.passive_address = "203.0.113.7".to_owned();
    let configuration = validated_ftps_configuration(&request).expect("accepted");
    assert_eq!(
        configuration.passive_address.expect("present").as_str(),
        "203.0.113.7"
    );

    let mut bad = a_good_request();
    bad.passive_address = "not an address".to_owned();
    assert!(validated_ftps_configuration(&bad).is_err());
}

#[test]
fn a_range_whose_minimum_is_above_its_maximum_is_refused() {
    // It renders a config that parses and then serves no passive connection at
    // all — a daemon that greets, authenticates and hangs on the first listing,
    // which every liveness check the enable path runs answers yes to.
    let mut request = a_good_request();
    request.passive_port_min = 30099;
    request.passive_port_max = 30000;

    let error = validated_ftps_configuration(&request).expect_err("refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_range_of_exactly_one_port_is_accepted() {
    // The inverse control for the check above: a gate mutated to refuse
    // everything passes every test that only ever hands it broken input.
    let mut request = a_good_request();
    request.passive_port_min = 30000;
    request.passive_port_max = 30000;

    assert!(validated_ftps_configuration(&request).is_ok());
}

#[test]
fn a_port_outside_the_range_a_port_can_take_is_refused() {
    for (min, max) in [(0, 30099), (30000, 0), (30000, 70000)] {
        let mut request = a_good_request();
        request.passive_port_min = min;
        request.passive_port_max = max;
        assert!(
            validated_ftps_configuration(&request).is_err(),
            "{min}-{max} must be refused"
        );
    }
}

#[test]
fn a_hostname_the_agent_will_not_accept_is_refused_before_anything_is_rendered() {
    for hostname in ["", "not a host", "ftp.example.test\nlisten_port=2121"] {
        let mut request = a_good_request();
        request.hostname = hostname.to_owned();
        assert!(
            validated_ftps_configuration(&request).is_err(),
            "{hostname:?} must be refused — it reaches a line-oriented config file"
        );
    }
}
