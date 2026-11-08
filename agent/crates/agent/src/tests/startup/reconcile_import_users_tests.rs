//! Tests for `reconcile_import_users`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::File;
use std::sync::Mutex;

use maran_agent_core::secret_string::SecretString;
use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_ops::backup::{BackupError, ImportUser, RestrictedDumpHost};

use super::reconcile_import_users;

/// A database server holding a fixed list of user names.
struct FakeServer {
    /// Every user name the server holds, temporary or not.
    users: Vec<&'static str>,
    /// Whether the listing fails.
    fail_listing: bool,
    /// The names a drop was asked for.
    dropped: Mutex<Vec<String>>,
}

impl FakeServer {
    /// A server holding `users`.
    fn holding(users: Vec<&'static str>, fail_listing: bool) -> Self {
        Self {
            users,
            fail_listing,
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
        panic!("a startup pass never creates a user");
    }

    fn load_dump_as(&self, _: &ImportUser, _: &SecretString, _: File) -> Result<(), BackupError> {
        panic!("a startup pass never loads a dump");
    }

    fn drop_import_user(&self, user: &ImportUser) -> Result<(), BackupError> {
        self.dropped.lock().unwrap().push(user.name().to_owned());

        Ok(())
    }

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

#[test]
fn a_leftover_import_user_is_dropped_at_start() {
    let server = FakeServer::holding(vec!["root", "maran_imp_00010aff10203040"], false);

    assert_eq!(reconcile_import_users(&server), Some(1));
    assert_eq!(
        server.dropped(),
        vec!["maran_imp_00010aff10203040".to_owned()]
    );
}

#[test]
fn a_user_that_is_not_a_temporary_import_user_is_left_alone() {
    let server = FakeServer::holding(
        vec!["root", "alice_shop", "maran_imp_shop", "maran_imp_"],
        false,
    );

    assert_eq!(reconcile_import_users(&server), Some(0));
    assert!(server.dropped().is_empty());
}

#[test]
fn a_failing_listing_removes_nothing_and_does_not_refuse_the_start() {
    let server = FakeServer::holding(vec!["maran_imp_00010aff10203040"], true);

    assert_eq!(reconcile_import_users(&server), None);
    assert!(server.dropped().is_empty());
}
