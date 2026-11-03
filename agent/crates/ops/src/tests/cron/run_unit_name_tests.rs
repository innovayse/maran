//! What a run's unit is called, and that no two runs share a name.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::RunUnitName;
use crate::cron::recording_cron_host::{ACCOUNT_UID, FIRST_ID, entry_id};

#[test]
fn the_name_is_the_prefix_the_uid_the_entry_and_the_run_number() {
    let name = RunUnitName::numbered(ACCOUNT_UID, &entry_id(FIRST_ID), 7);

    assert_eq!(
        name.stem(),
        format!("maran-cron-run-{ACCOUNT_UID}-{FIRST_ID}-7")
    );
}

#[test]
fn the_service_name_is_the_stem_with_the_service_suffix() {
    let name = RunUnitName::numbered(ACCOUNT_UID, &entry_id(FIRST_ID), 7);

    assert_eq!(name.service(), format!("{}.service", name.stem()));
}

#[test]
fn two_runs_of_one_entry_get_two_names() {
    let first = RunUnitName::next(ACCOUNT_UID, &entry_id(FIRST_ID));
    let second = RunUnitName::next(ACCOUNT_UID, &entry_id(FIRST_ID));

    assert_ne!(first, second);
}

#[test]
fn the_name_holds_only_lowercase_letters_digits_and_hyphens() {
    // The alphabet is what leaves no room for an option, a path or a second
    // unit in the argument it becomes.
    let name = RunUnitName::next(ACCOUNT_UID, &entry_id(FIRST_ID));

    assert!(
        name.stem()
            .chars()
            .all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '-'),
        "{}",
        name.stem()
    );
    assert!(!name.stem().starts_with('-'));
}
