//! What `drop_database` takes away, in which order, and what a repeat answers.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::db::create_database::create_database;
use crate::db::db_error::DbError;
use crate::db::drop_database::drop_database;
use crate::db::fake_db_host::{FakeDbHost, shop_database, shop_request, shop_user};

/// A host that already holds `alice`'s `shop` database and its user.
fn host_with_shop() -> FakeDbHost {
    let host = FakeDbHost::new();
    create_database(&host, &shop_request()).expect("created");

    host
}

/// Both the database and its dedicated user go.
#[test]
fn dropping_removes_the_database_and_its_dedicated_user() {
    let host = host_with_shop();

    drop_database(&host, &shop_database(), &shop_user()).expect("dropped");

    assert!(host.databases().is_empty());
    assert!(host.users().is_empty());
}

/// A second drop converges on `NotFound` instead of failing.
#[test]
fn dropping_a_database_twice_reports_not_found_rather_than_failing() {
    let host = host_with_shop();

    drop_database(&host, &shop_database(), &shop_user()).expect("dropped");
    let second = drop_database(&host, &shop_database(), &shop_user());

    assert!(matches!(second, Err(DbError::NotFound)));
}

/// Dropping something that was never there is `NotFound`, and nothing is sent.
#[test]
fn dropping_a_database_that_was_never_there_sends_no_drop_statement() {
    let host = FakeDbHost::new();

    let failure = drop_database(&host, &shop_database(), &shop_user()).expect_err("must fail");

    assert!(matches!(failure, DbError::NotFound));
    assert!(
        !host
            .statements()
            .iter()
            .any(|statement| statement.starts_with("DROP ")),
        "a drop must not be attempted for a database that is not there"
    );
}

/// The database goes before its user, so nothing is left unreachable.
#[test]
fn the_database_is_dropped_before_the_user_that_owned_it() {
    let host = host_with_shop();

    drop_database(&host, &shop_database(), &shop_user()).expect("dropped");

    let statements = host.statements();
    // The other order leaves the database present and unreachable for the
    // moment between the two statements — and permanently if the process dies
    // in between, since a retry then finds no user to key the cleanup on.
    let database_dropped = statements
        .iter()
        .position(|statement| statement.starts_with("DROP DATABASE "))
        .expect("the database was dropped");
    let user_dropped = statements
        .iter()
        .position(|statement| statement.starts_with("DROP USER "))
        .expect("the user was dropped");
    assert!(database_dropped < user_dropped);
}

/// Only the prefixed name reaches the server.
#[test]
fn the_drop_names_the_prefixed_database_and_never_a_bare_one() {
    let host = host_with_shop();

    drop_database(&host, &shop_database(), &shop_user()).expect("dropped");

    let drop = host
        .statements()
        .into_iter()
        .find(|statement| statement.starts_with("DROP DATABASE "))
        .expect("a drop was sent");
    assert_eq!(drop, "DROP DATABASE `alice_shop`");
}

/// A server that says the database is unknown is answered as `NotFound`, not as
/// an unexplained client failure.
#[test]
fn a_server_that_refuses_because_the_database_is_unknown_reports_not_found() {
    let host = FakeDbHost::failing_with(1049, "Unknown database 'alice_shop'");

    let failure = drop_database(&host, &shop_database(), &shop_user()).expect_err("must fail");

    assert!(matches!(failure, DbError::NotFound));
}
