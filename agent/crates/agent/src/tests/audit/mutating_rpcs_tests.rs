//! Tests for the list of mutating rpcs: the calls whose classification a
//! reader would be most likely to get wrong stay in it, and nothing that only
//! reports state has crept in. Coverage against the contract is the census in
//! `audit_rpc_class_tests.rs`.

use super::MUTATING_RPCS;

/// Verbs whose rpcs report state and so belong in the other list.
const READING_VERBS: [&str; 4] = ["List", "Get", "Tail", "Read"];

/// The rpcs named for reading that are mutating all the same, with the reason:
/// `ReadSourceInventory` reads a remote host, but it opens an outbound SSH
/// connection to a host an administrator named, which is the event the trail
/// exists for (issue #197).
const READING_NAMES_THAT_ARE_MUTATING: [&str; 1] = ["ReadSourceInventory"];

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
        "ApplyPhpPoolSettings",
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
            READING_NAMES_THAT_ARE_MUTATING.contains(&method_of(path))
                || !READING_VERBS
                    .iter()
                    .any(|verb| method_of(path).starts_with(verb)),
            "{path} is named for reading"
        );
    }
}

#[test]
fn the_reading_name_exemptions_are_each_still_in_the_list_and_still_named_for_reading() {
    // Inverse control: an exemption nobody needs any more would let a genuine
    // reader into the mutating list unnoticed.
    for method in READING_NAMES_THAT_ARE_MUTATING {
        assert!(
            MUTATING_RPCS.iter().any(|path| method_of(path) == method),
            "{method} is exempted but no longer listed"
        );
        assert!(
            READING_VERBS.iter().any(|verb| method.starts_with(verb)),
            "{method} is exempted but is not named for reading"
        );
    }
}

#[test]
fn the_remote_source_rpcs_are_mutating_because_they_open_an_outbound_connection() {
    for method in [
        "GenerateSourceKey",
        "ScanHostKey",
        "TestSource",
        "ReadSourceInventory",
    ] {
        assert!(
            MUTATING_RPCS
                .iter()
                .any(|path| path.starts_with("/maran.agent.v1.TransferService/")
                    && method_of(path) == method),
            "{method} is missing from the mutating list"
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
        "/maran.agent.v1.PhpService/SetPhpExtension",
        "/maran.agent.v1.FilesService/WriteFile",
    ] {
        assert!(MUTATING_RPCS.contains(&path), "{path}");
    }
}

#[test]
fn every_file_manager_mutation_including_the_trash_ones_is_listed() {
    for method in [
        "CreateDirectory",
        "MoveEntry",
        "ChangePermissions",
        "CopyEntry",
        "ForceDeleteEntry",
        "TrashEntry",
        "RestoreTrashEntry",
        "EmptyTrash",
        "PurgeExpiredTrash",
    ] {
        let path = format!("/maran.agent.v1.FilesService/{method}");
        assert!(
            MUTATING_RPCS.contains(&path.as_str()),
            "{method} is missing from the mutating list"
        );
    }
}

#[test]
fn both_moves_of_a_backup_between_servers_are_mutating_although_one_changes_nothing() {
    for method in ["ExportBackup", "ImportBackup"] {
        let path = format!("/maran.agent.v1.TransferService/{method}");
        assert!(
            MUTATING_RPCS.contains(&path.as_str()),
            "{method} is missing from the mutating list"
        );
    }
}

/// Signing rides inside `PutZone` and adds no rpc (issue #452), so `PutZone` is
/// the one call that installs and removes a zone's private signing key and it must
/// stay in the list. The control: `GetZone`, which only observes the signing, is
/// not in it.
#[test]
fn the_zone_write_that_carries_a_signing_key_is_mutating_and_the_zone_read_is_not() {
    let dns_methods: Vec<&str> = MUTATING_RPCS
        .iter()
        .filter(|path| path.contains(".DnsService/"))
        .map(|path| method_of(path))
        .collect();

    assert!(dns_methods.contains(&"PutZone"), "{dns_methods:?}");
    assert!(!dns_methods.contains(&"GetZone"), "{dns_methods:?}");
}
