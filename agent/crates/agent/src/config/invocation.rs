//! What the command line asked this process to do.

use super::agent_options::{ALLOW_UID_FLAG, AgentOptions, DEFAULT_SOCKET, SOCKET_FLAG};
use super::options_error::OptionsError;

/// Flags asking for the usage text instead of a running daemon.
const HELP_FLAGS: [&str; 2] = ["--help", "-h"];

/// The usage text, printed for [`Invocation::ShowUsage`].
///
/// Written out rather than derived from an argument-parsing library: the agent
/// takes two flags, and a dependency that renders help for two flags would be a
/// dependency in a root daemon, which is the one place where the cost of a
/// dependency is not measured in build time.
pub const USAGE: &str = concat!(
    "maran-agent — the Maran root daemon\n",
    "\n",
    "Usage: maran-agent [--socket <path>] [--allow-uid <uid>]\n",
    "\n",
    "  --socket <path>     Unix socket to bind (default: /run/maran/agent.sock)\n",
    "  --allow-uid <uid>   The single uid permitted to use the agent\n",
    "                      (default: the uid this process runs as)\n",
    "  -h, --help          Print this text and exit\n",
);

/// What the agent was asked to do.
///
/// An enum rather than a flag on [`AgentOptions`], because the two arms do
/// different things: one binds a socket and serves as root, the other writes to
/// standard output and exits. A boolean would let a caller do both, or neither.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Invocation {
    /// Run the daemon with these options.
    Run(AgentOptions),
    /// Print [`USAGE`] and exit successfully.
    ShowUsage,
}

impl Invocation {
    /// Parses `arguments`, which must not include the program name.
    ///
    /// `default_uid` is used when `--allow-uid` is absent — the installer always
    /// passes the panel user's uid explicitly, while a developer running the
    /// agent by hand gets their own uid and a working socket.
    ///
    /// **An unrecognised argument is refused.** It used to be ignored, on the
    /// reasoning that a unit file gaining a flag an older binary does not know
    /// must not stop the agent from coming up. That trade was the wrong way
    /// round, and it was observed: `maran-agent --help` printed nothing, was
    /// parsed as "no flags at all", and started a REAL root daemon on the
    /// default socket path with the default uid — taking the socket from the
    /// agent already serving it and answering with an access rule nobody chose.
    /// A unit file naming a flag the binary does not have is a packaging error,
    /// and a service that fails loudly on one is strictly better than a root
    /// daemon running a configuration its operator did not write.
    ///
    /// A malformed value of a *known* flag is refused for the same reason.
    /// `--allow-uid` decides who may drive a root daemon, and silently falling
    /// back to a default because the value did not parse is how a typo in a unit
    /// file turns into an access rule nobody chose.
    ///
    /// # Errors
    ///
    /// Returns [`OptionsError::UnknownFlag`] for an argument this binary does
    /// not define, [`OptionsError::MissingValue`] for a flag left dangling, and
    /// [`OptionsError::InvalidUid`] when `--allow-uid` is not a number.
    pub fn parse(arguments: &[String], default_uid: u32) -> Result<Self, OptionsError> {
        // Swept across the WHOLE command line before anything is validated, so
        // `--help` still answers when it stands beside the very flag the person
        // is asking about. Checking it in the loop below would refuse the one
        // invocation that explains the mistake.
        if arguments
            .iter()
            .any(|argument| HELP_FLAGS.contains(&argument.as_str()))
        {
            return Ok(Self::ShowUsage);
        }

        let mut options = AgentOptions {
            socket_path: std::path::PathBuf::from(DEFAULT_SOCKET),
            allow_uid: default_uid,
        };

        let mut remaining = arguments.iter();
        while let Some(argument) = remaining.next() {
            match argument.as_str() {
                SOCKET_FLAG => {
                    let value = remaining
                        .next()
                        .ok_or(OptionsError::MissingValue { flag: SOCKET_FLAG })?;
                    options.socket_path = std::path::PathBuf::from(value);
                }
                ALLOW_UID_FLAG => {
                    let value = remaining.next().ok_or(OptionsError::MissingValue {
                        flag: ALLOW_UID_FLAG,
                    })?;
                    options.allow_uid = value.parse().map_err(|_| OptionsError::InvalidUid {
                        value: value.clone(),
                    })?;
                }
                unknown => {
                    return Err(OptionsError::UnknownFlag {
                        flag: unknown.to_owned(),
                    });
                }
            }
        }

        Ok(Self::Run(options))
    }
}

#[cfg(test)]
#[path = "../tests/config/invocation_tests.rs"]
mod tests;
