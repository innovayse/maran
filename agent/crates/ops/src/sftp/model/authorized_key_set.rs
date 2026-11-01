//! The complete, bounded set of keys one SFTP login may authenticate with.

use maran_agent_core::validation::system::ssh_public_key::SshPublicKey;

use crate::sftp::sftp_error::SftpError;

/// The keys an SFTP login's key file is to hold — sorted, without duplicates,
/// and never more than [`Self::MAX_KEYS`].
///
/// The ceiling bounds what one request can make root write: [`SshPublicKey`]
/// already bounds each key at 8 KiB, so a whole file is at most a few hundred
/// KiB. It is enforced here, in the operation's own input type, as well as at
/// the service boundary, so an `ops` caller other than the service cannot skip
/// it (rules/rust.md "Validation first").
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AuthorizedKeySet {
    /// The distinct keys, sorted so an unchanged set renders identical bytes.
    keys: Vec<SshPublicKey>,
}

impl AuthorizedKeySet {
    /// The most keys one login may hold.
    pub const MAX_KEYS: usize = 32;

    /// Builds the set from `keys`, dropping duplicates.
    ///
    /// # Errors
    ///
    /// [`SftpError::TooManyKeys`] when more than [`Self::MAX_KEYS`] distinct
    /// keys were supplied; nothing is truncated.
    pub fn new(mut keys: Vec<SshPublicKey>) -> Result<Self, SftpError> {
        keys.sort();
        keys.dedup();
        if keys.len() > Self::MAX_KEYS {
            return Err(SftpError::TooManyKeys { count: keys.len() });
        }

        Ok(Self { keys })
    }

    /// Each key as `<algorithm> <base64>`, in the set's order.
    #[must_use]
    pub fn lines(&self) -> Vec<String> {
        self.keys.iter().map(SshPublicKey::as_line).collect()
    }

    /// How many distinct keys the set holds.
    #[must_use]
    pub fn len(&self) -> usize {
        self.keys.len()
    }

    /// Whether the set is empty, which removes the login's key file.
    #[must_use]
    pub fn is_empty(&self) -> bool {
        self.keys.is_empty()
    }
}

#[cfg(test)]
#[path = "../../tests/sftp/authorized_key_set_tests.rs"]
mod tests;
