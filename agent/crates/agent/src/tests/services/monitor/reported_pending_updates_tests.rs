//! Tests for how a pending-updates reading reaches the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_distro::PendingUpdate;

use super::reported_pending_updates;
use crate::proto::list_pending_updates_response;
use crate::proto::{AgentError, ErrorCode, PendingUpdate as WirePendingUpdate};

/// An update as the distro crate reports it.
fn update(name: &str, installed: Option<&str>) -> PendingUpdate {
    PendingUpdate {
        package_name: name.to_owned(),
        available_version: String::from("2.0-1"),
        installed_version: installed.map(str::to_owned),
        source: String::from("baseos"),
    }
}

/// The updates a successful reading carries, or a panic naming the error.
fn wire_updates(outcome: Result<Vec<PendingUpdate>, AgentError>) -> Vec<WirePendingUpdate> {
    match reported_pending_updates(outcome) {
        list_pending_updates_response::Result::Ok(ok) => ok.updates,
        list_pending_updates_response::Result::Error(error) => {
            panic!("unexpected error {error:?}")
        }
    }
}

#[test]
fn each_update_keeps_its_four_fields_and_the_order_it_was_read_in() {
    let wire = wire_updates(Ok(vec![
        update("zlib", Some("1.0-1")),
        update("acl", Some("1.0-1")),
    ]));

    assert_eq!(
        wire,
        vec![
            WirePendingUpdate {
                package_name: String::from("zlib"),
                available_version: String::from("2.0-1"),
                installed_version: String::from("1.0-1"),
                source: String::from("baseos"),
            },
            WirePendingUpdate {
                package_name: String::from("acl"),
                available_version: String::from("2.0-1"),
                installed_version: String::from("1.0-1"),
                source: String::from("baseos"),
            },
        ]
    );
}

#[test]
fn an_update_with_no_installed_version_sends_the_empty_string() {
    let wire = wire_updates(Ok(vec![update("acl", None)]));

    assert_eq!(wire[0].installed_version, "");
}

#[test]
fn a_successful_reading_of_no_updates_is_an_empty_ok() {
    assert!(wire_updates(Ok(Vec::new())).is_empty());
}

/// A reading the agent could not take is the error arm, never an empty list the
/// panel would draw as "up to date".
#[test]
fn a_failed_reading_travels_as_the_error_and_not_as_an_empty_list() {
    let error = AgentError {
        code: ErrorCode::SystemFailure as i32,
        message: String::from("the pending package updates could not be read"),
        tool_output: String::new(),
    };

    let result = reported_pending_updates(Err(error.clone()));

    assert_eq!(result, list_pending_updates_response::Result::Error(error));
}
