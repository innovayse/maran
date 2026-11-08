//! Tests for the set of database privileges.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::validation::db::database_privilege::DatabasePrivilege;
use crate::validation::db::database_privilege_set_error::DatabasePrivilegeSetError;

use super::DatabasePrivilegeSet;

#[test]
fn the_empty_set_holds_nothing_and_renders_nothing() {
    let set = DatabasePrivilegeSet::empty();

    assert!(set.is_empty());
    assert_eq!(set.sql_list(), "");
    assert_eq!(set.iter().count(), 0);
    assert_eq!(set, DatabasePrivilegeSet::default());
}

#[test]
fn an_empty_list_builds_the_empty_set() {
    assert_eq!(
        DatabasePrivilegeSet::of(&[]).unwrap(),
        DatabasePrivilegeSet::empty()
    );
}

#[test]
fn a_list_renders_in_the_fixed_order_whatever_order_it_arrived_in() {
    let set = DatabasePrivilegeSet::of(&[
        DatabasePrivilege::Update,
        DatabasePrivilege::Select,
        DatabasePrivilege::Event,
    ])
    .unwrap();

    assert_eq!(set.sql_list(), "SELECT, UPDATE, EVENT");
}

#[test]
fn a_multi_word_privilege_is_rendered_whole() {
    let set = DatabasePrivilegeSet::of(&[
        DatabasePrivilege::CreateTemporaryTables,
        DatabasePrivilege::LockTables,
    ])
    .unwrap();

    assert_eq!(set.sql_list(), "CREATE TEMPORARY TABLES, LOCK TABLES");
}

#[test]
fn all_eighteen_privileges_make_a_full_set() {
    let set = DatabasePrivilegeSet::of(&DatabasePrivilege::ALL).unwrap();

    assert_eq!(set.iter().count(), 18);
    for privilege in DatabasePrivilege::ALL {
        assert!(set.contains(privilege));
    }
}

#[test]
fn a_repeated_privilege_is_refused_and_named() {
    let refused = DatabasePrivilegeSet::of(&[DatabasePrivilege::Select, DatabasePrivilege::Select])
        .expect_err("must refuse");

    assert_eq!(
        refused,
        DatabasePrivilegeSetError::Duplicate {
            privilege: DatabasePrivilege::Select
        }
    );
}

#[test]
fn more_entries_than_the_closed_set_holds_is_refused_before_any_is_read() {
    let too_many = vec![DatabasePrivilege::Select; 19];

    let refused = DatabasePrivilegeSet::of(&too_many).expect_err("must refuse");

    assert_eq!(
        refused,
        DatabasePrivilegeSetError::TooMany {
            length: 19,
            maximum: 18
        }
    );
}

#[test]
fn a_difference_keeps_what_the_left_side_has_and_the_right_lacks() {
    let wanted =
        DatabasePrivilegeSet::of(&[DatabasePrivilege::Select, DatabasePrivilege::Insert]).unwrap();
    let held =
        DatabasePrivilegeSet::of(&[DatabasePrivilege::Insert, DatabasePrivilege::Delete]).unwrap();

    assert_eq!(wanted.difference(held).sql_list(), "SELECT");
    assert_eq!(held.difference(wanted).sql_list(), "DELETE");
    assert!(wanted.difference(wanted).is_empty());
}

#[test]
fn a_set_read_from_a_predicate_holds_exactly_what_the_predicate_accepts() {
    let set = DatabasePrivilegeSet::from_all_where(|privilege| {
        matches!(
            privilege,
            DatabasePrivilege::Drop | DatabasePrivilege::Trigger
        )
    });

    assert_eq!(set.sql_list(), "DROP, TRIGGER");
}

#[test]
fn a_predicate_accepting_nothing_reads_as_the_empty_set() {
    assert!(DatabasePrivilegeSet::from_all_where(|_| false).is_empty());
}
