//! What `set_database_user_privileges` sends, in which order, and what it refuses.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::db::database_privilege::DatabasePrivilege as P;
use maran_agent_core::validation::db::database_privilege_set::DatabasePrivilegeSet;

use crate::db::db_error::DbError;
use crate::db::fake_privilege_host::{FakePrivilegeHost, reader, shop};
use crate::db::model::database_privilege_change::DatabasePrivilegeChange;
use crate::db::set_database_user_privileges::set_database_user_privileges;

/// The stored form of `alice_shop`'s `Db` column.
const ESCAPED: &str = "alice\\_shop";

/// A change giving `alice_reader` exactly `privileges` on `alice_shop`.
fn change(privileges: &[P]) -> DatabasePrivilegeChange {
    DatabasePrivilegeChange {
        user: reader(),
        database: shop(),
        privileges: DatabasePrivilegeSet::of(privileges).unwrap(),
    }
}

#[test]
fn a_login_with_no_row_is_granted_the_wanted_set_on_the_escaped_pattern() {
    let host = FakePrivilegeHost::with_reader_and_shop();

    set_database_user_privileges(&host, &change(&[P::Update, P::Select])).expect("set");

    assert_eq!(
        host.changing_statements(),
        vec!["GRANT SELECT, UPDATE ON `alice\\_shop`.* TO 'alice_reader'@'localhost'".to_owned()]
    );
    assert_eq!(
        host.privileges_of("alice_reader", ESCAPED)
            .unwrap()
            .sql_list(),
        "SELECT, UPDATE"
    );
}

#[test]
fn every_statement_is_scoped_to_the_one_database_and_the_one_login() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    host.plant_row("alice_reader", ESCAPED, &[P::Select, P::Delete], true);

    set_database_user_privileges(&host, &change(&[P::Insert])).expect("set");

    for statement in host.changing_statements() {
        assert!(statement.contains("`alice\\_shop`.*"), "{statement}");
        assert!(
            statement.contains("'alice_reader'@'localhost'"),
            "{statement}"
        );
        assert!(!statement.contains("*.*"), "{statement}");
    }
}

#[test]
fn only_the_difference_is_sent_and_the_grant_comes_before_the_revoke() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    host.plant_row("alice_reader", ESCAPED, &[P::Select, P::Insert], false);

    set_database_user_privileges(&host, &change(&[P::Select, P::Update])).expect("set");

    assert_eq!(
        host.changing_statements(),
        vec![
            "GRANT UPDATE ON `alice\\_shop`.* TO 'alice_reader'@'localhost'".to_owned(),
            "REVOKE INSERT ON `alice\\_shop`.* FROM 'alice_reader'@'localhost'".to_owned(),
        ]
    );
    assert_eq!(
        host.privileges_of("alice_reader", ESCAPED)
            .unwrap()
            .sql_list(),
        "SELECT, UPDATE"
    );
}

#[test]
fn a_login_already_holding_exactly_the_set_is_left_alone() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    host.plant_row("alice_reader", ESCAPED, &[P::Select, P::Insert], false);

    set_database_user_privileges(&host, &change(&[P::Insert, P::Select])).expect("set");

    assert!(host.changing_statements().is_empty());
}

#[test]
fn a_second_identical_call_sends_nothing_beyond_the_reads() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    set_database_user_privileges(&host, &change(&[P::Select])).expect("first");
    let after_first = host.changing_statements().len();

    set_database_user_privileges(&host, &change(&[P::Select])).expect("second");

    assert_eq!(host.changing_statements().len(), after_first);
}

#[test]
fn an_empty_set_revokes_everything_the_login_holds_on_that_database() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    host.plant_row("alice_reader", ESCAPED, &[P::Select, P::Insert], false);

    set_database_user_privileges(&host, &change(&[])).expect("set");

    assert!(host.privileges_of("alice_reader", ESCAPED).is_none());
}

