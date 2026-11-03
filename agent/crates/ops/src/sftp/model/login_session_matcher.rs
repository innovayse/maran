//! How the processes of ONE SFTP login's open sessions are told from every
//! other process on the host.

use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;

/// `pkill`'s flag naming the signal to send.
const SIGNAL_FLAG: &str = "--signal";

/// `pkill`'s flag printing how many processes matched, which is the answer the
/// caller reports.
const COUNT_FLAG: &str = "--count";

/// `pkill`'s flag matching the pattern against the whole command line instead
/// of the executable's name.
///
/// Needed because a session is told apart by the title sshd gives it, which is
/// part of the command line and not of the executable name: every session of
/// every login runs the same `sshd`.
const FULL_FLAG: &str = "--full";

/// The title of a session's process, up to the login's name.
///
/// sshd retitles each connection it admits. Before OpenSSH 9.8 the process is
/// `sshd: <login> [priv]` (the privileged monitor, root) and
/// `sshd: <login>@<tty>` (the session, running as the login's uid); from 9.8 on
/// the executable is `sshd-session` and so is the title's first word. Both
/// spellings are accepted because the supported systems ship both (Ubuntu
/// 22.04, Debian 12 and AlmaLinux 9 the older, Debian 13 and AlmaLinux 10 the
/// newer — rules/architecture.md).
const TITLE_HEAD: &str = "^sshd(-session)?: ";

/// What follows the login's name in the title of either of its two processes:
/// a space and `[priv]`, or the `@` that introduces the session's terminal.
///
/// The name is therefore followed by a character no login name contains, so
/// `alice_web` never matches the title of `alice_webdav`'s session.
const TITLE_TAIL: &str = r"( \[priv\]|@)";

/// The `pkill` arguments that signal exactly one SFTP login's session
/// processes.
///
/// **Why a process title and not a uid.** An SFTP login is created with its
/// ACCOUNT's own uid (`AccountOwnership`), so `pkill --uid` would end every
/// process of the account — its other logins, and any `fork_as_account` child
/// the agent has running for it — to revoke one key. The only thing that
/// distinguishes one login's sessions is the name sshd puts in their titles.
///
/// **Why that is safe to hand to a regular expression.** The login name is a
/// [`SftpUserName`], whose alphabet is `[a-z0-9_]` and which has no constructor
/// taking a whole name off the wire, so it contains no regex metacharacter. The
/// anchor `^` and the shape of [`TITLE_HEAD`] mean the pattern matches a
/// process whose command line BEGINS with sshd's own title, not one that merely
/// mentions it. A customer can retitle a process of their own to match, and the
/// only effect is that their own process is signalled when their own login is
/// ended.
///
/// The pattern follows [`END_OF_OPTIONS`], so it can never be read as an option.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct LoginSessionMatcher {
    /// The extended regular expression `pkill --full` is given.
    pattern: String,
}

impl LoginSessionMatcher {
    /// Builds the matcher for `login`.
    #[must_use]
    pub fn for_login(login: &SftpUserName) -> Self {
        Self {
            pattern: format!("{TITLE_HEAD}{}{TITLE_TAIL}", login.as_str()),
        }
    }

    /// The regular expression, for a test and for an operator's log line.
    #[must_use]
    pub fn pattern(&self) -> &str {
        &self.pattern
    }

    /// The whole argument vector after the program: signal, count, full command
    /// line, end of options, pattern.
    ///
    /// `signal` is the signal's name (`KILL`); it is a constant of the caller,
    /// never a request's text.
    #[must_use]
    pub fn arguments(&self, signal: &str) -> Vec<String> {
        vec![
            SIGNAL_FLAG.to_owned(),
            signal.to_owned(),
            COUNT_FLAG.to_owned(),
            FULL_FLAG.to_owned(),
            END_OF_OPTIONS.to_owned(),
            self.pattern.clone(),
        ]
    }
}

#[cfg(test)]
#[path = "../../tests/sftp/login_session_matcher_tests.rs"]
mod tests;
