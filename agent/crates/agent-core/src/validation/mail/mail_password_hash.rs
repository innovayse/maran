//! A mailbox password, stored as a hash the IMAP server can verify.

use super::mail_password_hash_error::MailPasswordHashError;
use super::mail_password_scheme::MailPasswordScheme;
use crate::secret_string::SecretString;
use crate::validation::web::bcrypt_hash::BcryptHash;

/// The longest hash accepted, in characters. A real Argon2id string is about
/// a hundred; the ceiling is what bounds the login database's line.
const MAXIMUM_LENGTH: usize = 255;

/// The accepted Argon2id memory cost, in KiB: the floor is 19 MiB (the lowest
/// cost current guidance accepts for Argon2id), the ceiling 1 GiB.
const MEMORY_COST_KIB: core::ops::RangeInclusive<u32> = 19_456..=1_048_576;

/// The accepted Argon2id time cost (passes).
const TIME_COST: core::ops::RangeInclusive<u32> = 2..=10;

/// The accepted Argon2id parallelism.
const PARALLELISM: core::ops::RangeInclusive<u32> = 1..=16;

/// The accepted length of the encoded salt, in base64 characters (16 to 64
/// bytes of salt).
const SALT_LENGTH: core::ops::RangeInclusive<usize> = 22..=86;

/// The accepted length of the encoded hash, in base64 characters (32 to 64
/// bytes of output).
const OUTPUT_LENGTH: core::ops::RangeInclusive<usize> = 43..=86;

/// A validated password hash in the scheme THIS host verifies.
///
/// The panel hashes the password and sends only the hash; the agent checks its
/// SHAPE and never sees a password. The shape is what makes the value safe to
/// write as a field of the login database, where `:` separates fields and a
/// newline starts another entry: neither is in either scheme's alphabet
/// (rules/security.md item 4), and the scheme list leaves out every weak one
/// (item 9).
///
/// The scheme is a parameter of [`MailPasswordHash::parse`], supplied from the
/// distro adapter, because it is a fact of the host. Passing the wrong one is
/// not a validation question the caller could answer for themselves: this type
/// is where "the panel hashed with something this host cannot verify" becomes a
/// refusal rather than a mailbox nobody can open.
///
/// The hash is a credential at rest and is held in a [`SecretString`], so its
/// `Debug` and `Display` print `«redacted»`. [`MailPasswordHash::expose`] is the
/// only reader of the bare hash and
/// [`MailPasswordHash::with_scheme_prefix`] the only builder of the
/// `{SCHEME}hash` form; both are named to be greppable.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct MailPasswordHash {
    /// The scheme the hash is in, which is the scheme the host verifies.
    scheme: MailPasswordScheme,
    /// The bare hash, without a `{SCHEME}` prefix.
    hash: SecretString,
}

impl MailPasswordHash {
    /// Validates `candidate` as a hash in `host_scheme` and wraps it.
    ///
    /// # Errors
    ///
    /// - [`MailPasswordHashError::ControlCharacter`] for a control character;
    ///   checked first.
    /// - [`MailPasswordHashError::TooLong`] beyond 255 characters.
    /// - [`MailPasswordHashError::UnsupportedScheme`] when the candidate is
    ///   neither Argon2id nor bcrypt.
    /// - [`MailPasswordHashError::SchemeNotVerifiedHere`] when it is one of
    ///   them but not `host_scheme`.
    /// - [`MailPasswordHashError::MalformedArgon2id`] and
    ///   [`MailPasswordHashError::Argon2idCostOutOfRange`] for an Argon2id
    ///   string that is not in the encoded form or whose costs are out of range.
    /// - [`MailPasswordHashError::Bcrypt`] when the bcrypt reading refuses it.
    pub fn parse(
        candidate: &str,
        host_scheme: MailPasswordScheme,
    ) -> Result<Self, MailPasswordHashError> {
        if candidate.chars().any(char::is_control) {
            return Err(MailPasswordHashError::ControlCharacter);
        }

        if candidate.chars().count() > MAXIMUM_LENGTH {
            return Err(MailPasswordHashError::TooLong {
                maximum: MAXIMUM_LENGTH,
            });
        }

        let offered = if candidate.starts_with("$argon2id$") {
            MailPasswordScheme::Argon2id
        } else if ["$2a$", "$2b$", "$2y$"]
            .iter()
            .any(|marker| candidate.starts_with(marker))
        {
            MailPasswordScheme::BlowfishCrypt
        } else {
            return Err(MailPasswordHashError::UnsupportedScheme);
        };

        if offered != host_scheme {
            return Err(MailPasswordHashError::SchemeNotVerifiedHere {
                expected: host_scheme,
            });
        }

        match offered {
            MailPasswordScheme::Argon2id => check_argon2id(candidate)?,
            MailPasswordScheme::BlowfishCrypt => {
                BcryptHash::parse(candidate).map_err(MailPasswordHashError::Bcrypt)?;
            }
        }

        Ok(Self {
            scheme: offered,
            hash: SecretString::new(candidate.to_owned()),
        })
    }

