//! The password hash schemes the IMAP server can verify.

/// A password hash scheme the mail stack accepts, as a closed set.
///
/// WHICH of them a host supports is a fact of its distribution, not a choice of
/// the caller and not a constant of the product: the IMAP server verifies
/// Argon2id only when its build linked the library (some distributions' builds
/// did not), and bcrypt is the scheme every build can verify. The distro
/// adapter answers this per family, the status rpc reports it, and
/// [`crate::validation::mail::mail_password_hash::MailPasswordHash::parse`]
/// refuses a hash of any other scheme. A scheme absent from this enum cannot be
/// stored, which is how the weak ones (MD5, SHA-1, plain crypt) stay out
/// (rules/security.md item 9).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum MailPasswordScheme {
    /// Argon2id in its encoded form, `$argon2id$v=19$m=..,t=..,p=..$salt$hash`.
    Argon2id,
    /// bcrypt in its crypt form, `$2y$<cost>$...`.
    BlowfishCrypt,
}

impl MailPasswordScheme {
    /// The scheme's name as the IMAP server spells it in a `{SCHEME}hash`
    /// prefix and lists it in `doveadm pw -l`.
    #[must_use]
    pub fn dovecot_name(self) -> &'static str {
        match self {
            Self::Argon2id => "ARGON2ID",
            Self::BlowfishCrypt => "BLF-CRYPT",
        }
    }
}

#[cfg(test)]
#[path = "../../tests/validation/mail/mail_password_scheme_tests.rs"]
mod tests;
