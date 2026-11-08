//! One user of a site's HTTP basic authentication.

use maran_agent_core::validation::web::basic_auth_user_name::BasicAuthUserName;
use maran_agent_core::validation::web::bcrypt_hash::BcryptHash;

/// A user name and the bcrypt hash of that user's password.
///
/// Both halves are validated types, so a line built from them cannot hold a
/// second field separator or a line break. The hash is a secret: its type
/// prints `«redacted»` in `Debug`, so a request struct holding users can be
/// logged without leaking it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BasicAuthUser {
    /// The login name.
    name: BasicAuthUserName,
    /// The password, hashed by the panel.
    password_hash: BcryptHash,
}

impl BasicAuthUser {
    /// Pairs a validated name with a validated hash.
    #[must_use]
    pub fn new(name: BasicAuthUserName, password_hash: BcryptHash) -> Self {
        Self {
            name,
            password_hash,
        }
    }

    /// The login name.
    #[must_use]
    pub fn name(&self) -> &BasicAuthUserName {
        &self.name
    }

    /// The hash of the user's password.
    #[must_use]
    pub fn password_hash(&self) -> &BcryptHash {
        &self.password_hash
    }
}
