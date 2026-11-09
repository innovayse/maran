//! The two facts about a host that depend on its release and not only on its family.

use maran_distro::{DistroAdapter, DovecotMajor, KeyValueServer};

use crate::mail::mail_error::MailError;

/// What this host's release runs for Dovecot and for the key-value server.
///
/// Both are answers to a question about the RELEASE (Debian 13 ships Dovecot 2.4
/// where Debian 12 ships 2.3; AlmaLinux 10 ships Valkey where 9 ships Redis), so
/// they take the os-release `VERSION_ID` the daemon read at start. A release the
/// adapter has no answer for is refused: rendering for a guessed generation
/// writes a file the daemon will not read.
#[derive(Debug, Clone, Copy)]
pub struct MailLayout {
    /// The Dovecot configuration generation to render.
    pub dovecot_major: DovecotMajor,
    /// The Redis-protocol server to configure and start.
    pub key_value_server: KeyValueServer,
}

impl MailLayout {
    /// Asks the adapter about `version_id`.
    ///
    /// # Errors
    ///
    /// Returns [`MailError::UnsupportedRelease`] when the adapter has no answer
    /// for either question.
    pub fn resolve(distro: &dyn DistroAdapter, version_id: &str) -> Result<Self, MailError> {
        let dovecot_major = distro
            .dovecot_major(version_id)
            .ok_or(MailError::UnsupportedRelease)?;
        let key_value_server = distro
            .key_value_server(version_id)
            .ok_or(MailError::UnsupportedRelease)?;
        Ok(Self {
            dovecot_major,
            key_value_server,
        })
    }

    /// The unix socket the key-value server listens on.
    ///
    /// DERIVED, not a recorded fact: each of these servers creates a runtime
    /// directory named for its user and the socket is named for its unit
    /// (`/run/redis/redis-server.sock` on the Debian family, which is the packaged
    /// default). The RHEL family's spelling is UNVERIFIED; `KeyValueServer` should
    /// carry the socket path as a field once the lifecycle suite has measured it,
    /// and this method then disappears.
    #[must_use]
    pub fn key_value_socket(&self) -> String {
        format!(
            "/run/{}/{}.sock",
            self.key_value_server.user, self.key_value_server.service
        )
    }
}

#[cfg(test)]
#[path = "../../tests/mail/mail_layout_tests.rs"]
mod tests;
