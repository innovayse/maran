//! Tests for the closed set of database privileges.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::HashSet;

use super::DatabasePrivilege;

/// The keywords a customer's login may be granted, and the only ones. Written out
/// here, apart from the type, so adding a variant to the type does not silently
/// widen what is allowed: this list has to be edited by a reviewed change.
const ALLOWED_KEYWORDS: [&str; 18] = [
    "SELECT",
    "INSERT",
    "UPDATE",
    "DELETE",
    "CREATE",
    "DROP",
    "ALTER",
    "INDEX",
    "REFERENCES",
    "CREATE TEMPORARY TABLES",
    "LOCK TABLES",
    "CREATE VIEW",
    "SHOW VIEW",
    "CREATE ROUTINE",
    "ALTER ROUTINE",
    "EXECUTE",
    "TRIGGER",
    "EVENT",
];

/// Privileges that must never be representable, whatever else changes.
const NEVER_GRANTABLE: [&str; 9] = [
    "GRANT OPTION",
    "ALL PRIVILEGES",
    "ALL",
    "FILE",
    "PROCESS",
    "SUPER",
    "RELOAD",
    "SHUTDOWN",
    "REPLICATION SLAVE",
];

#[test]
fn every_privilege_renders_a_keyword_from_the_reviewed_allow_list() {
    for privilege in DatabasePrivilege::ALL {
        assert!(
            ALLOWED_KEYWORDS.contains(&privilege.sql_keyword()),
            "{privilege:?} renders {}, which is not on the allow-list",
            privilege.sql_keyword()
        );
    }
}

#[test]
fn the_allow_list_is_exactly_what_the_type_can_render() {
    let rendered: Vec<&str> = DatabasePrivilege::ALL
        .into_iter()
        .map(DatabasePrivilege::sql_keyword)
        .collect();

    assert_eq!(rendered, ALLOWED_KEYWORDS);
}

#[test]
fn no_privilege_that_hands_out_or_widens_access_can_be_rendered() {
    for privilege in DatabasePrivilege::ALL {
        assert!(
            !NEVER_GRANTABLE.contains(&privilege.sql_keyword()),
            "{privilege:?} must not exist"
        );
    }
}

#[test]
fn the_positions_are_distinct_and_fit_one_machine_word_of_bits() {
    let positions: HashSet<u32> = DatabasePrivilege::ALL
        .into_iter()
        .map(DatabasePrivilege::position)
        .collect();

    assert_eq!(positions.len(), DatabasePrivilege::ALL.len());
    assert!(positions.iter().all(|position| *position < u32::BITS));
}

#[test]
fn the_declaration_order_is_the_rendering_order() {
    let mut sorted = DatabasePrivilege::ALL;
    sorted.sort();

    assert_eq!(sorted, DatabasePrivilege::ALL);
    for (index, privilege) in DatabasePrivilege::ALL.into_iter().enumerate() {
        assert_eq!(privilege.position() as usize, index);
    }
}