    /// The scheme the hash is in.
    #[must_use]
    pub fn scheme(&self) -> MailPasswordScheme {
        self.scheme
    }

    /// The bare hash, for the one place that needs it without a prefix.
    ///
    /// Named after what it does, and with
    /// [`MailPasswordHash::with_scheme_prefix`] one of only two ways the hash
    /// leaves its wrapper.
    #[must_use]
    pub fn expose(&self) -> &str {
        self.hash.expose()
    }

    /// The hash as the login database stores it: `{SCHEME}hash`. Returned in
    /// the redacting wrapper, so building it does not make it printable.
    #[must_use]
    pub fn with_scheme_prefix(&self) -> SecretString {
        SecretString::new(format!(
            "{{{}}}{}",
            self.scheme.dovecot_name(),
            self.hash.expose()
        ))
    }
}

/// Checks the encoded Argon2id form
/// `$argon2id$v=19$m=<KiB>,t=<passes>,p=<lanes>$<salt>$<output>`.
///
/// Salt and output are unpadded standard base64 (`A-Za-z0-9+/`), the form the
/// reference implementation writes. Every field is read by splitting on `$` and
/// then by an exact grammar, so no byte outside the alphabet and no extra field
/// survives.
fn check_argon2id(candidate: &str) -> Result<(), MailPasswordHashError> {
    let malformed = MailPasswordHashError::MalformedArgon2id;

    // `$argon2id$v=19$m=..$salt$output` splits into an empty first field and
    // five more.
    let mut fields = candidate.split('$');
    let (
        Some(""),
        Some("argon2id"),
        Some("v=19"),
        Some(parameters),
        Some(salt),
        Some(output),
        None,
    ) = (
        fields.next(),
        fields.next(),
        fields.next(),
        fields.next(),
        fields.next(),
        fields.next(),
        fields.next(),
    )
    else {
        return Err(malformed);
    };

    let mut parameter_fields = parameters.split(',');
    let (Some(memory), Some(time), Some(lanes), None) = (
        parameter_fields.next().and_then(|f| f.strip_prefix("m=")),
        parameter_fields.next().and_then(|f| f.strip_prefix("t=")),
        parameter_fields.next().and_then(|f| f.strip_prefix("p=")),
        parameter_fields.next(),
    ) else {
        return Err(malformed);
    };

    let (Some(memory), Some(time), Some(lanes)) = (
        plain_number(memory),
        plain_number(time),
        plain_number(lanes),
    ) else {
        return Err(malformed);
    };

    if !(MEMORY_COST_KIB.contains(&memory)
        && TIME_COST.contains(&time)
        && PARALLELISM.contains(&lanes))
    {
        return Err(MailPasswordHashError::Argon2idCostOutOfRange);
    }

    if !(SALT_LENGTH.contains(&salt.len())
        && OUTPUT_LENGTH.contains(&output.len())
        && is_unpadded_base64(salt)
        && is_unpadded_base64(output))
    {
        return Err(malformed);
    }

    Ok(())
}

/// Reads `text` as a decimal number of at most nine digits and nothing else:
/// no sign, no space, no leading `+`, and no leading zero before another digit
/// (a lone `0` is a number, and the range check refuses it). The reference
/// implementation's decoder refuses a cost written with a leading zero
/// (`m=019456`), so a hash spelled that way would be stored as a login nobody
/// can complete.
fn plain_number(text: &str) -> Option<u32> {
    if text.is_empty()
        || text.len() > 9
        || (text.len() > 1 && text.starts_with('0'))
        || !text.bytes().all(|b| b.is_ascii_digit())
    {
        return None;
    }
    text.parse().ok()
}

/// Whether `text` is made only of the standard base64 alphabet, no padding.
fn is_unpadded_base64(text: &str) -> bool {
    text.bytes()
        .all(|b| b.is_ascii_alphanumeric() || b == b'+' || b == b'/')
}

#[cfg(test)]
#[path = "../../tests/validation/mail/mail_password_hash_tests.rs"]
mod tests;
