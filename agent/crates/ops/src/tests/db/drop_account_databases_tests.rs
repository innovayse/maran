//! What `drop_account_databases` takes away, and — the half that matters —
//! everything belonging to a neighbouring account that it leaves alone.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;

use crate::db::create_database::create_database;
use crate::db::db_error::DbError;
use crate::db::drop_account_databases::drop_account_databases;
use crate::db::fake_db_host::{FakeDbHost, account};
use crate::db::model::create_database_request::CreateDatabaseRequest;

/// The password every pair in this file is created with.
const PASSWORD: &str = "Gen3rated-pw";

/// Creates `owner`'s `<database>` database with its `<user>` user on `host`.
fn create(host: &FakeDbHost, owner: &str, database: &str, user: &str) {
    let owner = AccountName::parse(owner).expect("a valid account name");

    create_database(
        host,
        &CreateDatabaseRequest {
            database: DatabaseName::for_account(&owner, database).expect("a valid database name"),
            user: DbUserName::for_account(&owner, user).expect("a valid user name"),
            password: Password::parse(PASSWORD).expect("a valid password"),
        },
    )
    .expect("created");
}

/// Both the databases and their users go, all of them.
#[test]
fn dropping_an_accounts_databases_removes_every_database_and_every_user_it_owns() {
    let host = FakeDbHost::new();
    create(&host, "alice", "shop", "shopuser");
    create(&host, "alice", "blog", "bloguser");

    drop_account_databases(&host, &account()).expect("dropped");

    assert!(host.databases().is_empty());
    assert!(host.users().is_empty());
}

/// A database whose user the panel never recorded still goes.
#[test]
fn a_database_whose_pairing_the_panel_has_forgotten_is_still_dropped() {
    // The reason this operation is not a loop over `drop_database`: that one
    // needs the database AND the user it was created with, a pairing that
    // exists only in the panel's rows. An orphan the panel has forgotten is
    // exactly what an account deletion has to clean up, and a cascade driven
    // by the panel's list would leave it behind.
    let host = FakeDbHost::with_existing_many(&["alice_orphan"]);

    drop_account_databases(&host, &account()).expect("dropped");

    assert!(host.databases().is_empty());
}

/// A neighbouring account whose name starts with this one's is untouched.
#[test]
fn an_account_whose_name_this_one_is_a_prefix_of_keeps_its_databases_and_users() {
    // `alice_` is a prefix of `alice_bob_shop`, which is account `alice_bob`'s.
    // A prefix scan would drop another tenant's data and revoke their
    // credential; the decode splits at the LAST separator instead.
    let host = FakeDbHost::new();
    create(&host, "alice", "shop", "shopuser");
    create(&host, "alice_bob", "shop", "shopuser");

    drop_account_databases(&host, &account()).expect("dropped");

    assert_eq!(host.databases(), vec!["alice_bob_shop".to_owned()]);
    assert_eq!(host.users(), vec!["alice_bob_shopuser".to_owned()]);
}

/// The server's own databases and users are never named at all.
#[test]
fn the_servers_own_databases_and_users_are_left_alone() {
    let host = FakeDbHost::with_existing_many(&["information_schema", "mysql", "alice_shop"]);

    drop_account_databases(&host, &account()).expect("dropped");

    assert_eq!(
        host.databases(),
        vec!["information_schema".to_owned(), "mysql".to_owned()]
    );
}

/// Every database goes before any user does.
#[test]
fn every_database_is_dropped_before_any_user_is() {
    let host = FakeDbHost::new();
    create(&host, "alice", "shop", "shopuser");

    drop_account_databases(&host, &account()).expect("dropped");

    let statements = host.statements();
    let last_database = statements
        .iter()
        .rposition(|statement| statement.starts_with("DROP DATABASE "))
        .expect("a database was dropped");
    let first_user = statements
        .iter()
        .position(|statement| statement.starts_with("DROP USER "))
        .expect("a user was dropped");
    assert!(last_database < first_user);
}

/// The user drop is conditional, so an interrupted previous attempt converges.
#[test]
fn the_user_drop_is_conditional_so_a_half_finished_previous_attempt_converges() {
    let host = FakeDbHost::new();
    create(&host, "alice", "shop", "shopuser");

    drop_account_databases(&host, &account()).expect("dropped");

    let drop = host
        .statements()
        .into_iter()
        .find(|statement| statement.starts_with("DROP USER "))
        .expect("a user was dropped");
    assert_eq!(drop, "DROP USER IF EXISTS 'alice_shopuser'@'localhost'");
}

/// An account with nothing on the server sends no DDL and succeeds.
#[test]
fn an_account_with_nothing_on_the_server_sends_no_statement_that_changes_anything() {
    let host = FakeDbHost::new();

    drop_account_databases(&host, &account()).expect("dropped");

    assert!(
        !host
            .statements()
            .iter()
            .any(|statement| { statement.starts_with("DROP ") }),
        "nothing was there, so nothing should have been changed: {:?}",
        host.statements()
    );
}

/// A server that refuses is reported rather than shrugged off.
#[test]
fn a_server_that_refuses_the_listing_fails_the_whole_removal() {
    // Reported and not swallowed, because the caller is about to run `userdel`:
    // a cascade that reported success on a listing it never got would delete
    // the account and leave every database it could not see.
    let host = FakeDbHost::failing_with(2013, "Lost connection to server");

    let failure = drop_account_databases(&host, &account()).expect_err("must fail");

    assert!(matches!(failure, DbError::ClientFailed { code: 2013 }));
}
