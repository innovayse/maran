//! Tests for the identifier of a trash entry.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::TrashEntryId;
use crate::validation::fs::trash_entry_id_error::TrashEntryIdError;

#[test]
fn the_shape_the_agent_writes_is_accepted() {
    let id = TrashEntryId::parse("1700000000-0123456789abcdef").unwrap();

    assert_eq!(id.as_str(), "1700000000-0123456789abcdef");
    assert_eq!(id.unix_seconds(), 1_700_000_000);
}

#[test]
fn anything_that_is_not_the_shape_is_refused() {
    for text in [
        "",
        "..",
        "../x",
        "a/b",
        "1700000000",
        "1700000000-",
        "-0123456789abcdef",
        "1700000000-0123456789abcde",
        "1700000000-0123456789abcdef0",
        "1700000000-0123456789ABCDEF",
        "1700000000-0123456789abcdeg",
        "17x0000000-0123456789abcdef",
        "1700000000-0123456789abcdef\n",
        "+1-0123456789abcdef",
    ] {
        assert_eq!(
            TrashEntryId::parse(text).unwrap_err(),
            TrashEntryIdError::Shape,
            "{text:?}"
        );
    }
}

#[test]
fn seconds_that_overflow_are_refused_as_a_time() {
    assert_eq!(
        TrashEntryId::parse("99999999999999999999-0123456789abcdef").unwrap_err(),
        TrashEntryIdError::Time
    );
}
