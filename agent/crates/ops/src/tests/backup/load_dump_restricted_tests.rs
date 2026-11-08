//! The restricted loader creates a one-database user, loads as it, and always drops it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::{Read as _, Write as _};
use std::sync::Mutex;

use maran_agent_core::secret_string::SecretString;
use maran_agent_core::validation::system::name::AccountName;

use crate::backup::model::dump_refusal::DumpRefusal;

use super::*;

/// What the fake host was asked, in order, and what it saw.
#[derive(Default)]
struct Seen {
    /// `create`, `load` and `drop`, in the order they were called.
    calls: Vec<&'static str>,
    /// The user name every call was made for.
    users: Vec<String>,
    /// The length of the password the user was created and loaded with.
    password_lengths: Vec<usize>,
    /// The database the user was granted.
    database: Option<String>,
    /// The bytes the load was handed.
    loaded: Vec<u8>,
}

/// A host that records its calls and fails the steps it is told to.
#[derive(Default)]
struct FakeHost {
    /// What was asked.
    seen: Mutex<Seen>,
    /// Whether the creation fails.
    fail_create: bool,
    /// Whether the load fails.
    fail_load: bool,
    /// Whether the drop fails.
    fail_drop: bool,
}

impl RestrictedDumpHost for FakeHost {
    fn create_import_user(
        &self,
        user: &ImportUser,
        password: &SecretString,
        database: &DatabaseName,
    ) -> Result<(), BackupError> {
        let mut seen = self.seen.lock().unwrap();
        seen.calls.push("create");
        seen.users.push(user.name().to_owned());
        seen.password_lengths.push(password.expose().len());
        seen.database = Some(database.as_str().to_owned());
        if self.fail_create {
            return Err(BackupError::LoadFailed { status: 3 });
        }

        Ok(())
    }

    fn load_dump_as(
        &self,
        user: &ImportUser,
        password: &SecretString,
        mut dump: File,
    ) -> Result<(), BackupError> {
        let mut seen = self.seen.lock().unwrap();
        seen.calls.push("load");
        seen.users.push(user.name().to_owned());
        seen.password_lengths.push(password.expose().len());
        dump.read_to_end(&mut seen.loaded).unwrap();
        if self.fail_load {
            return Err(BackupError::LoadFailed { status: 1 });
        }

        Ok(())
    }

    fn drop_import_user(&self, user: &ImportUser) -> Result<(), BackupError> {
        let mut seen = self.seen.lock().unwrap();
        seen.calls.push("drop");
        seen.users.push(user.name().to_owned());
        if self.fail_drop {
            return Err(BackupError::DropFailed { status: 1 });
        }

        Ok(())
    }

    fn leftover_import_users(&self) -> Result<Vec<ImportUser>, BackupError> {
        Ok(Vec::new())
    }
}

/// The database every fixture dump is for.
fn database() -> DatabaseName {
    let account = AccountName::parse("alice").expect("the fixture name is valid");

    DatabaseName::for_account(&account, "shop").expect("the fixture name is valid")
}

/// An open file holding `text`, left at its END, the way a caller that has just
/// written it would leave it.
fn dump_file(text: &str) -> File {
    let mut file = tempfile::tempfile().unwrap();
    file.write_all(text.as_bytes()).unwrap();

    file
}

/// A dump that is clean.
const CLEAN: &str = "USE `alice_shop`;\nCREATE TABLE `t` (`a` int);\nINSERT INTO `t` VALUES (1);\n";

#[test]
fn a_clean_dump_is_loaded_in_full_by_a_user_that_is_created_first_and_dropped_last() {
    let host = FakeHost::default();

    load_dump_restricted(&host, &database(), dump_file(CLEAN)).unwrap();

    let seen = host.seen.lock().unwrap();
    assert_eq!(seen.calls, ["create", "load", "drop"]);
    assert_eq!(seen.loaded, CLEAN.as_bytes());
    assert_eq!(seen.database.as_deref(), Some("alice_shop"));
}

#[test]
fn every_step_is_made_for_the_same_user_and_it_is_a_temporary_one_with_a_long_password() {
    let host = FakeHost::default();

    load_dump_restricted(&host, &database(), dump_file(CLEAN)).unwrap();

    let seen = host.seen.lock().unwrap();
    assert_eq!(seen.users.len(), 3);
    assert!(seen.users.iter().all(|user| *user == seen.users[0]));
    assert!(seen.users[0].starts_with("maran_imp_"));
    assert_eq!(seen.password_lengths, [64, 64]);
}

#[test]
fn two_loads_use_two_different_users() {
    let host = FakeHost::default();

    load_dump_restricted(&host, &database(), dump_file(CLEAN)).unwrap();
    load_dump_restricted(&host, &database(), dump_file(CLEAN)).unwrap();

    let seen = host.seen.lock().unwrap();
    assert_ne!(seen.users[0], seen.users[3]);
}

#[test]
fn a_hostile_dump_is_refused_before_any_user_is_created() {
    let host = FakeHost::default();
    let hostile = "USE `alice_shop`;\nGRANT ALL PRIVILEGES ON *.* TO 'x'@'%';\n";

    let error = load_dump_restricted(&host, &database(), dump_file(hostile)).unwrap_err();

    assert_eq!(
        error,
        BackupError::RestrictedLoadRefused {
            reason: DumpRefusal::ForbiddenStatement
        }
    );
    assert!(host.seen.lock().unwrap().calls.is_empty());
}

#[test]
fn a_client_command_in_a_dump_is_refused_before_any_user_is_created() {
    let host = FakeHost::default();

    let error = load_dump_restricted(&host, &database(), dump_file("\\! id\n")).unwrap_err();

    assert_eq!(
        error,
        BackupError::RestrictedLoadRefused {
            reason: DumpRefusal::ClientCommand
        }
    );
    assert!(host.seen.lock().unwrap().calls.is_empty());
}

#[test]
fn the_user_is_dropped_when_the_load_fails() {
    let host = FakeHost {
        fail_load: true,
        ..FakeHost::default()
    };

    let error = load_dump_restricted(&host, &database(), dump_file(CLEAN)).unwrap_err();

    assert_eq!(error, BackupError::LoadFailed { status: 1 });
    assert_eq!(host.seen.lock().unwrap().calls, ["create", "load", "drop"]);
}

#[test]
fn the_user_is_dropped_when_its_creation_fails_halfway() {
    let host = FakeHost {
        fail_create: true,
        ..FakeHost::default()
    };

    let error = load_dump_restricted(&host, &database(), dump_file(CLEAN)).unwrap_err();

    assert_eq!(error, BackupError::LoadFailed { status: 3 });
    assert_eq!(host.seen.lock().unwrap().calls, ["create", "drop"]);
}

#[test]
fn a_user_that_could_not_be_dropped_after_a_good_load_is_an_error() {
    let host = FakeHost {
        fail_drop: true,
        ..FakeHost::default()
    };

    let error = load_dump_restricted(&host, &database(), dump_file(CLEAN)).unwrap_err();

    assert_eq!(error, BackupError::DropFailed { status: 1 });
}

#[test]
fn when_the_load_and_the_drop_both_fail_the_load_is_what_is_reported() {
    let host = FakeHost {
        fail_load: true,
        fail_drop: true,
        ..FakeHost::default()
    };

    let error = load_dump_restricted(&host, &database(), dump_file(CLEAN)).unwrap_err();

    assert_eq!(error, BackupError::LoadFailed { status: 1 });
}
