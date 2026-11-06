//! Tests for the list of read-only rpcs: the three that are easy to misfile
//! stay where their doc comments put them, and nothing that changes the host
//! has crept in. Coverage against the contract is the census in
//! `audit_rpc_class_tests.rs`.

use super::READ_ONLY_RPCS;

/// Verbs whose rpcs change the host and so belong in the other list.
const CHANGING_VERBS: [&str; 9] = [
    "Create", "Set", "Delete", "Drop", "Install", "Remove", "Write", "Enable", "Disable",
];

/// The method part of a full gRPC path.
fn method_of(path: &str) -> &str {
    path.rsplit('/').next().unwrap_or(path)
}

#[test]
fn the_calls_that_only_read_an_artifact_or_an_output_are_read_only() {
    for method in ["VerifyBackup", "ProbeDestination", "GetCronEntryOutput"] {
        assert!(
            READ_ONLY_RPCS.iter().any(|path| method_of(path) == method),
            "{method} is missing from the read-only list"
        );
    }
}

#[test]
fn no_rpc_named_for_changing_something_is_in_the_read_only_list() {
    for path in READ_ONLY_RPCS {
        assert!(
            !CHANGING_VERBS
                .iter()
                .any(|verb| method_of(path).starts_with(verb)),
            "{path} is named for changing"
        );
    }
}

#[test]
fn every_read_only_path_is_a_full_path_of_the_agent_package() {
    for path in READ_ONLY_RPCS {
        assert!(path.starts_with("/maran.agent.v1."), "{path}");
        assert_eq!(path.matches('/').count(), 2, "{path}");
    }
}

#[test]
fn the_handshake_and_every_monitor_rpc_are_read_only() {
    assert!(READ_ONLY_RPCS.contains(&"/maran.agent.v1.SystemService/GetAgentInfo"));
    assert_eq!(
        READ_ONLY_RPCS
            .iter()
            .filter(|path| path.starts_with("/maran.agent.v1.MonitorService/"))
            .count(),
        9
    );
}
