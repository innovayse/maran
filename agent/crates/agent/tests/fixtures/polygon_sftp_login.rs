//! The SFTP login every real-host SFTP suite creates, and the small helpers the
//! suites share: the polygon's adapter, an account's jail, a planted customer
//! file and a readable rendering of a client's output.
//!
//! Split out of `sftp_on_a_real_host.rs` when that suite outgrew one file, so the
//! suites that now share these do not each keep a copy.

use std::path::PathBuf;
use std::process::Command;

use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::sftp::{
    AccountJail, ProcessSftpHost, SftpUserRequest, create_sftp_user, delete_sftp_user,
};

use crate::polygon_account::PolygonAccount;

/// The password every login in this suite is created with.
///
/// It uses every character class `Password` allows — letters, digits and
/// `-_.=+`. A password made only of letters would not notice `chpasswd`, PAM or
/// a pipe eating the punctuation, and the failure would be a customer who
/// cannot log in with what the panel showed them.
pub const CUSTOMER_PASSWORD: &str = "Str0ng-pass.word=+_";

/// The password a REPEATED creation asks for, and which must not take effect.
///
/// It is also the value a deliberate RESET sets, so one test can show the same
/// password being refused when a repeat offered it and accepted once a reset
/// was asked for — which is the difference between converging and clobbering.
pub const SECOND_PASSWORD: &str = "Different-2.password";

/// The file each account is given in its home, so a session has something to
/// find and a neighbour has something worth stealing.
pub const CUSTOMER_FILE: &str = "hello.txt";

/// What that file holds.
pub const CUSTOMER_CONTENT: &str = "customer data";

/// The distribution adapter for the polygon this suite is running in.
///
/// # Panics
///
/// Panics when the host is outside the support matrix, which a polygon image
/// never is.
pub fn polygon_distro() -> &'static dyn DistroAdapter {
    adapter_for(
        detect()
            .expect("a polygon image is a supported host")
            .family,
    )
}

/// The jail paths for `account`, derived exactly as the operation derives them.
pub fn jail_of(account: &AccountName) -> AccountJail {
    AccountJail::for_account(account, polygon_distro().systemd_unit_directory())
}

/// One real SFTP login, created for a test and revoked when it ends.
///
/// The login is made by the code under test. What this type adds is the
/// teardown: the login is removed and the account's bind mount taken down, so
/// one test's mount cannot be what a later test is really looking at.
pub struct PolygonSftpLogin {
    /// The hosting account the login belongs to.
    ///
    /// Kept because the deletion takes it: the operation refuses a login the
    /// account does not hold, and the account is the only thing that can tell
    /// `alice`'s login `bob` from the hosting account `alice_bob`.
    pub account: AccountName,
    /// The login's validated name.
    pub user: SftpUserName,
    /// The account's jail, kept so the teardown unmounts the right path.
    pub jail: AccountJail,
}

impl PolygonSftpLogin {
    /// Creates `account`'s `web` login through `create_sftp_user`.
    ///
    /// # Panics
    ///
    /// Panics when the operation refuses — including when the bind mount could
    /// not be made, which is what a run without `--privileged` looks like.
    pub fn create(account: &PolygonAccount) -> Self {
        let user = SftpUserName::for_account(account.name(), "web").expect("a valid login name");
        let request = SftpUserRequest {
            account: account.name().clone(),
            user: user.clone(),
            password: Password::parse(CUSTOMER_PASSWORD).expect("a valid password"),
        };

        create_sftp_user(&ProcessSftpHost::new(), polygon_distro(), &request).unwrap_or_else(
            |error| {
                panic!(
                    "creating an SFTP login must succeed in the polygon: {error}. \
                     A JailFailed here usually means the container was started \
                     without --privileged, so the bind mount could not be made."
                )
            },
        );

        Self {
            account: account.name().clone(),
            user,
            jail: jail_of(account.name()),
        }
    }

    /// The login's full name, as the host holds it.
    pub fn name(&self) -> &str {
        self.user.as_str()
    }
}

impl Drop for PolygonSftpLogin {
    /// Revokes the login and takes the account's bind mount down.
    fn drop(&mut self) {
        // A failure here cannot fail the test — a panic in `drop` during another
        // panic aborts the process and hides the real failure — so it is
        // reported and nothing more.
        if let Err(error) = delete_sftp_user(
            &ProcessSftpHost::new(),
            polygon_distro(),
            &self.account,
            &self.user,
        ) {
            eprintln!(
                "the polygon login {} could not be removed: {error}",
                self.name()
            );
        }

        let unmounted = Command::new("umount").arg(self.jail.mount_point()).output();
        if let Ok(outcome) = unmounted
            && !outcome.status.success()
        {
            eprintln!(
                "the polygon jail {} could not be unmounted: {}",
                self.jail.mount_point(),
                String::from_utf8_lossy(&outcome.stderr)
            );
        }
    }
}

/// Puts a file in `account`'s home, owned by the account, and returns its path.
pub fn plant_file(account: &PolygonAccount) -> PathBuf {
    let path = account.home().join(CUSTOMER_FILE);
    std::fs::write(&path, CUSTOMER_CONTENT).expect("the account's home must be writable by root");
    std::os::unix::fs::chown(&path, Some(account.ids().uid()), Some(account.ids().gid()))
        .expect("the planted file must belong to the account");

    path
}

/// Everything the sftp client printed, both streams together.
pub fn said(output: &std::process::Output) -> String {
    format!(
        "{}{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    )
}
