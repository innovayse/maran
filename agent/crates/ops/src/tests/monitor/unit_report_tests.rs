//! Tests for `UnitReport`: reading `systemctl show`'s properties.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::UnitReport;

#[test]
fn each_property_is_read_by_its_own_name() {
    let report = UnitReport::parse(
        "LoadState=loaded\nActiveState=active\nSubState=running\nTriggeredBy=ssh.socket\n",
    );

    assert_eq!(report.load_state, "loaded");
    assert_eq!(report.active_state, "active");
    assert_eq!(report.sub_state, "running");
    assert_eq!(report.triggered_by, "ssh.socket");
}

#[test]
fn a_property_that_was_not_printed_is_empty() {
    // An empty ActiveState is classified as "not known" further up, which is
    // the only direction a missing answer is allowed to move a monitor.
    let report = UnitReport::parse("LoadState=loaded\n");

    assert_eq!(report.active_state, "");
    assert_eq!(report.triggered_by, "");
}

#[test]
fn a_property_this_area_did_not_ask_for_is_ignored() {
    let report = UnitReport::parse("Description=OpenBSD Secure Shell server\nActiveState=active\n");

    assert_eq!(report.active_state, "active");
}

#[test]
fn a_value_containing_an_equals_sign_survives() {
    // The key never contains a separator and the value may, so the line is
    // split at the first one only.
    let report = UnitReport::parse("SubState=start-pre=done\n");

    assert_eq!(report.sub_state, "start-pre=done");
}

#[test]
fn a_line_that_is_not_a_property_is_ignored() {
    let report = UnitReport::parse("\nnot a property\nActiveState=inactive\n");

    assert_eq!(report.active_state, "inactive");
}

#[test]
fn only_socket_units_count_as_triggers() {
    // TriggeredBy lists every unit that can start this one, which for a
    // periodic job is a timer. Only a socket answers "is something listening on
    // this service's behalf right now".
    let report = UnitReport::parse("TriggeredBy=backup.timer ssh.socket\n");

    assert_eq!(report.triggering_sockets(), ["ssh.socket"]);
}

#[test]
fn a_unit_that_nothing_triggers_has_no_sockets() {
    let report = UnitReport::parse("TriggeredBy=\n");

    assert!(report.triggering_sockets().is_empty());
}

#[test]
fn several_triggering_sockets_are_all_reported() {
    let report = UnitReport::parse("TriggeredBy=a.socket b.socket\n");

    assert_eq!(report.triggering_sockets(), ["a.socket", "b.socket"]);
}
