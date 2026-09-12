//! Tests for the wire code every backup failure travels as.
//!
//! Every variant of `BackupError` appears here (rules/testing.md), and the
//! file is organised by the distinction an operator acts on rather than by the
//! order of the enum: a retry that has already converged (ALREADY_EXISTS,
//! NOT_FOUND), a request the agent will not act on (INVALID_INPUT), a refusal
//! that left the account exactly as it was (VALIDATION_FAILED), and a machine
//! that failed at what it was told (SYSTEM_FAILURE). Collapsing any pair of
//! those sends the wrong person looking.
//!
//! The second proposition of the file is about secrecy rather than routing:
//! `tool_output` is empty for every variant, because no variant of this error
//! can carry a dump client's output — and a dump client that refuses partway
//! prints a customer's rows.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::backup::BackupError;

use super::to_agent_error;
use crate::proto::ErrorCode;

/// Every variant, once, so that the exhaustiveness of this file is a fact a
/// reader can check rather than a claim.
fn every_variant() -> Vec<BackupError> {
    vec![
        BackupError::BackupBinaryMissing {
            program: "tar".to_owned(),
            path: "/usr/bin/tar".to_owned(),
        },
        BackupError::AlreadyRunning,
        BackupError::AlreadyExists,
        BackupError::BackupRootUnsafe { uid: 1000, mode: 0 },
        BackupError::BackupRootUnusable,
        BackupError::UnmintedArtifactName {
            name: "notes.tar.gz".to_owned(),
        },
        BackupError::ScratchUnusable,
        BackupError::DatabasesUnknown,
        BackupError::DumpFailed { status: 2 },
        BackupError::DumpTooLarge {
            limit: 1,
            actual: 2,
        },
        BackupError::ArchiveFailed { status: 2 },
        BackupError::ArchiveTooLarge {
            limit: 1,
            actual: 2,
        },
        BackupError::ManifestUnwritable,
        BackupError::ChecksumUnreadable,
        BackupError::HomeUnreadable,
        BackupError::ArtifactUnpublishable,
        BackupError::NotFound,
        BackupError::ArtifactUndeletable,
        BackupError::ChecksumMismatch,
        BackupError::ManifestVersionUnknown,
        BackupError::ManifestAccountMismatch,
        BackupError::ManifestDisagreesWithSidecar,
        BackupError::UnexpectedArchiveMember {
            name: "../../etc/cron.d/pwn".to_owned(),
        },
        BackupError::UnknownDatabase {
            name: "alice_ghost".to_owned(),
        },
        BackupError::DumpChecksumMismatch {
            database: "alice_shop".to_owned(),
        },
        BackupError::DropFailed { status: 1 },
        BackupError::LoadFailed { status: 1 },
        BackupError::StagingUnusable,
        BackupError::ExtractionIdentityUnavailable,
        BackupError::RolledBack {
            failed: "alice_shop".to_owned(),
            rolled_back: vec!["alice_blog".to_owned()],
        },
        BackupError::RolledBackPartially {
            failed: "alice_shop".to_owned(),
            rolled_back: vec!["alice_blog".to_owned()],
            not_rolled_back: vec!["alice_wiki".to_owned()],
        },
        BackupError::HomeParkedAt {
            path: "/var/lib/maran/restore/alice.previous".to_owned(),
        },
        BackupError::DestinationInsecure,
        BackupError::ObjectNotFound,
        BackupError::ObjectStoreFailed {
            message: "the bucket refused".to_owned(),
        },
    ]
}

/// The code `error` is reported as.
fn code_of(error: &BackupError) -> i32 {
    to_agent_error(error).code
}

#[test]
fn a_repeated_creation_is_already_exists() {
    assert_eq!(
        code_of(&BackupError::AlreadyExists),
        ErrorCode::AlreadyExists as i32
    );
}

#[test]
fn an_account_another_operation_is_holding_is_busy_and_never_already_exists() {
    // These two used to be asserted together as ALREADY_EXISTS, which made one
    // code mean two things. ALREADY_EXISTS is the idempotency outcome: the
    // archive is there, treat the creation as done. AlreadyRunning is the
    // account's lock being held BEFORE anything was dumped, so a caller that
    // read it as the idempotency outcome would mark a backup complete that was
    // never taken.
    assert_eq!(
        code_of(&BackupError::AlreadyRunning),
        ErrorCode::AccountBusy as i32
    );
    assert_ne!(
        code_of(&BackupError::AlreadyRunning),
        code_of(&BackupError::AlreadyExists),
        "a refusal that dumped nothing and an archive that is already there \
         must not be one code"
    );
}

