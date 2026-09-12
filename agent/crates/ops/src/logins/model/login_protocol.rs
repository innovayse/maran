//! Which daemon serves a file-transfer login.

/// The daemon that answers a login, and therefore what taking it away means.
///
/// Carried beside the name rather than inferred from it, because the name
/// cannot answer it: `alice_web` is a valid login name under either protocol,
/// and the two are told apart only by the jail their passwd home is. The panel
/// needs the distinction to say WHICH credential a suspension turned, and an
/// operator needs it to know which daemon's log to read.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum LoginProtocol {
    /// A login served by the host's OpenSSH daemon, jailed under
    /// `AgentPaths::SFTP_JAIL_ROOT`.
    Sftp,

    /// A login served by this panel's own vsftpd instance, jailed under
    /// `AgentPaths::FTPS_JAIL_ROOT`.
    Ftps,
}
