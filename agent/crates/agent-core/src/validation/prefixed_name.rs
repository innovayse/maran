//! The shared core of every "account-prefixed" name.

use crate::validation::prefix_problem::PrefixProblem;
use crate::validation::system::name::AccountName;

/// The separator between the owning account and the requested half.
///
/// One constant for all three prefixed names, because the DECODERS on those
/// types split at this character and a disagreement would decode silently
/// wrong (see `SftpUserName::decode`).
pub(crate) const SEPARATOR: char = '_';

/// Builds `<account>_<requested>`, applying the shared alphabet and length
/// rules all three prefixed names agree on.
///
/// # Errors
///
/// - [`PrefixProblem::Empty`] when `requested` is empty.
/// - [`PrefixProblem::UnexpectedCharacter`] for anything outside `[a-z0-9]`.
/// - [`PrefixProblem::TooLong`] when the prefixed result exceeds
///   `maximum_length` bytes.
pub(crate) fn prefixed(
    account: &AccountName,
    requested: &str,
    maximum_length: usize,
) -> Result<String, PrefixProblem> {
    if requested.is_empty() {
        return Err(PrefixProblem::Empty);
    }

    if let Some(character) = requested
        .chars()
        .find(|c| !(c.is_ascii_lowercase() || c.is_ascii_digit()))
    {
        return Err(PrefixProblem::UnexpectedCharacter { character });
    }

    let full = format!("{}{SEPARATOR}{requested}", account.as_str());
    if full.len() > maximum_length {
        return Err(PrefixProblem::TooLong { length: full.len() });
    }

    Ok(full)
}