#[test]
fn a_busy_account_and_a_genuine_fault_of_this_host_are_different_codes() {
    // The inverse control: a dump this host could not take must still read as a
    // fault, or the busy code above was bought by relabelling the area.
    assert_eq!(
        code_of(&BackupError::DumpFailed { status: 2 }),
        ErrorCode::SystemFailure as i32
    );
    assert_ne!(
        code_of(&BackupError::AlreadyRunning),
        code_of(&BackupError::DumpFailed { status: 2 })
    );
}

#[test]
fn a_backup_that_is_not_there_is_not_found() {
    for error in [BackupError::NotFound, BackupError::ObjectNotFound] {
        assert_eq!(code_of(&error), ErrorCode::NotFound as i32);
    }
}

#[test]
fn what_the_caller_asked_for_and_the_agent_will_not_act_on_is_invalid_input() {
    for error in [
        BackupError::UnexpectedArchiveMember {
            name: "../../etc/cron.d/pwn".to_owned(),
        },
        BackupError::UnknownDatabase {
            name: "alice_ghost".to_owned(),
        },
        BackupError::ManifestAccountMismatch,
        BackupError::DestinationInsecure,
    ] {
        assert_eq!(code_of(&error), ErrorCode::InvalidInput as i32);
    }
}

#[test]
fn the_checks_that_refuse_before_anything_is_destroyed_are_validation_failed() {
    for error in [
        BackupError::ChecksumMismatch,
        BackupError::DumpChecksumMismatch {
            database: "alice_shop".to_owned(),
        },
        BackupError::ManifestVersionUnknown,
        BackupError::ManifestDisagreesWithSidecar,
        BackupError::DumpTooLarge {
            limit: 1,
            actual: 2,
        },
        BackupError::ArchiveTooLarge {
            limit: 1,
            actual: 2,
        },
        BackupError::UnmintedArtifactName {
            name: "notes.tar.gz".to_owned(),
        },
        BackupError::BackupRootUnsafe { uid: 1000, mode: 0 },
    ] {
        assert_eq!(code_of(&error), ErrorCode::ValidationFailed as i32);
    }
}

#[test]
fn a_rollback_and_a_parked_home_are_system_failures_naming_what_happened() {
    let rolled_back = BackupError::RolledBackPartially {
        failed: "alice_shop".to_owned(),
        rolled_back: vec!["alice_blog".to_owned()],
        not_rolled_back: vec!["alice_wiki".to_owned()],
    };
    let error = to_agent_error(&rolled_back);

    assert_eq!(error.code, ErrorCode::SystemFailure as i32);
    // The message is the ONLY carrier of these names on the wire: the ok arm's
    // `not_rolled_back` is always empty because a restore that fails never
    // reaches it. So the mapping must not flatten the message away.
    assert!(error.message.contains("alice_wiki"), "{}", error.message);

    let parked = to_agent_error(&BackupError::HomeParkedAt {
        path: "/var/lib/maran/restore/alice.previous".to_owned(),
    });
    assert_eq!(parked.code, ErrorCode::SystemFailure as i32);
    assert!(
        parked.message.contains("/var/lib/maran"),
        "{}",
        parked.message
    );
}

#[test]
fn this_machine_failing_at_what_it_was_told_is_a_system_failure() {
    for error in [
        BackupError::BackupBinaryMissing {
            program: "tar".to_owned(),
            path: "/usr/bin/tar".to_owned(),
        },
        BackupError::BackupRootUnusable,
        BackupError::ScratchUnusable,
        BackupError::DatabasesUnknown,
        BackupError::DumpFailed { status: 2 },
        BackupError::ArchiveFailed { status: 2 },
        BackupError::ManifestUnwritable,
        BackupError::ChecksumUnreadable,
        BackupError::HomeUnreadable,
        BackupError::ArtifactUnpublishable,
        BackupError::ArtifactUndeletable,
        BackupError::DropFailed { status: 1 },
        BackupError::LoadFailed { status: 1 },
        BackupError::StagingUnusable,
        BackupError::ExtractionIdentityUnavailable,
        BackupError::ObjectStoreFailed {
            message: "the bucket refused".to_owned(),
        },
    ] {
        assert_eq!(code_of(&error), ErrorCode::SystemFailure as i32);
    }
}

#[test]
fn no_variant_puts_anything_in_tool_output() {
    for error in every_variant() {
        assert!(
            to_agent_error(&error).tool_output.is_empty(),
            "{error} filled tool_output"
        );
    }
}

#[test]
fn every_variant_is_classified_and_none_reports_the_unspecified_code() {
    for error in every_variant() {
        assert_ne!(
            code_of(&error),
            ErrorCode::Unspecified as i32,
            "{error} reported the unspecified code"
        );
    }
}
