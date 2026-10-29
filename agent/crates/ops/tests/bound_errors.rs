//! A stopped program reaches every area's error as that area's `Bound`
//! variant, and never as a verdict.
//!
//! The area error enums are the surface the rpc layer matches on, so this is
//! an integration test of the crate's public API: one test per property, each
//! looping over every area that wraps the core's `BoundExceeded`. The
//! per-area mapping from a failed spawn is covered beside each host; what is
//! pinned here is the part every area shares and a new area could forget.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fmt::{Debug, Display};
use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::output_stream::OutputStream;
use maran_agent_core::utils::spawn_error::SpawnError;
use maran_ops::accounts::AccountError;
use maran_ops::backup::BackupError;
use maran_ops::cron::CronError;
use maran_ops::db::DbError;
use maran_ops::firewall::FirewallError;
use maran_ops::ftps::FtpsError;
use maran_ops::logins::LoginsError;
use maran_ops::monitor::MonitorError;
use maran_ops::php::PhpOpError;
use maran_ops::safe_write::SafeWriteError;
use maran_ops::sftp::SftpError;
use maran_ops::sites::SitesOpError;
use maran_ops::ssl::SslOpError;

/// The program path every bound in this file names.
const PROGRAM: &str = "/usr/sbin/nft";

/// A deadline bound that names [`PROGRAM`].
fn timed_out() -> BoundExceeded {
    BoundExceeded::TimedOut {
        program: PROGRAM.to_string(),
        deadline: Duration::from_secs(60),
    }
}

/// An output-cap bound that names [`PROGRAM`].
fn output_too_large() -> BoundExceeded {
    BoundExceeded::OutputTooLarge {
        program: PROGRAM.to_string(),
        stream: OutputStream::Stdout,
        cap: 1024,
    }
}

/// Both bounds, so that no test passes for one variant only.
fn both_bounds() -> [BoundExceeded; 2] {
    [timed_out(), output_too_large()]
}

/// Converts each bound into the area's error and checks it landed on the
/// area's `Bound` variant with the core's own wording intact.
fn assert_bound_is_forwarded<E>(area: &str, is_bound: fn(&E) -> bool)
where
    E: From<BoundExceeded> + Display + Debug,
{
    for bound in both_bounds() {
        let wording = bound.to_string();
        let error = E::from(bound);

        assert!(
            is_bound(&error),
            "{area}: a bound must stay a bound: {error:?}"
        );
        assert_eq!(
            error.to_string(),
            wording,
            "{area}: the area must not reword a bound"
        );
    }
}

/// Every area that runs programs lists the bound as its own variant.
#[test]
fn every_area_lists_a_bound_as_its_own_variant() {
    assert_bound_is_forwarded::<AccountError>("accounts", |e| matches!(e, AccountError::Bound(_)));
    assert_bound_is_forwarded::<BackupError>("backup", |e| matches!(e, BackupError::Bound(_)));
    assert_bound_is_forwarded::<CronError>("cron", |e| matches!(e, CronError::Bound(_)));
    assert_bound_is_forwarded::<DbError>("db", |e| matches!(e, DbError::Bound(_)));
    assert_bound_is_forwarded::<FirewallError>("firewall", |e| {
        matches!(e, FirewallError::Bound(_))
    });
    assert_bound_is_forwarded::<FtpsError>("ftps", |e| matches!(e, FtpsError::Bound(_)));
    assert_bound_is_forwarded::<LoginsError>("logins", |e| matches!(e, LoginsError::Bound(_)));
    assert_bound_is_forwarded::<MonitorError>("monitor", |e| matches!(e, MonitorError::Bound(_)));
    assert_bound_is_forwarded::<PhpOpError>("php", |e| matches!(e, PhpOpError::Bound(_)));
    assert_bound_is_forwarded::<SafeWriteError>("safe_write", |e| {
        matches!(e, SafeWriteError::Bound(_))
    });
    assert_bound_is_forwarded::<SftpError>("sftp", |e| matches!(e, SftpError::Bound(_)));
    assert_bound_is_forwarded::<SitesOpError>("sites", |e| matches!(e, SitesOpError::Bound(_)));
    assert_bound_is_forwarded::<SslOpError>("ssl", |e| matches!(e, SslOpError::Bound(_)));
}

/// A bound's text names the program, which is what the panel's operator reads.
#[test]
fn a_bound_names_the_program_it_stopped() {
    for bound in both_bounds() {
        assert!(
            FirewallError::from(bound.clone())
                .to_string()
                .contains(PROGRAM),
            "the text must name the program: {bound}"
        );
    }
}

