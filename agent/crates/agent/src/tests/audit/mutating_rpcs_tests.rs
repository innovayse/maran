//! Tests for the list of mutating rpcs: the calls whose classification a
//! reader would be most likely to get wrong stay in it, and nothing that only
//! reports state has crept in. Coverage against the contract is the census in
//! `audit_rpc_class_tests.rs`.

use super::MUTATING_RPCS;

/// Verbs whose rpcs report state and so belong in the other list.
const READING_VERBS: [&str; 4] = ["List", "Get", "Tail", "Read"];

/// The method part of a full gRPC path.
fn method_of(path: &str) -> &str {
    path.rsplit('/').next().unwrap_or(path)
}

#[test]
fn the_calls_that_look_harmless_but_change_the_host_are_mutating() {
    for method in [
        "RunCronEntry",
        "EndSftpSessions",
        "ReloadWebServer",
        "ReloadFtpsTls",
        "RepairAccountHomeGroups",
        "RepairDatabaseGrants",
        "SetAccountLoginsLocked",
    ] {
        assert!(
            MUTATING_RPCS.iter().any(|path| method_of(path) == method),
            "{method} is missing from the mutating list"
        );
    }
}

#[test]
fn no_rpc_named_for_reading_is_in_the_mutating_list() {
    for path in MUTATING_RPCS {
        assert!(
            !READING_VERBS
                .iter()
                .any(|verb| method_of(path).starts_with(verb)),
            "{path} is named for reading"
        );
    }
}

#[test]
fn every_mutating_path_is_a_full_path_of_the_agent_package() {
    for path in MUTATING_RPCS {
        assert!(path.starts_with("/maran.agent.v1."), "{path}");
        assert_eq!(path.matches('/').count(), 2, "{path}");
    }
}

#[test]
fn the_streaming_mutating_rpcs_are_listed() {
    for path in [
        "/maran.agent.v1.BackupService/CreateBackup",
        "/maran.agent.v1.BackupService/RestoreBackup",
        "/maran.agent.v1.PhpService/InstallPhpVersion",
        "/maran.agent.v1.FilesService/WriteFile",
    ] {
        assert!(MUTATING_RPCS.contains(&path), "{path}");
    }
}
