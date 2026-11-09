//! The one place that decides which shell path `useradd` and `usermod` are given.

use maran_distro::DistroAdapter;

use crate::accounts::model::login_shell::LoginShell;

/// The shell path of `shell` on this host, as the argument of `--shell`.
///
/// Asked by account creation (`useradd --shell`) and by unsuspension (`usermod
/// --shell`) alike, and by nothing else: an account created with a login shell
/// and unsuspended with a different decision would lose the shell the first time
/// it was suspended and put back, and two spellings of the choice is how that
/// happens. The path itself is the distribution's (`DistroAdapter::nologin_shell`,
/// `DistroAdapter::bash_shell`): `useradd` does not verify a shell, so a literal
/// here would create the account successfully on a host where the path names
/// nothing (rules/rust.md "Distro adapter").
///
/// Suspension does not ask this at all: it always sets the nologin shell, which
/// is what makes a suspended account unable to log in however it authenticated.
#[must_use]
pub(super) fn login_shell_argument(distro: &dyn DistroAdapter, shell: LoginShell) -> &'static str {
    match shell {
        LoginShell::Nologin => distro.nologin_shell(),
        LoginShell::Bash => distro.bash_shell(),
    }
}

#[cfg(test)]
#[path = "../tests/accounts/login_shell_argument_tests.rs"]
mod tests;
