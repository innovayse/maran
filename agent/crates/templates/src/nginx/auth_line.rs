//! One line of a site's credential file, as the template writes it.

/// A user name and the hash of that user's password.
///
/// Both are validated by the caller: the name is a `BasicAuthUserName`
/// (`[A-Za-z0-9._-]`, no `:`) and the hash a `BcryptHash` (the bcrypt alphabet
/// and `$`), so neither can hold the `:` that separates the fields or a line
/// break (rules/security.md item 4). The hash is a secret and this struct
/// deliberately has no `Debug`: it cannot be logged by accident.
pub struct AuthLine<'a> {
    /// The login name.
    pub name: &'a str,
    /// The bcrypt hash of the password.
    pub hash: &'a str,
}