/// A deadline names the deadline and an output cap names the stream and the cap.
#[test]
fn a_bound_names_the_limit_it_hit() {
    let deadline = SftpError::from(timed_out()).to_string();
    let cap = SftpError::from(output_too_large()).to_string();

    assert!(deadline.contains("60s"), "{deadline}");
    assert!(cap.contains("1024"), "{cap}");
    assert!(cap.contains("standard output"), "{cap}");
    // The two bounds are different events and must not read alike.
    assert_ne!(deadline, cap);
}

/// The kill does not cancel work handed to somebody else, and the text says so,
/// so that no caller reports a rollback it did not perform.
#[test]
fn a_deadline_says_the_work_may_still_run() {
    let text = DbError::from(timed_out()).to_string();

    assert!(text.contains("may still run"), "{text}");
}

/// Positive control for the loop above: a variant that is not a bound is
/// reported as not a bound, so the closures do not accept everything.
#[test]
fn a_variant_that_is_not_a_bound_is_not_reported_as_one() {
    assert!(!matches!(
        MonitorError::HostStatisticsUnavailable,
        MonitorError::Bound(_)
    ));
    assert!(!matches!(
        DbError::ClientKilled { signal: 9 },
        DbError::Bound(_)
    ));
}

/// The areas that convert a failed spawn send a bound to `Bound`.
#[test]
fn a_bound_spawn_error_becomes_the_areas_bound() {
    for bound in both_bounds() {
        assert!(matches!(
            FtpsError::from(SpawnError::Bound(bound.clone())),
            FtpsError::Bound(_)
        ));
        assert!(matches!(
            LoginsError::from(SpawnError::Bound(bound.clone())),
            LoginsError::Bound(_)
        ));
        assert!(matches!(
            SftpError::from(SpawnError::Bound(bound.clone())),
            SftpError::Bound(_)
        ));
        assert!(matches!(
            SslOpError::from(SpawnError::Bound(bound)),
            SslOpError::Bound(_)
        ));
    }
}

/// Inverse control: a program that never started is not a bound.
#[test]
fn a_spawn_that_never_started_is_not_a_bound() {
    let start = || SpawnError::Start {
        program: PROGRAM.to_string(),
        reason: "No such file or directory".to_string(),
    };

    assert!(!matches!(FtpsError::from(start()), FtpsError::Bound(_)));
    assert!(!matches!(LoginsError::from(start()), LoginsError::Bound(_)));
    assert!(!matches!(SftpError::from(start()), SftpError::Bound(_)));
    assert!(!matches!(SslOpError::from(start()), SslOpError::Bound(_)));
}

/// A bound that crosses an area boundary stays a bound.
#[test]
fn a_bound_stays_a_bound_when_it_crosses_an_area_boundary() {
    assert!(matches!(
        AccountError::from(PhpOpError::Bound(timed_out())),
        AccountError::Bound(_)
    ));
    assert!(matches!(
        AccountError::from(DbError::Bound(timed_out())),
        AccountError::Bound(_)
    ));
    assert!(matches!(
        AccountError::from(SftpError::Bound(timed_out())),
        AccountError::Bound(_)
    ));
    assert!(matches!(
        AccountError::from(FtpsError::Bound(timed_out())),
        AccountError::Bound(_)
    ));
    assert!(matches!(
        AccountError::from(SitesOpError::Bound(timed_out())),
        AccountError::Bound(_)
    ));
    assert!(matches!(
        SitesOpError::from(PhpOpError::Bound(timed_out())),
        SitesOpError::Bound(_)
    ));
    assert!(matches!(
        SitesOpError::from(SafeWriteError::Bound(timed_out())),
        SitesOpError::Bound(_)
    ));
    assert!(matches!(
        PhpOpError::from(SafeWriteError::Bound(timed_out())),
        PhpOpError::Bound(_)
    ));
    assert!(matches!(
        SslOpError::from(SitesOpError::Bound(timed_out())),
        SslOpError::Bound(_)
    ));
    assert!(matches!(
        SslOpError::from(SafeWriteError::Bound(timed_out())),
        SslOpError::Bound(_)
    ));
}

/// Inverse control: the account area still flattens an ordinary pool failure,
/// so the exception for a bound is an exception and not the rule.
#[test]
fn an_ordinary_failure_crossing_an_area_boundary_is_still_flattened() {
    let flattened = AccountError::from(PhpOpError::AccountBusy {
        username: "alice".to_string(),
    });

    assert!(matches!(flattened, AccountError::PoolRemoval { .. }));
    assert!(!matches!(
        PhpOpError::from(SafeWriteError::ValidationFailed {
            stderr: "bad".to_string()
        }),
        PhpOpError::Bound(_)
    ));
}
