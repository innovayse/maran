//! Where one SFTP login's public keys live, and where they wait out a
//! suspension.

use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;

/// The suffix of the name a login's key file is kept under while its account
/// is suspended.
///
/// A login name is `[a-z0-9_]`, so no login's ACTIVE name can ever end in a
/// dot-suffix, and sshd, which reads `AuthorizedKeysFile <directory>/%u`, never
/// opens this one.
const SUSPENDED_SUFFIX: &str = ".suspended";

/// The character an account name and a login's suffix are joined with
/// (`SftpUserName` builds `<account>_<suffix>`).
const ACCOUNT_SEPARATOR: char = '_';

/// The two names one SFTP login's key file can have, both derived from the
/// validated login name and nothing else (issue #135).
///
/// Two names because a suspension has to reach public-key logins too, and the
/// password lock does not: `usermod --lock` prefixes the shadow hash, and with
/// `UsePAM yes` — the default on every supported family — sshd does not consult
/// that hash for a key. So while the account is suspended the file is kept at
/// [`Self::suspended`], a name sshd never reads, and an unlocked login's file
/// is at [`Self::active`]. Both the lock and the key write decide between them
/// from the same shadow field, so neither can undo the other.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct LoginKeyFile {
    /// `<keys directory>/<login>`: the path sshd reads.
    active: PathBuf,
    /// `<keys directory>/<login>.suspended`: the path sshd never reads.
    suspended: PathBuf,
}

impl LoginKeyFile {
    /// The key file names of `login`.
    #[must_use]
    pub fn for_login(login: &SftpUserName) -> Self {
        let active = Path::new(AgentPaths::SFTP_AUTHORIZED_KEYS_DIRECTORY).join(login.as_str());
        let suspended = Path::new(AgentPaths::SFTP_AUTHORIZED_KEYS_DIRECTORY)
            .join(format!("{}{SUSPENDED_SUFFIX}", login.as_str()));

        Self { active, suspended }
    }

    /// The path sshd reads the login's keys from.
    #[must_use]
    pub fn active(&self) -> &Path {
        &self.active
    }

    /// The path the keys are kept at while the account is suspended.
    #[must_use]
    pub fn suspended(&self) -> &Path {
        &self.suspended
    }

    /// The login a file in the key directory belongs to, read back out of its
    /// NAME: the inverse of [`Self::for_login`], so the two cannot drift apart.
    ///
    /// Answers `None` for a name that is not one of the two shapes this agent
    /// writes — an optional `.suspended` suffix after a name made only of
    /// `[a-z0-9_]` that holds the account separator — which is what keeps the
    /// orphan sweep from touching a file somebody else put there (a README, an
    /// editor's backup, a write protocol's `.tmp` file). The name is the
    /// SHAPE of a login's, not proof of one: whether such a login exists is the
    /// password database's answer, which the caller asks.
    #[must_use]
    pub fn login_named_by(file_name: &str) -> Option<&str> {
        let login = file_name
            .strip_suffix(SUSPENDED_SUFFIX)
            .unwrap_or(file_name);
        let looks_like_a_login = login.contains(ACCOUNT_SEPARATOR)
            && login
                .chars()
                .all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == ACCOUNT_SEPARATOR);

        looks_like_a_login.then_some(login)
    }
}

#[cfg(test)]
#[path = "../../tests/sftp/login_key_file_tests.rs"]
mod tests;
