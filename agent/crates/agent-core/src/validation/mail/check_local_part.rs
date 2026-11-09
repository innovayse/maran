//! The one reading of an address's local part.

use super::mail_local_part_error::MailLocalPartError;

/// The longest local part an address may have, from RFC 5321.
pub(crate) const MAXIMUM_LENGTH: usize = 64;

/// Checks `candidate` against the local-part grammar and returns it lower-cased.
///
/// The grammar is a deliberate subset of what RFC 5321 permits: lower-case ASCII
/// letters and digits, and `.`, `-` and `_` between them. With
/// `allow_extension_delimiter` a `+` is accepted too, for a TARGET on another
/// host (`name+tag@example.org`) where the plus is the remote side's business;
/// it is refused for an address this host hosts, because the MTA's recipient
/// delimiter is `+` and a mailbox containing one would be unreachable.
///
/// Why so narrow: the value is written into a mail map (whitespace-separated),
/// a login database (colon-separated), a directory name under a customer's
/// home and a script. The alphabet contains none of the separators any of
/// them uses, and it needs no quoting, so there is nothing to escape wrongly
/// (rules/security.md item 4).
///
/// Lower-cased on the way in, because mail systems treat local parts
/// case-insensitively in practice and a directory name is case-sensitive: two
/// spellings must not become two mailboxes.
///
/// # Errors
///
/// - [`MailLocalPartError::Empty`] for an empty candidate.
/// - [`MailLocalPartError::TooLong`] beyond [`MAXIMUM_LENGTH`] characters.
/// - [`MailLocalPartError::IllegalCharacter`] for any character outside the
///   alphabet; checked before the shape rules, so a newline is named as itself.
/// - [`MailLocalPartError::EdgeNotAlphanumeric`] when the first or last
///   character is not a letter or a digit.
/// - [`MailLocalPartError::ConsecutiveDots`] for `..`.
pub(crate) fn check_local_part(
    candidate: &str,
    allow_extension_delimiter: bool,
) -> Result<String, MailLocalPartError> {
    if candidate.is_empty() {
        return Err(MailLocalPartError::Empty);
    }

    if candidate.chars().count() > MAXIMUM_LENGTH {
        return Err(MailLocalPartError::TooLong {
            maximum: MAXIMUM_LENGTH,
        });
    }

    let lowered = candidate.to_ascii_lowercase();

    if let Some(character) = lowered.chars().find(|c| {
        !(c.is_ascii_lowercase()
            || c.is_ascii_digit()
            || matches!(c, '.' | '-' | '_')
            || (allow_extension_delimiter && *c == '+'))
    }) {
        return Err(MailLocalPartError::IllegalCharacter { character });
    }

    let edge_ok = |c: Option<char>| c.is_some_and(|c| c.is_ascii_lowercase() || c.is_ascii_digit());
    if !(edge_ok(lowered.chars().next()) && edge_ok(lowered.chars().next_back())) {
        return Err(MailLocalPartError::EdgeNotAlphanumeric);
    }

    if lowered.contains("..") {
        return Err(MailLocalPartError::ConsecutiveDots);
    }

    Ok(lowered)
}

#[cfg(test)]
#[path = "../../tests/validation/mail/check_local_part_tests.rs"]
mod tests;
