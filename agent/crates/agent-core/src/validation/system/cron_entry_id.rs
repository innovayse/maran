//! The identifier of one cron entry, and the only variable part of its paths.

use super::cron_entry_id_error::CronEntryIdError;

/// The length of a hyphenated uuid — `8-4-4-4-12` plus four hyphens.
const UUID_LENGTH: usize = 36;

/// The four positions a hyphenated uuid puts its hyphens at.
const HYPHEN_POSITIONS: [usize; 4] = [8, 13, 18, 23];

/// A validated cron entry id: a plain lowercase hyphenated uuid.
///
/// The inner string is private and the only constructor is
/// [`CronEntryId::parse`], so holding a value of this type is proof that
/// validation happened.
///
/// This type exists to make one specific mistake unrepresentable. The id is
/// interpolated into three paths under an account's home —
/// `<home>/.maran/cron/<id>.cmd`, `.log` and `.exit` — and `Path::join` with an
/// absolute string REPLACES the path it is joined to, so an id of
/// `/etc/cron.d/evil` would move the write out of the account's home entirely,
/// and one of `../../..` would climb out of it. Refusing anything but 36
/// characters of lowercase hex and four hyphens at fixed positions removes the
/// alphabet those attacks are written in, which is why the path helpers carry
/// no traversal check: they cannot be reached by a value that needs one.
///
/// The agent mints the id — it is a uuid the agent generates when an entry is
/// created, never a field of a request — so this type is a re-validation of the
/// agent's own value rather than a gate on a customer's. That is the same
/// defense in depth every other validator here applies (rules/rust.md
/// "Validation first"): the layer that generates a value and the layer that
/// writes it are not the same layer, and only one of them is in this crate.
///
/// No generator lives here on purpose. Minting a uuid needs a source of
/// randomness and a dependency to match, and `agent-core` is the crate every
/// other one depends on; the operation that creates an entry generates the id
/// and hands it here to be checked.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct CronEntryId(String);

impl CronEntryId {
    /// Validates `candidate` as a lowercase hyphenated uuid and wraps it.
    ///
    /// Accepts exactly the 36-character form — eight, four, four, four and
    /// twelve lowercase hexadecimal digits separated by hyphens — and nothing
    /// else. No braces, no `urn:uuid:` prefix, no uppercase, no unhyphenated
    /// 32-character form: an id has one spelling because it names a file.
    ///
    /// The version and variant nibbles are NOT checked. This type answers "can
    /// this safely be a path segment", and a uuid whose version bits say
    /// something unexpected is still exactly 36 harmless characters.
    ///
    /// # Errors
    ///
    /// - [`CronEntryIdError::WrongLength`] when `candidate` is not 36 bytes —
    ///   which is what refuses the empty id, an absolute path and `..`. Bytes,
    ///   and the refusal reports bytes: every character this type accepts is
    ///   ASCII, so for anything that could have been valid the byte count and
    ///   the character count are the same number.
    /// - [`CronEntryIdError::MisplacedHyphen`] when a hyphen is missing from,
    ///   or found outside, positions 8, 13, 18 and 23.
    /// - [`CronEntryIdError::IllegalCharacter`] for anything outside `0-9` and
    ///   `a-f` at a non-hyphen position — uppercase hex included.
    pub fn parse(candidate: &str) -> Result<Self, CronEntryIdError> {
        if candidate.len() != UUID_LENGTH {
            return Err(CronEntryIdError::WrongLength {
                expected: UUID_LENGTH,
                actual: candidate.len(),
            });
        }

        for (position, character) in candidate.char_indices() {
            if HYPHEN_POSITIONS.contains(&position) {
                if character != '-' {
                    return Err(CronEntryIdError::MisplacedHyphen { position });
                }
                continue;
            }

            if character == '-' {
                return Err(CronEntryIdError::MisplacedHyphen { position });
            }

            if !character.is_ascii_digit() && !('a'..='f').contains(&character) {
                return Err(CronEntryIdError::IllegalCharacter { character });
            }
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated id, as it appears in the file names it forms.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/cron_entry_id_tests.rs"]
mod tests;
