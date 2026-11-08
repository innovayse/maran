//! The identifier of one entry in an account's trash.

use super::trash_entry_id_error::TrashEntryIdError;

/// Most digits the seconds part may have (`u64::MAX` has twenty).
const MAXIMUM_SECOND_DIGITS: usize = 20;

/// Exact length of the hexadecimal nonce.
const NONCE_DIGITS: usize = 16;

/// `<unix seconds>-<16 lower-case hex digits>`, the name of a trash entry's
/// directory, valid by construction.
///
/// The trash lives in the customer's home and the customer can rename anything in
/// it, so a directory name is only believed once it parses here. The shape holds
/// no separator, no dot and no control character, so it can name nothing but one
/// entry directly inside `entries/`. The seconds part is what retention is
/// decided on.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TrashEntryId {
    /// The whole text, kept as parsed.
    text: String,
    /// The seconds part, as a number.
    unix_seconds: u64,
}

impl TrashEntryId {
    /// Parses `text` as a trash entry identifier.
    ///
    /// # Errors
    ///
    /// Returns [`TrashEntryIdError::Shape`] when the text is not the exact shape
    /// (digits, one `-`, sixteen lower-case hex digits, nothing else) and
    /// [`TrashEntryIdError::Time`] when the seconds do not fit a `u64`.
    pub fn parse(text: &str) -> Result<Self, TrashEntryIdError> {
        let (seconds, nonce) = text.split_once('-').ok_or(TrashEntryIdError::Shape)?;
        let seconds_ok = !seconds.is_empty()
            && seconds.len() <= MAXIMUM_SECOND_DIGITS
            && seconds.bytes().all(|byte| byte.is_ascii_digit());
        let nonce_ok = nonce.len() == NONCE_DIGITS
            && nonce
                .bytes()
                .all(|byte| byte.is_ascii_digit() || (b'a'..=b'f').contains(&byte));
        if !seconds_ok || !nonce_ok {
            return Err(TrashEntryIdError::Shape);
        }
        let unix_seconds = seconds.parse().map_err(|_| TrashEntryIdError::Time)?;
        Ok(Self {
            text: text.to_owned(),
            unix_seconds,
        })
    }

    /// The identifier as the directory name it is.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.text
    }

    /// When the entry was trashed, in seconds since the epoch.
    #[must_use]
    pub fn unix_seconds(&self) -> u64 {
        self.unix_seconds
    }
}

#[cfg(test)]
#[path = "../../tests/validation/fs/trash_entry_id_tests.rs"]
mod tests;
