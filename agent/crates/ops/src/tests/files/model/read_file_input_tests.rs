//! Tests for the read request's byte limit.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::system::name::AccountName;

use super::ReadFileInput;
use crate::files::model::read_file_input_error::ReadFileInputError;

/// The 512 MiB ceiling.
const CEILING: u64 = 512 * 1024 * 1024;

/// A request with `limit`.
fn with(limit: u64) -> Result<ReadFileInput, ReadFileInputError> {
    ReadFileInput::new(
        AccountName::parse("alice1").unwrap(),
        BrowsePath::parse("a/b.txt").unwrap(),
        limit,
    )
}

#[test]
fn a_limit_of_zero_is_refused() {
    assert_eq!(with(0).unwrap_err(), ReadFileInputError::ZeroLimit);
}

#[test]
fn a_limit_one_byte_over_the_ceiling_is_refused() {
    assert_eq!(
        with(CEILING + 1).unwrap_err(),
        ReadFileInputError::AboveCeiling
    );
}

#[test]
fn a_limit_exactly_at_the_ceiling_is_accepted() {
    assert_eq!(with(CEILING).unwrap().max_bytes(), CEILING);
}

#[test]
fn a_limit_of_one_byte_is_accepted_and_the_fields_are_kept() {
    let input = with(1).unwrap();

    assert_eq!(input.max_bytes(), 1);
    assert_eq!(input.account().as_str(), "alice1");
    assert_eq!(input.path().file_name(), "b.txt");
}
