//! The temporary import user is granted the escaped pattern of its one database.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use super::*;

/// The database `acct`'s `my_db` suffix names: `acct_mydb` shaped with a separator.
fn database() -> DatabaseName {
    let account = AccountName::parse("acct_my").expect("the fixture name is valid");

    DatabaseName::for_account(&account, "db").expect("the fixture name is valid")
}

/// The statements the loader sends for the fixture database.
fn statements() -> String {
    creation_statements(
        &ImportUser::from_random(&[1; 8]),
        &SecretString::new("ab".to_owned()),
        &database(),
    )
}

/// The grant names the database with every `_` escaped, so a neighbour whose
/// name differs at a separator is not matched.
#[test]
fn the_grant_escapes_every_underscore_of_the_database_name() {
    let text = statements();

    assert!(
        text.contains("ON `acct\\_my\\_db`.*"),
        "the grant must carry the escaped pattern: {text}"
    );
    assert!(
        !text.contains("ON `acct_my_db`.*"),
        "the unescaped name is a wildcard pattern: {text}"
    );
}

/// The user is still created before the grant, with the same name in both.
#[test]
fn the_user_is_created_and_then_granted_under_one_name() {
    let text = statements();
    let name = ImportUser::from_random(&[1; 8]);

    assert!(text.starts_with(&format!("CREATE USER '{}'@'localhost'", name.name())));
    assert!(text.contains(&format!("TO '{}'@'localhost';", name.name())));
}
