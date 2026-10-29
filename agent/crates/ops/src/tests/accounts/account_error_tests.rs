//! Tests for how the other areas' failures and the spawn core's fold into the
//! account area's.
//!
//! Test code may panic where production code may not (rules/rust.md
//! "Toolchain & lints").
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;
use maran_distro::{DistroFamily, adapter_for};

use super::AccountError;
use crate::db::DbError;
use crate::ftps::FtpsError;
use crate::php::PhpOpError;
use crate::safe_write::adapter_programs::timed_out;
use crate::sftp::SftpError;
use crate::sites::SitesOpError;

/// The bound every conversion below is given, and must hand back unchanged.
fn bound() -> BoundExceeded {
    timed_out("/usr/sbin/userdel")
}

/// Whether `error` is the account area's bound for [`bound`].
fn is_the_bound(error: &AccountError) -> bool {
    matches!(error, AccountError::Bound(inner) if *inner == bound())
}

/// A program the core killed is a bound, not "unavailable".
#[test]
fn a_bound_from_spawning_reaches_the_account_area_as_a_bound() {
    // `CommandUnavailable` says the tool is not installed, and an operator would go and
    // install it. The tool ran, and was stopped.
    let program = adapter_for(DistroFamily::Debian).useradd_binary();

    let error = AccountError::from_spawn(&program, SpawnError::Bound(bound()));

    assert!(is_the_bound(&error), "{error:?}");
}

/// The inverse control: a program that never started is unavailable and named.
#[test]
fn a_program_that_never_started_is_unavailable_and_named() {
    let program = adapter_for(DistroFamily::Debian).useradd_binary();

    let error = AccountError::from_spawn(
        &program,
        SpawnError::Start {
            program: "/usr/sbin/useradd".to_owned(),
            reason: "No such file or directory".to_owned(),
        },
    );

    match error {
        AccountError::CommandUnavailable { program, reason } => {
            assert_eq!(program, "/usr/sbin/useradd");
            assert!(reason.contains("No such file or directory"), "{reason}");
        }
        other => panic!("expected CommandUnavailable, got {other:?}"),
    }
}

/// A pool removal that was killed has not shown the account still owns the pool.
#[test]
fn a_php_bound_reaches_the_account_area_as_a_bound_and_not_a_pool_removal() {
    let error = AccountError::from(PhpOpError::Bound(bound()));

    assert!(is_the_bound(&error), "{error:?}");
}

/// A database removal that was killed has not shown the account still owns a database.
#[test]
fn a_database_bound_reaches_the_account_area_as_a_bound_and_not_a_database_removal() {
    let error = AccountError::from(DbError::Bound(bound()));

    assert!(is_the_bound(&error), "{error:?}");
}

/// An SFTP removal that was killed is a bound.
#[test]
fn an_sftp_bound_reaches_the_account_area_as_a_bound_and_not_an_sftp_removal() {
    let error = AccountError::from(SftpError::Bound(bound()));

    assert!(is_the_bound(&error), "{error:?}");
}

/// An FTPS removal that was killed is a bound.
#[test]
fn an_ftps_bound_reaches_the_account_area_as_a_bound_and_not_an_ftps_removal() {
    let error = AccountError::from(FtpsError::Bound(bound()));

    assert!(is_the_bound(&error), "{error:?}");
}

/// A site read that was killed is a bound, not a failed inspection.
#[test]
fn a_sites_bound_reaches_the_account_area_as_a_bound_and_not_a_site_inspection() {
    let error = AccountError::from(SitesOpError::Bound(bound()));

    assert!(is_the_bound(&error), "{error:?}");
}

/// The inverse controls: every other failure keeps flattening into its removal variant.
#[test]
fn the_other_failures_of_each_area_still_flatten_into_their_removal_variants() {
    assert!(matches!(
        AccountError::from(PhpOpError::UnsupportedVersion {
            version: "9.9".to_owned()
        }),
        AccountError::PoolRemoval { .. }
    ));
    assert!(matches!(
        AccountError::from(DbError::AccessDenied),
        AccountError::DatabaseRemoval { .. }
    ));
}

/// Each leftover refusal names the account and nothing else.
#[test]
fn the_at_jobs_remain_message_names_the_account_and_no_job_detail() {
    let error = AccountError::AtJobsRemain {
        username: "acme".to_owned(),
    };

    assert_eq!(
        error.to_string(),
        "scheduled at jobs of account 'acme' are still queued after being removed"
    );
}

/// The user manager refusal names the account only.
#[test]
fn the_user_manager_remains_message_names_the_account_only() {
    let error = AccountError::UserManagerRemains {
        username: "acme".to_owned(),
    };

    assert_eq!(
        error.to_string(),
        "the user manager of account 'acme' is still running after being ended"
    );
}

/// The IPC refusal names the account and no object id.
#[test]
fn the_ipc_remain_message_names_the_account_and_no_object_detail() {
    let error = AccountError::IpcRemain {
        username: "acme".to_owned(),
    };

    assert_eq!(
        error.to_string(),
        "IPC objects of account 'acme' are still present after being removed"
    );
}

/// The shared files refusal names the account and no path.
#[test]
fn the_shared_files_remain_message_names_the_account_and_no_path() {
    let error = AccountError::SharedFilesRemain {
        username: "acme".to_owned(),
    };

    assert_eq!(
        error.to_string(),
        "shared files of account 'acme' are still present after being removed"
    );
}

/// The mail spool refusal names the account and no path.
#[test]
fn the_mail_spool_remains_message_names_the_account_and_no_path() {
    let error = AccountError::MailSpoolRemains {
        username: "acme".to_owned(),
    };

    assert_eq!(
        error.to_string(),
        "the mail spool of account 'acme' is still present after being removed"
    );
}

/// The survivor refusal names the account and nothing else.
#[test]
fn the_processes_remain_message_names_the_account_and_no_process_detail() {
    let error = AccountError::ProcessesRemain {
        username: "acme".to_owned(),
    };

    assert_eq!(
        error.to_string(),
        "processes of account 'acme' are still running after being killed"
    );
}
