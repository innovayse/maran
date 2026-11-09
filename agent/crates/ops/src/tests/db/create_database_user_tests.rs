//! What `create_database_user` creates, what it refuses, and what it never says.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::db::create_database_user::create_database_user;
use crate::db::db_error::DbError;
use crate::db::fake_privilege_host::{FakePrivilegeHost, reader_request};

#[test]
fn a_new_login_is_created_with_the_password_it_was_given() {
    let host = FakePrivilegeHost::new();

    create_database_user(&host, &reader_request()).expect("created");

    assert_eq!(host.users(), vec!["alice_reader".to_owned()]);
    assert_eq!(
        host.password_of("alice_reader"),
        Some("Gen3rated-pw".to_owned())
    );
}

#[test]
fn a_new_login_is_local_only_and_holds_no_privilege() {
    let host = FakePrivilegeHost::new();

    create_database_user(&host, &reader_request()).expect("created");

    assert_eq!(
        host.changing_statements(),
        vec!["CREATE USER 'alice_reader'@'localhost' IDENTIFIED BY 'Gen3rated-pw'".to_owned()]
    );
    assert!(host.databases_granted_to("alice_reader").is_empty());
}

#[test]
fn a_login_that_is_already_there_is_reported_and_its_password_is_left_alone() {
    let host = FakePrivilegeHost::new();
    create_database_user(&host, &reader_request()).expect("created");
    let mut again = reader_request();
    again.password =
        maran_agent_core::validation::secrets::password::Password::parse("Another-pw1").unwrap();

    let failure = create_database_user(&host, &again).expect_err("must fail");

    assert!(matches!(failure, DbError::AlreadyExists));
    assert_eq!(
        host.password_of("alice_reader"),
        Some("Gen3rated-pw".to_owned())
    );
}

#[test]
fn an_existing_login_is_refused_before_any_create_is_sent() {
    let host = FakePrivilegeHost::new();
    host.add_user("alice_reader");

    create_database_user(&host, &reader_request()).expect_err("must fail");

    assert!(host.changing_statements().is_empty());
}

#[test]
fn losing_the_race_to_another_writer_is_still_reported_as_already_existing() {
    let host = FakePrivilegeHost::new();
    host.refuse_statements_starting_with("CREATE USER", 1, "ERROR 1396 (HY000): exists");

    let failure = create_database_user(&host, &reader_request()).expect_err("must fail");

    assert!(matches!(failure, DbError::AlreadyExists));
}

#[test]
fn a_refused_connection_is_access_denied() {
    let host = FakePrivilegeHost::new();
    host.refuse_statements_starting_with("SELECT", 1, "ERROR 1045 (28000): Access denied");

    let failure = create_database_user(&host, &reader_request()).expect_err("must fail");

    assert!(matches!(failure, DbError::AccessDenied));
}

#[test]
fn another_refusal_carries_its_number_and_never_the_password() {
    let host = FakePrivilegeHost::new();
    host.refuse_statements_starting_with(
        "CREATE USER",
        1,
        "ERROR 1470 (HY000): String 'Gen3rated-pw' is too long",
    );

    let failure = create_database_user(&host, &reader_request()).expect_err("must fail");

    assert!(matches!(failure, DbError::ClientFailed { code: 1470 }));
    assert!(!format!("{failure:?} {failure}").contains("Gen3rated-pw"));
}

#[test]
fn an_existence_answer_that_is_not_a_number_is_unparsable_and_nothing_is_created() {
    let host = FakePrivilegeHost::new();
    host.print_for_statements_starting_with("SELECT COUNT", "many");

    let failure = create_database_user(&host, &reader_request()).expect_err("must fail");

    assert!(matches!(failure, DbError::Unparsable));
    assert!(host.changing_statements().is_empty());
}
