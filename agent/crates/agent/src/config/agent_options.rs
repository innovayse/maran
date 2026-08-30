//! Command-line options of the agent process.

use std::path::{Path, PathBuf};

use super::options_error::OptionsError;

/// Default production socket path (spec §9).
const DEFAULT_SOCKET: &str = "/run/maran/agent.sock";

/// Flag selecting the socket to bind.
const SOCKET_FLAG: &str = "--socket";

/// Flag selecting the single uid allowed to connect.
const ALLOW_UID_FLAG: &str = "--allow-uid";

/// How the agent was asked to run.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AgentOptions {
    /// Path of the unix socket to bind.
    pub socket_path: PathBuf,
    /// The uid permitted to use the agent.
    pub allow_uid: u32,
}

impl AgentOptions {
    /// Parses `arguments`, which must not include the program name.
    ///
    /// `default_uid` is used when `--allow-uid` is absent — the installer always
    /// passes the panel user's uid explicitly, while a developer running the
    /// agent by hand gets their own uid and a working socket.
    ///
    /// Unknown arguments are ignored rather than rejected: this process is
    /// started by systemd, and a unit file gaining a flag a running binary does
    /// not yet understand must not stop the agent from coming up.
    ///
    /// A malformed value of a *known* flag is a different matter and is refused.
    /// `--allow-uid` decides who may drive a root daemon, and silently falling
    /// back to a default because the value did not parse is how a typo in a unit
    /// file turns into an access rule nobody chose.
    ///
    /// # Errors
    ///
    /// Returns [`OptionsError`] when a flag is given a value that cannot be used.
    pub fn parse(arguments: &[String], default_uid: u32) -> Result<Self, OptionsError> {
        let mut options = Self {
            socket_path: PathBuf::from(DEFAULT_SOCKET),
            allow_uid: default_uid,
        };

        let mut remaining = arguments.iter();
        while let Some(argument) = remaining.next() {
            match argument.as_str() {
                SOCKET_FLAG => {
                    let value = remaining
                        .next()
                        .ok_or(OptionsError::MissingValue { flag: SOCKET_FLAG })?;
                    options.socket_path = PathBuf::from(value);
                }
                ALLOW_UID_FLAG => {
                    let value = remaining.next().ok_or(OptionsError::MissingValue {
                        flag: ALLOW_UID_FLAG,
                    })?;
                    options.allow_uid = value.parse().map_err(|_| OptionsError::InvalidUid {
                        value: value.clone(),
                    })?;
                }
                _ => {}
            }
        }

        Ok(options)
    }

    /// The socket path as a borrowed path.
    #[must_use]
    pub fn socket_path(&self) -> &Path {
        &self.socket_path
    }
}

#[cfg(test)]
#[path = "../tests/config/agent_options_tests.rs"]
mod tests;
