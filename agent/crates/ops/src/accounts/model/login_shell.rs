//! The login shell an account is given.

/// The login shell of a hosting account: a closed set, and never a path.
///
/// A path would be caller text reaching `useradd --shell` and `usermod --shell`
/// as root. A case cannot be anything else than one of two, and the distribution
/// adapter says what each is on this host (`login_shell_argument`), which is also
/// the only place that decision is taken - creation and unsuspension both ask it,
/// so the shell an account is created with and the one it gets back cannot drift
/// apart (issue #194).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum LoginShell {
    /// No interactive login. SFTP and cron work through the account; a terminal
    /// does not. What every account had before the panel could grant a shell,
    /// and what a caller older than the field asks for.
    Nologin,
    /// The account's own user may log in to a bash shell, where sshd admits it.
    Bash,
}
