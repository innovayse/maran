//! The preflight that names which of the three backup binaries is missing,
//! and where it looked.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::HashSet;

use maran_distro::{DistroFamily, adapter_for};

use super::*;

/// A lookup a test can steer: reports every path executable except the ones
/// named as absent.
struct FakeLookup {
    absent: HashSet<&'static str>,
}

impl ExecutableLookup for FakeLookup {
    fn is_executable(&self, path: &str) -> bool {
        !self.absent.contains(path)
    }
}

/// The Debian adapter's own literals, real and fixed — no fake adapter is
/// needed, the same convention `create_backup_tests.rs`'s `distro()` uses.
fn distro() -> &'static dyn maran_distro::DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// **The inverse control.** Every binary reported present is accepted.
#[test]
fn every_binary_present_is_accepted() {
    let lookup = FakeLookup {
        absent: HashSet::new(),
    };

    verify_backup_binaries(distro(), &lookup).expect("nothing is missing");
}

/// A missing `tar` is reported by name and by the exact path the adapter
/// declared — not a generic "a required program is missing".
#[test]
fn a_missing_tar_names_the_program_and_its_path() {
    let path = distro().tar_binary();
    let lookup = FakeLookup {
        absent: HashSet::from([path]),
    };

    let error = verify_backup_binaries(distro(), &lookup).expect_err("tar is reported absent");

    assert_eq!(
        error,
        BackupError::BackupBinaryMissing {
            program: "tar".to_string(),
            path: path.to_string(),
        }
    );
}

/// A missing `gzip` names `gzip`, not `tar` — the diagnostic must say WHICH of
/// the three programs is absent, not merely that one is.
#[test]
fn a_missing_gzip_names_gzip_and_its_path() {
    let path = distro().gzip_binary();
    let lookup = FakeLookup {
        absent: HashSet::from([path]),
    };

    let error = verify_backup_binaries(distro(), &lookup).expect_err("gzip is reported absent");

    assert_eq!(
        error,
        BackupError::BackupBinaryMissing {
            program: "gzip".to_string(),
            path: path.to_string(),
        }
    );
}

/// A missing database dump client — the exact failure Q5 asks about: a
/// package drops the `mysqldump` compatibility symlink, or renames
/// `mariadb-dump` outright, and this is the assertion that must go red before
/// a scheduled backup finds it at 03:00.
#[test]
fn a_missing_database_dump_client_names_it_and_its_path() {
    let path = distro().database_dump_binary();
    let lookup = FakeLookup {
        absent: HashSet::from([path]),
    };

    let error =
        verify_backup_binaries(distro(), &lookup).expect_err("the dump client is reported absent");

    assert_eq!(
        error,
        BackupError::BackupBinaryMissing {
            program: "database dump client".to_string(),
            path: path.to_string(),
        }
    );
}