#[test]
fn an_empty_set_for_a_login_that_holds_nothing_is_a_no_op() {
    let host = FakePrivilegeHost::with_reader_and_shop();

    set_database_user_privileges(&host, &change(&[])).expect("set");

    assert!(host.changing_statements().is_empty());
}

#[test]
fn a_grant_option_found_on_the_row_is_revoked_whatever_was_asked() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    host.plant_row("alice_reader", ESCAPED, &[P::Select], true);

    set_database_user_privileges(&host, &change(&[P::Select])).expect("set");

    assert_eq!(
        host.changing_statements(),
        vec!["REVOKE GRANT OPTION ON `alice\\_shop`.* FROM 'alice_reader'@'localhost'".to_owned()]
    );
    assert_eq!(host.grant_option_of("alice_reader", ESCAPED), Some(false));
}

#[test]
fn no_statement_ever_grants_the_grant_option_or_everything() {
    let host = FakePrivilegeHost::with_reader_and_shop();

    set_database_user_privileges(&host, &change(&P::ALL)).expect("set");

    for statement in host.changing_statements() {
        assert!(!statement.contains("GRANT OPTION"), "{statement}");
        assert!(!statement.contains("ALL PRIVILEGES"), "{statement}");
    }
    assert_eq!(host.grant_option_of("alice_reader", ESCAPED), Some(false));
}

#[test]
fn a_wildcard_row_an_older_agent_left_is_removed_after_the_escaped_grant() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    host.plant_row("alice_reader", "alice_shop", &[P::Select, P::Drop], false);

    set_database_user_privileges(&host, &change(&[P::Select])).expect("set");

    assert_eq!(
        host.changing_statements(),
        vec![
            "GRANT SELECT ON `alice\\_shop`.* TO 'alice_reader'@'localhost'".to_owned(),
            "REVOKE GRANT OPTION ON `alice_shop`.* FROM 'alice_reader'@'localhost'".to_owned(),
            "REVOKE ALL PRIVILEGES ON `alice_shop`.* FROM 'alice_reader'@'localhost'".to_owned(),
        ]
    );
    assert_eq!(
        host.databases_granted_to("alice_reader"),
        vec![ESCAPED.to_owned()]
    );
}

#[test]
fn a_row_for_another_database_is_neither_read_nor_touched() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    host.plant_row("alice_reader", "alice\\_blog", &[P::Delete, P::Drop], true);

    set_database_user_privileges(&host, &change(&[P::Select])).expect("set");

    assert_eq!(
        host.privileges_of("alice_reader", "alice\\_blog")
            .unwrap()
            .sql_list(),
        "DELETE, DROP"
    );
    assert_eq!(
        host.grant_option_of("alice_reader", "alice\\_blog"),
        Some(true)
    );
}

#[test]
fn a_row_of_another_login_on_the_same_database_is_not_touched() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    host.add_user("alice_writer");
    host.plant_row("alice_writer", ESCAPED, &[P::Update], false);

    set_database_user_privileges(&host, &change(&[P::Select])).expect("set");

    assert_eq!(
        host.privileges_of("alice_writer", ESCAPED)
            .unwrap()
            .sql_list(),
        "UPDATE"
    );
}

#[test]
fn a_login_that_is_not_there_is_not_found_and_nothing_is_granted() {
    let host = FakePrivilegeHost::new();
    host.add_database("alice_shop");

    let failure = set_database_user_privileges(&host, &change(&[P::Select])).expect_err("fail");

    assert!(matches!(failure, DbError::NotFound));
    assert!(host.changing_statements().is_empty());
}

#[test]
fn a_database_that_is_not_there_is_not_found_and_nothing_is_granted() {
    let host = FakePrivilegeHost::new();
    host.add_user("alice_reader");

    let failure = set_database_user_privileges(&host, &change(&[P::Select])).expect_err("fail");

    assert!(matches!(failure, DbError::NotFound));
    assert!(host.changing_statements().is_empty());
}

