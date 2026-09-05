//! A real system account, created for one test and removed when it ends.
//!
//! Shared by the two polygon suites rather than written twice: an account left
//! behind by a failed test is a uid the next run resolves to something it did
//! not create, which is exactly the recycling `AccountIds` refuses to cache
//! against.

use std::path::{Path, PathBuf};
use std::process::Command;

use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::accounts::{AccountOperations, ProcessSystemHost};
use maran_ops::php::{ProcessPhpHost, remove_account_pools};

/// The environment variable each polygon image sets, naming itself.
const POLYGON_MARKER: &str = "MARAN_POLYGON";

/// One real hosting account on the polygon host, owned by the test that made it.
///
/// Created through the same `AccountOperations::create` the agent runs in
/// production — `useradd --create-home --user-group` with the family's nologin
/// shell — so the ids, the home directory and the primary group are the ones a
/// real customer would have, and not a hand-built approximation.
///
/// Removed in [`Drop`], including on a panic, because the test process is the
/// only thing that knows this account is disposable.
pub struct PolygonAccount {
    /// The validated name, as every operation wants it.
    name: AccountName,
    /// The ids the password database holds for it, resolved once at creation.
    ids: AccountIds,
    /// The account's home directory, as `useradd` created it.
    home: PathBuf,
    /// The distribution adapter used to create it, kept so the removal asks the
    /// same host the creation did.
    distro: &'static dyn DistroAdapter,
}

impl PolygonAccount {
    /// Refuses to go on unless this process is root inside a polygon image.
    ///
    /// A panic and not a quiet `return`. These suites are `#[ignore]`d, so the
    /// only way to reach them is to ask for them by name — and a skip would then
    /// report as a pass, which is how a suite that never ran a single `nginx -t`
    /// comes to be counted as coverage of `nginx -t` (rules/testing.md: "no tests
    /// found" is a failure, never a pass).
    ///
    /// # Panics
    ///
    /// Panics when the polygon marker is absent or the process is not root.
    pub fn require_polygon() {
        let marker = std::env::var(POLYGON_MARKER).unwrap_or_default();
        assert!(
            !marker.is_empty(),
            "these tests create real system accounts, real vhosts and real \
             directories, and must run only inside a polygon container: \
             {POLYGON_MARKER} is not set. See docker/README.md."
        );
        assert_eq!(
            rustix::process::getuid().as_raw(),
            0,
            "the polygon suites drop privileges, which requires starting as root"
        );
    }

    /// Creates the account, or fails the test saying why it could not.
    ///
    /// An account that somehow survived a previous run is removed first rather
    /// than reused: its home may hold whatever that run left in it, and a test
    /// that starts from someone else's state proves nothing about the operation
    /// it is exercising.
    ///
    /// # Panics
    ///
    /// Panics when the host is outside the support matrix, when `useradd`
    /// refuses, or when the created account cannot be resolved.
    pub fn create(username: &str) -> Self {
        Self::require_polygon();

        let name = AccountName::parse(username).expect("the fixture's own name must be valid");
        let distro = adapter_for(
            detect()
                .expect("a polygon image is a supported host")
                .family,
        );
        let operations = AccountOperations::new(ProcessSystemHost::new(distro), distro);

        // Idempotent by removal, not by reuse: see the doc comment above.
        remove_account(distro, &name);

        let created = operations
            .create(&name, 0)
            .unwrap_or_else(|error| panic!("useradd must succeed in the polygon: {error}"));
        let ids = AccountIds::resolve(&name)
            .unwrap_or_else(|error| panic!("a just-created account must resolve: {error}"));

        Self {
            name,
            ids,
            home: PathBuf::from(created.home_directory),
            distro,
        }
    }

    /// The account's validated name.
    pub fn name(&self) -> &AccountName {
        &self.name
    }

    /// The ids the password database holds for the account.
    pub fn ids(&self) -> AccountIds {
        self.ids
    }

    /// The account's home directory.
    pub fn home(&self) -> &Path {
        &self.home
    }
}

/// Removes `name`'s php-fpm pools and then the system user itself.
///
/// **Deliberately NOT `AccountOperations::delete`, and the reason is worth
/// stating.** That operation is the product's account-deletion cascade: it also
/// drops the account's databases and takes its SFTP jail down, and to do so it
/// has to reach a running MariaDB. Most polygon suites never start one — the
/// images ship the server stopped on purpose — so a fixture built on the cascade
/// would fail its teardown in every suite but two, leave the account behind, and
/// report only a line on standard error.
///
/// What that costs is bounded and covered elsewhere: the two suites that create
/// databases and SFTP logins remove them with guards of their own, and the
/// cascade itself is exercised by `account_deletion_on_a_real_host.rs`, which
/// starts the server it needs.
///
/// The pool removal is the agent's own operation, because a pool file outlives
/// `userdel` and names a user that no longer resolves — which is what takes
/// php-fpm down for every other tenant at the next reload.
///
/// # Panics
///
/// Panics when `userdel` cannot be run at all, or refuses for any reason other
/// than the account not being there.
fn remove_account(distro: &'static dyn DistroAdapter, name: &AccountName) {
    if let Err(error) = remove_account_pools(&ProcessPhpHost::new(), distro, name) {
        eprintln!(
            "the polygon account {}'s pools could not be removed: {error}",
            name.as_str()
        );
    }

    let outcome = Command::new(distro.userdel_binary())
        .args(["--remove", name.as_str()])
        .output()
        .unwrap_or_else(|error| panic!("the polygon image installs userdel: {error}"));

    // 6 is the shadow suite's E_NOTFOUND: nothing to remove is the state this
    // function wanted. 12 is "the home could not be removed in full", which a
    // container's overlay filesystem produces for a mail spool that was never
    // there; the user itself is gone, which is what matters here.
    let status = outcome.status.code().unwrap_or(-1);
    assert!(
        matches!(status, 0 | 6 | 12),
        "a leftover polygon account must be removable, userdel exited {status}: {}",
        String::from_utf8_lossy(&outcome.stderr)
    );
}

impl Drop for PolygonAccount {
    /// Removes the account and its home, whether the test passed or panicked.
    fn drop(&mut self) {
        remove_account(self.distro, &self.name);
    }
}
