//! What `drop_database_user` removes and what it refuses.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::db::database_privilege::DatabasePrivilege;

use crate::db::db_error::DbError;
use crate::db::drop_database_user::drop_database_user;
use crate::db::fake_privilege_host::{FakePrivilegeHost, reader};

#[test]
fn dropping_a_login_removes_it_and_every_privilege_it_held() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    host.plant_row(
        "alice_reader",
        "alice\\_shop",
        &[DatabasePrivilege::Select],
        false,
    );
    host.plant_row(
        "alice_reader",
        "alice\\_blog",
        &[DatabasePrivilege::Insert],
        false,
    );

    drop_database_user(&host, &reader()).expect("dropped");

    assert!(host.users().is_empty());
    assert!(host.databases_granted_to("alice_reader").is_empty());
}

#[test]
fn only_the_named_login_is_dropped() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    host.add_user("alice_writer");
    host.plant_row(
        "alice_writer",
        "alice\\_shop",
        &[DatabasePrivilege::Update],
        false,
    );

    drop_database_user(&host, &reader()).expect("dropped");

    assert_eq!(host.users(), vec!["alice_writer".to_owned()]);
    assert_eq!(host.databases_granted_to("alice_writer").len(), 1);
}

#[test]
fn the_statement_names_the_local_account_and_nothing_wider() {
    let host = FakePrivilegeHost::with_reader_and_shop();

    drop_database_user(&host, &reader()).expect("dropped");

    assert_eq!(
        host.changing_statements(),
        vec!["DROP USER 'alice_reader'@'localhost'".to_owned()]
    );
}

#[test]
fn a_login_that_is_not_there_is_not_found_and_nothing_is_dropped() {
    let host = FakePrivilegeHost::new();

    let failure = drop_database_user(&host, &reader()).expect_err("must fail");

    assert!(matches!(failure, DbError::NotFound));
    assert!(host.changing_statements().is_empty());
}

#[test]
fn a_second_drop_reports_not_found() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    drop_database_user(&host, &reader()).expect("dropped");

    let failure = drop_database_user(&host, &reader()).expect_err("must fail");

    assert!(matches!(failure, DbError::NotFound));
}

#[test]
fn a_refusal_by_the_server_carries_its_number() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    host.refuse_statements_starting_with("DROP USER", 1, "ERROR 1227 (42000): denied");

    let failure = drop_database_user(&host, &reader()).expect_err("must fail");

    assert!(matches!(failure, DbError::ClientFailed { code: 1227 }));
}
