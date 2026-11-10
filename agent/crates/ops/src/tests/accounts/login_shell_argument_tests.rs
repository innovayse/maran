//! Tests for the `login_shell_argument` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_distro::{DistroFamily, adapter_for};

use super::login_shell_argument;
use crate::accounts::LoginShell;

#[test]
fn the_nologin_case_is_the_distributions_nologin_shell_on_each_family() {
    let debian = adapter_for(DistroFamily::Debian);
    let rhel = adapter_for(DistroFamily::Rhel);
    let suse = adapter_for(DistroFamily::Suse);

    assert_eq!(
        login_shell_argument(debian, LoginShell::Nologin),
        debian.nologin_shell()
    );
    assert_eq!(
        login_shell_argument(rhel, LoginShell::Nologin),
        rhel.nologin_shell()
    );
    assert_eq!(
        login_shell_argument(suse, LoginShell::Nologin),
        suse.nologin_shell()
    );
}

#[test]
fn the_bash_case_is_the_distributions_bash_on_each_family() {
    let debian = adapter_for(DistroFamily::Debian);
    let rhel = adapter_for(DistroFamily::Rhel);
    let suse = adapter_for(DistroFamily::Suse);

    assert_eq!(
        login_shell_argument(debian, LoginShell::Bash),
        debian.bash_shell()
    );
    assert_eq!(
        login_shell_argument(rhel, LoginShell::Bash),
        rhel.bash_shell()
    );
    assert_eq!(
        login_shell_argument(suse, LoginShell::Bash),
        suse.bash_shell()
    );
}

#[test]
fn the_two_cases_never_answer_the_same_path() {
    for family in [DistroFamily::Debian, DistroFamily::Rhel, DistroFamily::Suse] {
        let distro = adapter_for(family);

        assert_ne!(
            login_shell_argument(distro, LoginShell::Nologin),
            login_shell_argument(distro, LoginShell::Bash)
        );
    }
}
