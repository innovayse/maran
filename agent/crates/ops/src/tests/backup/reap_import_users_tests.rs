//! The sweep of temporary import users an interrupted import left behind.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::File;
use std::sync::Mutex;

use maran_agent_core::secret_string::SecretString;
use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::model::import_user::ImportUser;

use super::*;

/// A server holding a fixed set of users, which records the drops it is asked for.
struct FakeServer {
    /// The names the server holds, in the shape it prints them: any user at all.
    users: Vec<String>,
    /// Whether listing fails.
    fail_listing: bool,
    /// The name whose drop fails, if any.
    fail_drop_of: Option<String>,
    /// Every name a drop was asked for, in order.
    dropped: Mutex<Vec<String>>,
}

impl FakeServer {
    /// A server holding `users`, with every call succeeding.
    fn holding(users: &[&str]) -> Self {
        Self {
            users: users.iter().map(|&name| name.to_owned()).collect(),
            fail_listing: false,
            fail_drop_of: None,
            dropped: Mutex::new(Vec::new()),
        }
    }

    /// The names a drop was asked for.
    fn dropped(&self) -> Vec<String> {
        self.dropped.lock().unwrap().clone()
    }
}

impl RestrictedDumpHost for FakeServer {
    fn create_import_user(
        &self,
        _: &ImportUser,
        _: &SecretString,
        _: &DatabaseName,
    ) -> Result<(), BackupError> {
        panic!("a sweep never creates a user");
    }

    fn load_dump_as(&self, _: &ImportUser, _: &SecretString, _: File) -> Result<(), BackupError> {
        panic!("a sweep never loads a dump");
    }

    fn drop_import_user(&self, user: &ImportUser) -> Result<(), BackupError> {
        self.dropped.lock().unwrap().push(user.name().to_owned());
        if self.fail_drop_of.as_deref() == Some(user.name()) {
            return Err(BackupError::DropFailed { status: 1 });
        }

        Ok(())
    }

    /// Answers what a real listing answers: the users of the exact shape.
    fn leftover_import_users(&self) -> Result<Vec<ImportUser>, BackupError> {
        if self.fail_listing {
            return Err(BackupError::ImportUsersUnlistable { status: 1 });
        }

        Ok(self
            .users
            .iter()
            .filter_map(|name| ImportUser::from_listed(name))
            .collect())
    }
}

/// A leftover user's name.
const LEFTOVER: &str = "maran_imp_00010aff10203040";

/// A second leftover user's name.
const OTHER_LEFTOVER: &str = "maran_imp_ffffffffffffffff";

#[test]
fn a_leftover_import_user_is_dropped() {
    let server = FakeServer::holding(&[LEFTOVER, OTHER_LEFTOVER]);

    let dropped = reap_import_users(&server).unwrap();

    assert_eq!(dropped, 2);
    assert_eq!(
        server.dropped(),
        vec![LEFTOVER.to_owned(), OTHER_LEFTOVER.to_owned()]
    );
}

#[test]
fn a_user_without_the_exact_prefix_shape_is_never_dropped() {
    let server = FakeServer::holding(&[
        "root",
        "alice_shop",
        "maran_imp_",
        "maran_imp_shop",
        "maran_impX00010aff10203040",
        "maran_imp_00010aff10203040_extra",
        LEFTOVER,
    ]);

    let dropped = reap_import_users(&server).unwrap();

    assert_eq!(dropped, 1);
    assert_eq!(server.dropped(), vec![LEFTOVER.to_owned()]);
}

#[test]
fn a_server_holding_no_leftover_drops_nothing() {
    let server = FakeServer::holding(&["root", "alice_shop"]);

    assert_eq!(reap_import_users(&server).unwrap(), 0);
    assert!(server.dropped().is_empty());
}

#[test]
fn a_failing_listing_drops_nothing_and_is_an_error() {
    let mut server = FakeServer::holding(&[LEFTOVER]);
    server.fail_listing = true;

    let error = reap_import_users(&server).unwrap_err();

    assert!(matches!(
        error,
        BackupError::ImportUsersUnlistable { status: 1 }
    ));
    assert!(server.dropped().is_empty());
}

#[test]
fn a_failed_drop_does_not_stop_the_other_leftovers_and_is_reported() {
    let mut server = FakeServer::holding(&[LEFTOVER, OTHER_LEFTOVER]);
    server.fail_drop_of = Some(LEFTOVER.to_owned());

    let error = reap_import_users(&server).unwrap_err();

    assert!(matches!(error, BackupError::DropFailed { status: 1 }));
    assert_eq!(
        server.dropped(),
        vec![LEFTOVER.to_owned(), OTHER_LEFTOVER.to_owned()]
    );
}