#[test]
fn a_neighbouring_database_matched_by_the_unescaped_underscore_is_not_this_one() {
    // `alicexshop` is what the unescaped pattern `alice_shop` would also match.
    let host = FakePrivilegeHost::with_reader_and_shop();
    host.add_database("alicexshop");
    host.plant_row("alice_reader", "alicexshop", &[P::Drop], false);

    set_database_user_privileges(&host, &change(&[P::Select])).expect("set");

    assert_eq!(
        host.privileges_of("alice_reader", "alicexshop")
            .unwrap()
            .sql_list(),
        "DROP"
    );
}

#[test]
fn a_flag_that_is_neither_yes_nor_no_is_unparsable_and_nothing_is_sent() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    let flags = vec!["Maybe"; 19].join("\t");
    host.print_for_statements_starting_with("SELECT Db", &format!("alice\\_shop\t{flags}"));

    let failure = set_database_user_privileges(&host, &change(&[P::Insert])).expect_err("fail");

    assert!(matches!(failure, DbError::Unparsable));
    assert!(host.changing_statements().is_empty());
}

#[test]
fn a_row_with_the_wrong_number_of_fields_is_unparsable_and_nothing_is_sent() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    host.print_for_statements_starting_with("SELECT Db", "alice\\_shop\tY\tY");

    let failure = set_database_user_privileges(&host, &change(&[P::Insert])).expect_err("fail");

    assert!(matches!(failure, DbError::Unparsable));
    assert!(host.changing_statements().is_empty());
}

#[test]
fn a_flag_row_printed_in_the_clients_batch_escaping_is_read_back_correctly() {
    // The control for the two refusals above: a well-formed row, with the
    // backslash doubled as the real client prints it, is read as the escaped
    // pattern and leaves nothing to change.
    let host = FakePrivilegeHost::with_reader_and_shop();
    let none = vec!["N"; 17].join("\t");
    host.print_for_statements_starting_with("SELECT Db", &format!("alice\\\\_shop\tN\tY\t{none}"));

    set_database_user_privileges(&host, &change(&[P::Select])).expect("set");

    assert!(host.changing_statements().is_empty());
}

#[test]
fn a_refused_grant_stops_before_any_revoke_is_sent() {
    let host = FakePrivilegeHost::with_reader_and_shop();
    host.plant_row("alice_reader", ESCAPED, &[P::Insert], false);
    host.refuse_statements_starting_with("GRANT", 1, "ERROR 1227 (42000): denied");

    let failure = set_database_user_privileges(&host, &change(&[P::Select])).expect_err("fail");

    assert!(matches!(failure, DbError::ClientFailed { code: 1227 }));
    assert_eq!(
        host.privileges_of("alice_reader", ESCAPED)
            .unwrap()
            .sql_list(),
        "INSERT",
        "the login keeps what it had when the grant is refused"
    );
}

#[test]
fn the_fake_host_refuses_the_combined_revoke_mariadb_rejects() {
    use crate::db::db_host::DbHost;

    let host = FakePrivilegeHost::with_reader_and_shop();
    host.plant_row("alice_reader", "alice_shop", &[P::Select], true);

    let combined = host.execute(
        "REVOKE ALL PRIVILEGES, GRANT OPTION ON `alice_shop`.* FROM 'alice_reader'@'localhost'",
    );

    assert!(
        combined.is_err(),
        "the combined form must be a syntax error"
    );
}

#[test]
fn the_fake_host_accepts_each_half_of_the_revoke_on_its_own() {
    use crate::db::db_host::DbHost;

    let host = FakePrivilegeHost::with_reader_and_shop();
    host.plant_row("alice_reader", "alice_shop", &[P::Select], true);

    host.execute("REVOKE GRANT OPTION ON `alice_shop`.* FROM 'alice_reader'@'localhost'")
        .expect("grant option half");
    host.execute("REVOKE ALL PRIVILEGES ON `alice_shop`.* FROM 'alice_reader'@'localhost'")
        .expect("privileges half");
}
